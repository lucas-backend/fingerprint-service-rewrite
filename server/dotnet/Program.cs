using FingerprintService.Application;
using FingerprintService.Configuration;
using FingerprintService.Contracts;
using FingerprintService.Data;
using FingerprintService.Diagnostics;
using FingerprintService.Errors;
using FingerprintService.Health;
using FingerprintService.Imaging;
using FingerprintService.Metrics;
using FingerprintService.Middleware;
using FingerprintService.Recognition;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------
// Configuration
// ---------------------------------------------------------

builder.Services
    .AddOptions<FingerprintOptions>()
    .Bind(builder.Configuration.GetSection(FingerprintOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var serverOptions =
    builder.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>()
    ?? new ServerOptions();

var corsSettings =
    builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>()
    ?? new CorsSettings();

// ---------------------------------------------------------
// Host / resource limits / graceful shutdown
// ---------------------------------------------------------

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = serverOptions.MaxRequestBodySizeBytes;
});

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

// ---------------------------------------------------------
// MVC + consistent validation error contract
// ---------------------------------------------------------

builder.Services.AddControllers();

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
        new BadRequestObjectResult(ErrorResponse.Create(
            ApiErrorCodes.ValidationError,
            "Invalid request.",
            context.HttpContext.TraceIdentifier));
});

// ---------------------------------------------------------
// Application services
// ---------------------------------------------------------

builder.Services.AddSingleton<R503ImageService>();
builder.Services.AddSingleton<IFingerprintRecognitionService, FingerprintRecognitionService>();
builder.Services.AddSingleton<IFingerprintDiagnosticStore, FileSystemDiagnosticStore>();

builder.Services.AddScoped<MySqlConnectionFactory>();
builder.Services.AddScoped<IFingerprintRepository, MySqlFingerprintRepository>();
builder.Services.AddScoped<EnrollmentService>();
builder.Services.AddScoped<IdentificationService>();

// Metrics (System.Diagnostics.Metrics)
builder.Services.AddMetrics();
builder.Services.AddSingleton<FingerprintMetrics>();

// ---------------------------------------------------------
// Health checks
// ---------------------------------------------------------

builder.Services
    .AddHealthChecks()
    .AddCheck<MySqlHealthCheck>("mysql", tags: new[] { "ready" });

// ---------------------------------------------------------
// Error handling
// ---------------------------------------------------------

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ---------------------------------------------------------
// CORS (only enabled when origins are explicitly configured)
// ---------------------------------------------------------

if (corsSettings.AllowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy
                .WithOrigins(corsSettings.AllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });
}

// ---------------------------------------------------------
// API documentation (served only in Development)
// ---------------------------------------------------------

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Fingerprint Service API",
        Version = "v1",
        Description = "Standalone fingerprint enrollment and identification service."
    });
});

var app = builder.Build();

// Correlation before exception handling so handler logs share the request id.
app.UseMiddleware<RequestCorrelationMiddleware>();

// Optional API-key gate (no-op when ApiKey not configured). Must run
// before exception handler so 401 is returned directly.
app.UseMiddleware<ApiKeyMiddleware>();

app.UseExceptionHandler();

// Swagger only in Development, and never when an API key gate is active
// without explicit opt-in. The Development check already excludes prod,
// but we also guard against accidentally exposing OpenAPI behind a key.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (corsSettings.AllowedOrigins.Length > 0)
{
    app.UseCors();
}

app.MapControllers();

app.Run();

/// <summary>Exposed for integration testing with WebApplicationFactory.</summary>
public partial class Program;

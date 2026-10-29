namespace FingerprintService.Middleware;

/// <summary>
/// Optional API-key gate for fingerprint endpoints. Disabled when no key is
/// configured (ApiKey is empty). When enabled, every /api/* request must
/// present X-Api-Key matching configuration. Health endpoints are exempt.
/// Enable by setting `ApiKey` via env: `ApiKey__Value` or `ApiKey:Value`.
/// </summary>
public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _config;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration config, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _config = config;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string? requiredKey = _config["ApiKey:Value"] ?? _config["ApiKey"];
        if (string.IsNullOrWhiteSpace(requiredKey))
        {
            await _next(context);
            return;
        }

        // Health checks are unauthenticated by design (load balancer probes)
        string path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/v1/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Non-API paths (e.g. swagger in dev) — pass through
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        string? presented = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(presented) || !CryptographicEquals(presented, requiredKey))
        {
            _logger.LogWarning("Rejected {Method} {Path}: missing or invalid API key RequestId={RequestId}",
                context.Request.Method, context.Request.Path, context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = new { code = "UNAUTHORIZED", message = "Missing or invalid API key." },
                requestId = context.TraceIdentifier
            });
            return;
        }

        await _next(context);
    }

    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}

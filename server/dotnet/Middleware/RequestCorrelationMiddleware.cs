namespace FingerprintService.Middleware;

/// <summary>
/// Establishes a request correlation id (using the inbound X-Request-Id header
/// when present) and exposes it to structured logs and the response.
/// </summary>
public sealed class RequestCorrelationMiddleware
{
    public const string HeaderName = "X-Request-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestCorrelationMiddleware> _logger;

    public RequestCorrelationMiddleware(
        RequestDelegate next,
        ILogger<RequestCorrelationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string requestId =
            context.Request.Headers[HeaderName].FirstOrDefault()
            ?? context.TraceIdentifier;

        context.TraceIdentifier = requestId;
        context.Response.Headers[HeaderName] = requestId;

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = requestId,
            ["TraceId"] = requestId
        }))
        {
            await _next(context);
        }
    }
}

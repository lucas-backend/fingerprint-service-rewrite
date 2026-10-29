using System.Net.Sockets;
using FingerprintService.Contracts;
using FingerprintService.Middleware;
using Microsoft.AspNetCore.Diagnostics;
using MySqlConnector;

namespace FingerprintService.Errors;

/// <summary>
/// Global exception handler. Produces a consistent error contract and logs
/// internal detail server-side. Never exposes stack traces, SQL, connection
/// strings, or filesystem paths to clients.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, code, message) = Map(exception, httpContext, cancellationToken);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unhandled exception for {Method} {Path} RequestId={RequestId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                httpContext.TraceIdentifier);
        }
        else
        {
            _logger.LogWarning(
                "Request rejected for {Method} {Path}: {Code} RequestId={RequestId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                code,
                httpContext.TraceIdentifier);
        }

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        string requestId = httpContext.TraceIdentifier;

        httpContext.Response.Clear();
        // Re-apply correlation header after Clear() (Clear removes headers set by middleware).
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            httpContext.Response.Headers[RequestCorrelationMiddleware.HeaderName] = requestId;
        }
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";

        await httpContext.Response.WriteAsJsonAsync(
            ErrorResponse.Create(code, message, string.IsNullOrWhiteSpace(requestId) ? null : requestId),
            cancellationToken);

        return true;
    }

    private static (int StatusCode, string Code, string Message) Map(
        Exception exception,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Cancellation — distinguish client abort vs unexpected
        if (exception is OperationCanceledException)
        {
            bool wasCancelled =
                cancellationToken.IsCancellationRequested
                || httpContext.RequestAborted.IsCancellationRequested;

            if (wasCancelled)
            {
                return (
                    StatusCodes.Status503ServiceUnavailable,
                    ApiErrorCodes.DatabaseUnavailable,
                    "Request was cancelled.");
            }

            return (
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError,
                "An internal error occurred.");
        }

        return exception switch
        {
            ApiException api => (api.StatusCode, api.Code, api.Message),

            // Infrastructure / DB — never leak SQL, host, or stack to client
            MySqlException => (
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.DatabaseUnavailable,
                "Database is currently unavailable. Please try again later."),

            SocketException => (
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.DatabaseUnavailable,
                "Database is currently unavailable. Please try again later."),

            TimeoutException => (
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.DatabaseUnavailable,
                "Database is currently unavailable. Please try again later."),

            // MySqlConnector protocol-level failures
            MySqlProtocolException => (
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.DatabaseUnavailable,
                "Database is currently unavailable. Please try again later."),

            InvalidOperationException invalid
                when invalid.Message.Contains("Connection string", StringComparison.OrdinalIgnoreCase)
                => (StatusCodes.Status500InternalServerError, ApiErrorCodes.ServiceMisconfigured,
                    invalid.Message),

            BadHttpRequestException bad
                when bad.StatusCode == StatusCodes.Status413PayloadTooLarge
                => (StatusCodes.Status413PayloadTooLarge, ApiErrorCodes.RequestTooLarge,
                    "Request body is too large."),

            BadHttpRequestException bad
                => (StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError,
                    "Malformed request."),

            _ => (StatusCodes.Status500InternalServerError, ApiErrorCodes.InternalError,
                "An internal error occurred.")
        };
    }
}

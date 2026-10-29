namespace FingerprintService.Errors;

/// <summary>
/// Base class for controlled application exceptions. Messages on these
/// exceptions are considered safe to return to API clients.
/// </summary>
public abstract class ApiException : Exception
{
    protected ApiException(string code, string message, int statusCode)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public int StatusCode { get; }
}

public sealed class ValidationException : ApiException
{
    public ValidationException(string code, string message)
        : base(code, message, StatusCodes.Status400BadRequest)
    {
    }
}

public sealed class ConflictException : ApiException
{
    public ConflictException(string code, string message)
        : base(code, message, StatusCodes.Status409Conflict)
    {
    }
}

public sealed class NotFoundApiException : ApiException
{
    public NotFoundApiException(string code, string message)
        : base(code, message, StatusCodes.Status404NotFound)
    {
    }
}

public sealed class DatabaseUnavailableException : ApiException
{
    public DatabaseUnavailableException(string message = "Database is currently unavailable. Please try again later.")
        : base(ApiErrorCodes.DatabaseUnavailable, message, StatusCodes.Status503ServiceUnavailable)
    {
    }
}

public sealed class ServiceMisconfiguredException : ApiException
{
    public ServiceMisconfiguredException(string message)
        : base(ApiErrorCodes.ServiceMisconfigured, message, StatusCodes.Status500InternalServerError)
    {
    }
}

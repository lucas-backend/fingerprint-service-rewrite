namespace FingerprintService.Errors;

/// <summary>
/// Stable, machine-readable error codes returned to API clients.
/// </summary>
public static class ApiErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InvalidFingerprintImage = "INVALID_FINGERPRINT_IMAGE";
    public const string InvalidTemplate = "INVALID_TEMPLATE";
    public const string InvalidBase64 = "INVALID_BASE64";
    public const string DuplicateEmployeeCode = "DUPLICATE_EMPLOYEE_CODE";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string RequestTooLarge = "REQUEST_TOO_LARGE";
    public const string NotFound = "NOT_FOUND";
    public const string InternalError = "INTERNAL_ERROR";
    public const string DatabaseUnavailable = "DATABASE_UNAVAILABLE";
    public const string ServiceMisconfigured = "SERVICE_MISCONFIGURED";
}

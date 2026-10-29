using System.Text.Json.Serialization;

namespace FingerprintService.Contracts;

/// <summary>
/// Consistent API error contract. Detail is intentionally minimal so internal
/// implementation details (stack traces, SQL, paths) are never exposed.
/// </summary>
public sealed record ErrorDetail(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("requestId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RequestId = null);

public sealed record ErrorResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error")] ErrorDetail Error)
{
    public static ErrorResponse Create(string code, string message, string? requestId = null)
        => new(false, new ErrorDetail(code, message, requestId));

    public static ErrorResponse CreateWithRequestId(string code, string message, string? requestId)
        => new(false, new ErrorDetail(code, message, requestId));
}

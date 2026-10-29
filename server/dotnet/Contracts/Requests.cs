using System.Text.Json.Serialization;

namespace FingerprintService.Contracts;

public sealed class EnrollmentRequest
{
    /// <summary>
    /// Application-level identifier of the user this fingerprint belongs to
    /// (the application's user id). This is the only user reference the
    /// fingerprint service stores.
    /// </summary>
    [JsonPropertyName("externalUserId")]
    public string ExternalUserId { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded serialized SourceAFIS template.
    /// </summary>
    [JsonPropertyName("template")]
    public string Template { get; set; } = string.Empty;

    /// <summary>
    /// Legacy compatibility only. Not stored by the fingerprint service.
    /// Ignored except that the legacy /enrollment route may use EmployeeCode
    /// as a fallback externalUserId when ExternalUserId is omitted.
    /// </summary>
    [JsonPropertyName("employeeCode")]
    public string? EmployeeCode { get; set; }

    /// <summary>Legacy compatibility only. Never stored.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class MatchRequest
{
    [JsonPropertyName("probeTemplate")]
    public string ProbeTemplate { get; set; } = string.Empty;

    [JsonPropertyName("candidateTemplate")]
    public string CandidateTemplate { get; set; } = string.Empty;
}

using System.Text.Json.Serialization;

namespace FingerprintService.Contracts;

public sealed record EnrollResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("subjectId")] long SubjectId,
    [property: JsonPropertyName("fingerprintId")] long FingerprintId,
    [property: JsonPropertyName("externalUserId")] string ExternalUserId,
    [property: JsonPropertyName("templateSize")] int TemplateSize);

/// <summary>
/// Identification result. The fingerprint service returns only its own
/// identifiers: the application-level <c>externalUserId</c> and the
/// fingerprint record id. Resolving that to a name/employee code is the
/// application server's responsibility.
/// </summary>
public sealed record IdentifyResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("matched")] bool Matched,
    [property: JsonPropertyName("score")] double? Score,
    [property: JsonPropertyName("candidateCount")] int CandidateCount,
    [property: JsonPropertyName("externalUserId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExternalUserId,
    [property: JsonPropertyName("fingerprintId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? FingerprintId,
    [property: JsonPropertyName("reason")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason);

public sealed record TemplateResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("imageSize")] int ImageSize,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("templateSize")] int TemplateSize,
    [property: JsonPropertyName("template")] string Template);

public sealed record MatchTestResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("probeTemplateSize")] int ProbeTemplateSize,
    [property: JsonPropertyName("candidateTemplateSize")] int CandidateTemplateSize,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("threshold")] double Threshold,
    [property: JsonPropertyName("matched")] bool Matched);

public sealed record DiagnosticCaptureResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("imageSize")] int ImageSize,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("min")] byte Min,
    [property: JsonPropertyName("max")] byte Max,
    [property: JsonPropertyName("average")] double Average,
    [property: JsonPropertyName("pgmSaved")] bool PgmSaved,
    [property: JsonPropertyName("pgmFile")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PgmFile);

using FingerprintService.Data;

namespace FingerprintService.Application;

/// <summary>
/// Result of a fingerprint identification attempt. Returns only fingerprint
/// service identifiers (externalUserId, fingerprintId); application data such
/// as name or employee code is resolved by the application server.
/// </summary>
public sealed record IdentificationOutcome(
    bool Matched,
    double? Score,
    int CandidateCount,
    string? ExternalUserId,
    long? FingerprintId,
    string? Reason)
{
    public static IdentificationOutcome NoCandidates() =>
        new(false, null, 0, null, null, "NO_ENROLLED_TEMPLATES");

    public static IdentificationOutcome NotMatched(double score, int candidateCount) =>
        new(false, Math.Round(score, 2), candidateCount, null, null, null);

    public static IdentificationOutcome FromMatch(
        double score,
        int candidateCount,
        CandidateTemplate candidate) =>
        new(
            true,
            Math.Round(score, 2),
            candidateCount,
            candidate.ExternalUserId,
            candidate.FingerprintId,
            null);
}

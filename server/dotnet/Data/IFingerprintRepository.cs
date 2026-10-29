namespace FingerprintService.Data;

/// <summary>
/// Persistence boundary for biometric data only. All queries target
/// <c>limen_db</c>; this interface never touches application data.
/// </summary>
public interface IFingerprintRepository
{
    Task<bool> SubjectExistsAsync(
        string externalUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ensures the subject exists (created on first enrollment) and inserts a
    /// template. Subject creation + template insert are atomic.
    /// </summary>
    Task<EnrollmentResult> EnrollAsync(
        string externalUserId,
        byte[] template,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CandidateTemplate>> GetCandidatesAsync(
        CancellationToken cancellationToken);
}

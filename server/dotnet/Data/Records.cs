namespace FingerprintService.Data;

/// <summary>
/// A candidate template loaded from limen_db for identification.
/// Carries the application-level external user id, never application data.
/// </summary>
public sealed record CandidateTemplate(
    long SubjectId,
    long FingerprintId,
    string ExternalUserId,
    byte[] Template);

public sealed record EnrollmentResult(
    long SubjectId,
    long FingerprintId,
    string ExternalUserId,
    int TemplateSize);

public sealed record SubjectRecord(
    long SubjectId,
    string ExternalUserId);

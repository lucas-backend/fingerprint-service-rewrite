using System.Diagnostics;
using FingerprintService.Data;
using FingerprintService.Errors;
using FingerprintService.Recognition;

namespace FingerprintService.Application;

/// <summary>
/// Enrollment use case: validate the external user id, obtain a SourceAFIS
/// template (from a Base64 template or a raw R503 image) and persist it in
/// limen_db. The service never stores application data.
/// </summary>
public sealed class EnrollmentService
{
    private const int MaxExternalUserIdLength = 100;
    private const int MaxTemplateBytes = 64 * 1024;

    private readonly IFingerprintRepository _repository;
    private readonly IFingerprintRecognitionService _recognition;
    private readonly ILogger<EnrollmentService> _logger;

    public EnrollmentService(
        IFingerprintRepository repository,
        IFingerprintRecognitionService recognition,
        ILogger<EnrollmentService> logger)
    {
        _repository = repository;
        _recognition = recognition;
        _logger = logger;
    }

    public async Task<EnrollmentResult> EnrollFromTemplateAsync(
        string? externalUserId,
        string? templateBase64,
        CancellationToken cancellationToken)
    {
        string validExternalUserId = ValidateExternalUserId(externalUserId);

        if (string.IsNullOrWhiteSpace(templateBase64))
        {
            throw new ValidationException(
                ApiErrorCodes.InvalidTemplate,
                "Template fingerprint wajib diisi.");
        }

        byte[] template;

        try
        {
            template = Convert.FromBase64String(templateBase64);
        }
        catch (FormatException)
        {
            throw new ValidationException(
                ApiErrorCodes.InvalidBase64,
                "Template bukan Base64 yang valid.");
        }

        ValidateTemplateBytes(template);

        return await PersistAsync(validExternalUserId, template, cancellationToken);
    }

    public async Task<EnrollmentResult> EnrollFromR503Async(
        string? externalUserId,
        byte[] r503Data,
        CancellationToken cancellationToken)
    {
        string validExternalUserId = ValidateExternalUserId(externalUserId);

        ArgumentNullException.ThrowIfNull(r503Data);

        if (r503Data.Length != Imaging.R503ImageFormat.PackedSize)
        {
            throw new ValidationException(
                ApiErrorCodes.InvalidFingerprintImage,
                "Invalid fingerprint image.");
        }

        byte[] template;

        try
        {
            template = _recognition.CreateTemplateFromR503(r503Data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Enrollment template generation failed: {Reason}", ex.GetType().Name);

            throw new ValidationException(
                ApiErrorCodes.InvalidFingerprintImage,
                "Invalid fingerprint image.");
        }

        ValidateTemplateBytes(template);

        return await PersistAsync(validExternalUserId, template, cancellationToken);
    }

    private async Task<EnrollmentResult> PersistAsync(
        string externalUserId,
        byte[] template,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        EnrollmentResult result =
            await _repository.EnrollAsync(externalUserId, template, cancellationToken);

        stopwatch.Stop();

        _logger.LogInformation(
            "Enrollment completed. SubjectId={SubjectId} FingerprintId={FingerprintId} " +
            "ExternalUserId={ExternalUserId} TemplateSize={TemplateSize} DurationMs={DurationMs}",
            result.SubjectId,
            result.FingerprintId,
            result.ExternalUserId,
            result.TemplateSize,
            stopwatch.ElapsedMilliseconds);

        return result;
    }

    private static string ValidateExternalUserId(string? externalUserId)
    {
        string value = (externalUserId ?? string.Empty).Trim();

        if (value.Length == 0)
        {
            throw new ValidationException(
                ApiErrorCodes.ValidationError,
                "ExternalUserId wajib diisi.");
        }

        if (value.Length > MaxExternalUserIdLength)
        {
            throw new ValidationException(
                ApiErrorCodes.ValidationError,
                $"ExternalUserId maksimum {MaxExternalUserIdLength} karakter.");
        }

        return value;
    }

    private void ValidateTemplateBytes(byte[] template)
    {
        if (template.Length == 0)
        {
            throw new ValidationException(
                ApiErrorCodes.InvalidTemplate,
                "Template kosong.");
        }

        if (template.Length > MaxTemplateBytes)
        {
            throw new ValidationException(
                ApiErrorCodes.InvalidTemplate,
                "Template terlalu besar.");
        }

        try
        {
            _recognition.ValidateTemplate(template);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Template validation failed: {Reason}", ex.GetType().Name);

            throw new ValidationException(
                ApiErrorCodes.InvalidTemplate,
                "Template fingerprint tidak valid.");
        }
    }
}

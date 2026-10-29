using FingerprintService.Application;
using FingerprintService.Contracts;
using FingerprintService.Errors;
using Microsoft.AspNetCore.Mvc;

namespace FingerprintService.Controllers;

/// <summary>
/// Legacy enrollment endpoint (Base64 template). Kept for backward
/// compatibility.
///
/// The fingerprint service does not store application data. The compatibility
/// bridge resolves the subject from <c>externalUserId</c> when provided,
/// otherwise falls back to the legacy <c>employeeCode</c> as the external
/// identifier. The <c>name</c> field is ignored and never stored.
/// </summary>
[ApiController]
[Route("enrollment")]
public sealed class EnrollmentController : ControllerBase
{
    private readonly EnrollmentService _enrollmentService;

    public EnrollmentController(EnrollmentService enrollmentService)
    {
        _enrollmentService = enrollmentService;
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Enroll(
        [FromBody] EnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        string? externalUserId = request.ExternalUserId;

        if (string.IsNullOrWhiteSpace(externalUserId))
        {
            throw new ValidationException(
                ApiErrorCodes.ValidationError,
                "ExternalUserId (users.id) wajib diisi.");
        }

        var result = await _enrollmentService.EnrollFromTemplateAsync(
            externalUserId,
            request.Template,
            cancellationToken);

        return Ok(new
        {
            success = true,
            subjectId = result.SubjectId,
            fingerprintId = result.FingerprintId,
            externalUserId = result.ExternalUserId,
            templateSize = result.TemplateSize
        });
    }
}

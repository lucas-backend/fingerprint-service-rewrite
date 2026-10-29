using FingerprintService.Application;
using FingerprintService.Contracts;
using FingerprintService.Data;
using FingerprintService.Diagnostics;
using FingerprintService.Imaging;
using FingerprintService.Infrastructure;
using FingerprintService.Recognition;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace FingerprintService.Controllers;

/// <summary>
/// Versioned production API. All user references are the application-level
/// <c>externalUserId</c>; the service never accepts or returns application
/// data such as name or employee code.
/// </summary>
[ApiController]
[Route("api/v1/fingerprints")]
public sealed class FingerprintsController : ControllerBase
{
    private readonly EnrollmentService _enrollmentService;
    private readonly IdentificationService _identificationService;
    private readonly R503ImageService _imageService;
    private readonly IFingerprintDiagnosticStore _diagnosticStore;

    public FingerprintsController(
        EnrollmentService enrollmentService,
        IdentificationService identificationService,
        R503ImageService imageService,
        IFingerprintDiagnosticStore diagnosticStore)
    {
        _enrollmentService = enrollmentService;
        _identificationService = identificationService;
        _imageService = imageService;
        _diagnosticStore = diagnosticStore;
    }

    [HttpPost("enroll")]
    [Consumes("application/json")]
    public async Task<ActionResult<EnrollResponse>> Enroll(
        [FromBody] EnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _enrollmentService.EnrollFromTemplateAsync(
            request.ExternalUserId,
            request.Template,
            cancellationToken);

        return Ok(ToResponse(result));
    }

    /// <summary>
    /// Enrolls directly from a raw R503 image. The preferred flow: the binary
    /// template never leaves the service.
    /// </summary>
    [HttpPost("enroll/raw")]
    public async Task<ActionResult<EnrollResponse>> EnrollRaw(
        [FromQuery] string? externalUserId,
        CancellationToken cancellationToken)
    {
        RawBodyReader.RequireOctetStream(Request);

        byte[] r503Data = await RawBodyReader.ReadExactlyAsync(
            Request,
            R503ImageFormat.PackedSize,
            cancellationToken);

        var result = await _enrollmentService.EnrollFromR503Async(
            externalUserId,
            r503Data,
            cancellationToken);

        return Ok(ToResponse(result));
    }

    [HttpPost("identify")]
    public async Task<ActionResult<IdentifyResponse>> Identify(
        CancellationToken cancellationToken)
    {
        RawBodyReader.RequireOctetStream(Request);

        byte[] r503Data = await RawBodyReader.ReadExactlyAsync(
            Request,
            R503ImageFormat.PackedSize,
            cancellationToken);

        var outcome = await _identificationService.IdentifyAsync(
            r503Data,
            cancellationToken);

        return Ok(new IdentifyResponse(
            true,
            outcome.Matched,
            outcome.Score,
            outcome.CandidateCount,
            outcome.ExternalUserId,
            outcome.FingerprintId,
            outcome.Reason));
    }

    /// <summary>
    /// Diagnostic capture. Never writes to disk unless explicitly enabled via
    /// Fingerprint:Diagnostics:SavePgm. In production, the route returns 404
    /// when diagnostics are disabled to reduce surface & disk/biometric exposure.
    /// </summary>
    [HttpPost("diagnostics/capture")]
    public async Task<ActionResult<DiagnosticCaptureResponse>> Capture(
        CancellationToken cancellationToken)
    {
        if (!_diagnosticStore.IsEnabled && !HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
        {
            return NotFound();
        }

        RawBodyReader.RequireOctetStream(Request);

        byte[] r503Data = await RawBodyReader.ReadAllAsync(Request, cancellationToken);

        _imageService.ValidatePackedSize(r503Data.Length);

        byte[] grayscale = _imageService.UnpackImage(r503Data);
        ImageStatistics statistics = _imageService.GetStatistics(grayscale);

        string? pgmFile =
            await _diagnosticStore.SavePgmAsync(grayscale, cancellationToken);

        return Ok(new DiagnosticCaptureResponse(
            true,
            r503Data.Length,
            R503ImageFormat.Width,
            R503ImageFormat.Height,
            statistics.Min,
            statistics.Max,
            Math.Round(statistics.Average, 2),
            pgmFile is not null,
            pgmFile));
    }

    private static EnrollResponse ToResponse(EnrollmentResult result) =>
        new(
            true,
            result.SubjectId,
            result.FingerprintId,
            result.ExternalUserId,
            result.TemplateSize);
}

using FingerprintService.Contracts;
using FingerprintService.Diagnostics;
using FingerprintService.Errors;
using FingerprintService.Imaging;
using FingerprintService.Infrastructure;
using FingerprintService.Recognition;
using Microsoft.AspNetCore.Mvc;

namespace FingerprintService.Controllers;

/// <summary>
/// Legacy/testing endpoints. These are retained for backward compatibility.
/// In production, restrict access to them at the network/reverse-proxy layer
/// or avoid deploying them publicly.
/// </summary>
[ApiController]
[Route("fingerprint")]
public sealed class FingerprintController : ControllerBase
{
    private readonly IFingerprintRecognitionService _recognition;
    private readonly R503ImageService _imageService;
    private readonly IFingerprintDiagnosticStore _diagnosticStore;

    public FingerprintController(
        IFingerprintRecognitionService recognition,
        R503ImageService imageService,
        IFingerprintDiagnosticStore diagnosticStore)
    {
        _recognition = recognition;
        _imageService = imageService;
        _diagnosticStore = diagnosticStore;
    }

    /// <summary>
    /// Diagnostic unpack endpoint. .pgm is only written when diagnostics are
    /// explicitly enabled in configuration.
    /// </summary>
    [HttpPost("raw")]
    public async Task<IActionResult> ReceiveRaw(CancellationToken cancellationToken)
    {
        byte[] r503Data = await RawBodyReader.ReadAllAsync(Request, cancellationToken);

        _imageService.ValidatePackedSize(r503Data.Length);

        byte[] grayscale = _imageService.UnpackImage(r503Data);

        string? pgmFile =
            await _diagnosticStore.SavePgmAsync(grayscale, cancellationToken);

        return Ok(new
        {
            success = true,
            imageSize = r503Data.Length,
            width = R503ImageFormat.Width,
            height = R503ImageFormat.Height,
            pgmFile
        });
    }

    [HttpPost("template")]
    [RequestSizeLimit(1_000_000)]
    public async Task<IActionResult> CreateTemplateFromR503(CancellationToken cancellationToken)
    {
        byte[] r503Data = await RawBodyReader.ReadExactlyAsync(
            Request,
            R503ImageFormat.PackedSize,
            cancellationToken);

        byte[] template = _recognition.CreateTemplateFromR503(r503Data);

        return Ok(new
        {
            success = true,
            imageSize = r503Data.Length,
            width = R503ImageFormat.Width,
            height = R503ImageFormat.Height,
            templateSize = template.Length,
            template = Convert.ToBase64String(template)
        });
    }

    [HttpPost("match-test")]
    public IActionResult MatchTest([FromBody] MatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProbeTemplate))
        {
            throw new ValidationException(
                ApiErrorCodes.InvalidTemplate,
                "ProbeTemplate kosong.");
        }

        if (string.IsNullOrWhiteSpace(request.CandidateTemplate))
        {
            throw new ValidationException(
                ApiErrorCodes.InvalidTemplate,
                "CandidateTemplate kosong.");
        }

        byte[] probeTemplate = Decode(request.ProbeTemplate);
        byte[] candidateTemplate = Decode(request.CandidateTemplate);

        double score;

        try
        {
            score = _recognition.Match(probeTemplate, candidateTemplate);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ValidationException(
                ApiErrorCodes.InvalidTemplate,
                "Template fingerprint tidak valid.");
        }

        double rounded = Math.Round(score, 2);

        return Ok(new MatchTestResponse(
            true,
            probeTemplate.Length,
            candidateTemplate.Length,
            rounded,
            _recognition.MatchThreshold,
            _recognition.IsMatch(score)));
    }

    private static byte[] Decode(string base64)
    {
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new ValidationException(
                ApiErrorCodes.InvalidBase64,
                "Template bukan Base64 yang valid.");
        }
    }
}

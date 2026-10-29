using FingerprintService.Application;
using FingerprintService.Imaging;
using FingerprintService.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace FingerprintService.Controllers;

/// <summary>
/// Legacy identification endpoint kept for backward compatibility with the
/// existing binary contract. The response now carries <c>externalUserId</c>;
/// application data (name/employeeCode) must be resolved by the caller.
/// </summary>
[ApiController]
[Route("identification")]
public sealed class IdentificationController : ControllerBase
{
    private readonly IdentificationService _identificationService;

    public IdentificationController(IdentificationService identificationService)
    {
        _identificationService = identificationService;
    }

    [HttpPost]
    public async Task<IActionResult> Identify(CancellationToken cancellationToken)
    {
        RawBodyReader.RequireOctetStream(Request);

        byte[] r503Data = await RawBodyReader.ReadExactlyAsync(
            Request,
            R503ImageFormat.PackedSize,
            cancellationToken);

        var outcome = await _identificationService.IdentifyAsync(
            r503Data,
            cancellationToken);

        if (outcome.CandidateCount == 0)
        {
            return NotFound(new
            {
                success = false,
                matched = false,
                message = "Belum ada fingerprint yang terdaftar."
            });
        }

        if (!outcome.Matched)
        {
            return Ok(new
            {
                success = true,
                matched = false,
                message = "Fingerprint tidak dikenali.",
                score = outcome.Score,
                candidateCount = outcome.CandidateCount
            });
        }

        return Ok(new
        {
            success = true,
            matched = true,
            externalUserId = outcome.ExternalUserId,
            fingerprintId = outcome.FingerprintId,
            score = outcome.Score,
            candidateCount = outcome.CandidateCount
        });
    }
}

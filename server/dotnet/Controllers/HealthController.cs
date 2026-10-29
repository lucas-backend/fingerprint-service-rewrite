using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FingerprintService.Controllers;

[ApiController]
public sealed class HealthController : ControllerBase
{
    private const string ServiceName = "fingerprint-service";

    private readonly HealthCheckService _healthCheckService;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        HealthCheckService healthCheckService,
        ILogger<HealthController> logger)
    {
        _healthCheckService = healthCheckService;
        _logger = logger;
    }

    /// <summary>Legacy liveness endpoint (shape preserved).</summary>
    [HttpGet("health")]
    public IActionResult LegacyHealth()
    {
        return Ok(new { status = "ok", service = ServiceName });
    }

    /// <summary>Liveness probe: the process is running.</summary>
    [HttpGet("health/live")]
    public IActionResult Live()
    {
        return Ok(new { status = "ok", service = ServiceName });
    }

    /// <summary>
    /// Readiness probe: the service can serve fingerprint requests, including
    /// required dependencies (currently MySQL).
    /// </summary>
    [HttpGet("health/ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        HealthReport report =
            await _healthCheckService.CheckHealthAsync(cancellationToken);

        bool ready = report.Status == HealthStatus.Healthy;

        var payload = new
        {
            status = ready ? "ready" : "unready",
            service = ServiceName,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString().ToLowerInvariant()
            })
        };

        if (!ready)
        {
            _logger.LogWarning(
                "Readiness check reported {Status}.",
                report.Status);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
        }

        return Ok(payload);
    }

    /// <summary>Versioned health summary.</summary>
    [HttpGet("api/v1/health")]
    public IActionResult Versioned()
    {
        return Ok(new
        {
            status = "ok",
            service = ServiceName,
            version = "v1"
        });
    }
}

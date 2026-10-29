using FingerprintService.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySqlConnector;

namespace FingerprintService.Health;

/// <summary>
/// Lightweight readiness probe: verifies MySQL is reachable. Deliberately does
/// not perform any fingerprint processing.
/// </summary>
public sealed class MySqlHealthCheck : IHealthCheck
{
    private readonly MySqlConnectionFactory _connectionFactory;
    private readonly ILogger<MySqlHealthCheck> _logger;

    public MySqlHealthCheck(
        MySqlConnectionFactory connectionFactory,
        ILogger<MySqlHealthCheck> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_connectionFactory.IsConfigured)
        {
            return HealthCheckResult.Unhealthy("Database connection string is not configured.");
        }

        try
        {
            await using var connection = _connectionFactory.CreateConnection();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            await connection.OpenAsync(timeoutCts.Token);

            await using var command = new MySqlCommand("SELECT 1;", connection);
            using var cmdTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cmdTimeout.Token);
            await command.ExecuteScalarAsync(linked.Token);

            return HealthCheckResult.Healthy("MySQL reachable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("MySQL health check cancelled.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("MySQL health check timed out.");
            return HealthCheckResult.Unhealthy("MySQL health check timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MySQL health check failed.");

            // Detail intentionally omitted from the returned description.
            return HealthCheckResult.Unhealthy("MySQL connection failed.");
        }
    }
}

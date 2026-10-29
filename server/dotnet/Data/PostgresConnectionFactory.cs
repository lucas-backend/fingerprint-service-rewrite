using FingerprintService.Errors;
using Npgsql;

namespace FingerprintService.Data;

/// <summary>
/// Creates PostgreSQL connections from configuration. The connection string must be
/// supplied via configuration (environment variable / secret store in
/// production) and is never logged.
/// </summary>
public sealed class PostgresConnectionFactory
{
    private readonly string _connectionString;

    public PostgresConnectionFactory(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("Postgres")
            ?? string.Empty;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    public NpgsqlConnection CreateConnection()
    {
        if (!IsConfigured)
        {
            throw new ServiceMisconfiguredException(
                "PostgreSQL connection string is not configured. " +
                "Provide it via configuration (e.g. ConnectionStrings__Postgres).");
        }

        return new NpgsqlConnection(_connectionString);
    }
}

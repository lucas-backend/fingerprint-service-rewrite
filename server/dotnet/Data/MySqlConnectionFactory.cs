using FingerprintService.Errors;
using MySqlConnector;

namespace FingerprintService.Data;

/// <summary>
/// Creates MySQL connections from configuration. The connection string must be
/// supplied via configuration (environment variable / secret store in
/// production) and is never logged.
/// </summary>
public sealed class MySqlConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("MySql")
            ?? string.Empty;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    public MySqlConnection CreateConnection()
    {
        if (!IsConfigured)
        {
            throw new ServiceMisconfiguredException(
                "MySQL connection string is not configured. " +
                "Provide it via configuration (e.g. ConnectionStrings__MySql).");
        }

        return new MySqlConnection(_connectionString);
    }
}

using FingerprintService.Configuration;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace FingerprintService.Data;

/// <summary>
/// MySQL persistence for biometric data. All SQL targets fingerprint tables
/// (fingerprint_subjects, fingerprint_templates) in the unified limen_db.
/// There is no access to application data.
/// </summary>
public sealed class MySqlFingerprintRepository : IFingerprintRepository
{
    private readonly MySqlConnectionFactory _connectionFactory;
    private readonly FingerprintOptions _options;

    public MySqlFingerprintRepository(
        MySqlConnectionFactory connectionFactory,
        IOptions<FingerprintOptions> options)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
    }

    public async Task<bool> SubjectExistsAsync(
        string externalUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id
            FROM fingerprint_subjects
            WHERE external_user_id = @externalUserId
            LIMIT 1;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@externalUserId", externalUserId);

        object? result = await command.ExecuteScalarAsync(cancellationToken);

        return result is not null && result is not DBNull;
    }

    public async Task<EnrollmentResult> EnrollAsync(
        string externalUserId,
        byte[] template,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Create the subject on first enrollment, otherwise reuse it.
            // The LAST_INSERT_ID(id) idiom returns the existing id on conflict.
            const string subjectSql = """
                INSERT INTO fingerprint_subjects (external_user_id)
                VALUES (@externalUserId)
                ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id);

                SELECT LAST_INSERT_ID();
                """;

            long subjectId;

            await using (var subjectCommand = new MySqlCommand(subjectSql, connection, transaction))
            {
                subjectCommand.Parameters.AddWithValue("@externalUserId", externalUserId);

                object? result = await subjectCommand.ExecuteScalarAsync(cancellationToken);

                subjectId = Convert.ToInt64(result);
            }

            const string templateSql = """
                INSERT INTO fingerprint_templates (subject_id, template)
                VALUES (@subjectId, @template);

                SELECT LAST_INSERT_ID();
                """;

            long fingerprintId;

            await using (var templateCommand = new MySqlCommand(templateSql, connection, transaction))
            {
                templateCommand.Parameters.AddWithValue("@subjectId", subjectId);
                templateCommand.Parameters
                    .Add("@template", MySqlDbType.LongBlob)
                    .Value = template;

                object? result = await templateCommand.ExecuteScalarAsync(cancellationToken);

                fingerprintId = Convert.ToInt64(result);
            }

            // Enforce MaxTemplatesPerSubject: evict oldest when over limit
            int max = _options.MaxTemplatesPerSubject;
            if (max > 0)
            {
                const string countSql = "SELECT COUNT(*) FROM fingerprint_templates WHERE subject_id = @subjectId;";
                await using var countCmd = new MySqlCommand(countSql, connection, transaction);
                countCmd.Parameters.AddWithValue("@subjectId", subjectId);
                int count = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken));
                if (count > max)
                {
                    int toDelete = count - max;
                    const string deleteSql = """
                        DELETE FROM fingerprint_templates
                        WHERE subject_id = @subjectId
                        ORDER BY id ASC
                        LIMIT @toDelete;
                        """;
                    await using var delCmd = new MySqlCommand(deleteSql, connection, transaction);
                    delCmd.Parameters.AddWithValue("@subjectId", subjectId);
                    delCmd.Parameters.AddWithValue("@toDelete", toDelete);
                    await delCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);

            return new EnrollmentResult(
                subjectId,
                fingerprintId,
                externalUserId,
                template.Length);
        }
        catch
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<CandidateTemplate>> GetCandidatesAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                s.id AS subject_id,
                s.external_user_id,
                t.id AS fingerprint_id,
                t.template
            FROM fingerprint_subjects s
            INNER JOIN fingerprint_templates t
                ON t.subject_id = s.id
            ORDER BY s.id, t.id;
            """;

        var candidates = new List<CandidateTemplate>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new CandidateTemplate(
                reader.GetInt64(reader.GetOrdinal("subject_id")),
                reader.GetInt64(reader.GetOrdinal("fingerprint_id")),
                reader.GetString(reader.GetOrdinal("external_user_id")),
                (byte[])reader["template"]));
        }

        return candidates;
    }

    private static async Task SafeRollbackAsync(
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch
        {
            // Rollback best-effort; the original exception is more important.
        }
    }
}

using FingerprintService.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace FingerprintService.Data;

/// <summary>
/// PostgreSQL persistence for biometric data. All SQL targets fingerprint tables
/// (fingerprint_subjects, fingerprint_templates) in the unified database.
/// There is no access to application data.
/// </summary>
public sealed class PostgresFingerprintRepository : IFingerprintRepository
{
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly FingerprintOptions _options;

    public PostgresFingerprintRepository(
        PostgresConnectionFactory connectionFactory,
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
            WHERE user_id = @userId
            LIMIT 1;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@userId", NpgsqlDbType.Uuid, ParseUserId(externalUserId));

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
            const string subjectSql = """
                INSERT INTO fingerprint_subjects (user_id)
                VALUES (@userId)
                ON CONFLICT (user_id) DO UPDATE SET user_id = EXCLUDED.user_id
                RETURNING id;
                """;

            long subjectId;

            await using (var subjectCommand = new NpgsqlCommand(subjectSql, connection, transaction))
            {
                subjectCommand.Parameters.AddWithValue("@userId", NpgsqlDbType.Uuid, ParseUserId(externalUserId));

                object? result = await subjectCommand.ExecuteScalarAsync(cancellationToken);

                subjectId = Convert.ToInt64(result);
            }

            const string templateSql = """
                INSERT INTO fingerprint_templates (subject_id, template)
                VALUES (@subjectId, @template)
                RETURNING id;
                """;

            long fingerprintId;

            await using (var templateCommand = new NpgsqlCommand(templateSql, connection, transaction))
            {
                templateCommand.Parameters.AddWithValue("@subjectId", NpgsqlDbType.Bigint, subjectId);
                templateCommand.Parameters
                    .Add("@template", NpgsqlDbType.Bytea)
                    .Value = template;

                object? result = await templateCommand.ExecuteScalarAsync(cancellationToken);

                fingerprintId = Convert.ToInt64(result);
            }

            // Enforce MaxTemplatesPerSubject: evict oldest when over limit
            int max = _options.MaxTemplatesPerSubject;
            if (max > 0)
            {
                const string countSql = "SELECT COUNT(*) FROM fingerprint_templates WHERE subject_id = @subjectId;";
                await using var countCmd = new NpgsqlCommand(countSql, connection, transaction);
                countCmd.Parameters.AddWithValue("@subjectId", NpgsqlDbType.Bigint, subjectId);
                int count = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken));
                if (count > max)
                {
                    int toDelete = count - max;
                    const string deleteSql = """
                        DELETE FROM fingerprint_templates
                        WHERE id IN (
                            SELECT id
                            FROM fingerprint_templates
                            WHERE subject_id = @subjectId
                            ORDER BY id ASC
                            LIMIT @toDelete
                        );
                        """;
                    await using var delCmd = new NpgsqlCommand(deleteSql, connection, transaction);
                    delCmd.Parameters.AddWithValue("@subjectId", NpgsqlDbType.Bigint, subjectId);
                    delCmd.Parameters.AddWithValue("@toDelete", NpgsqlDbType.Integer, toDelete);
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
                s.user_id,
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

        await using var command = new NpgsqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new CandidateTemplate(
                reader.GetInt64(reader.GetOrdinal("subject_id")),
                reader.GetInt64(reader.GetOrdinal("fingerprint_id")),
                reader.GetGuid(reader.GetOrdinal("user_id")).ToString(),
                (byte[])reader["template"]));
        }

        return candidates;
    }

    private static Guid ParseUserId(string userId)
    {
        if (!Guid.TryParse(userId, out Guid parsed))
        {
            throw new InvalidOperationException("The fingerprint user id must be a UUID.");
        }

        return parsed;
    }

    private static async Task SafeRollbackAsync(
        NpgsqlTransaction transaction,
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

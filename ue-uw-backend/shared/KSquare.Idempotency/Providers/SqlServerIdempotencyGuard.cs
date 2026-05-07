using KSquare.Idempotency.Configuration;
using KSquare.Idempotency.Contracts;
using KSquare.Idempotency.Models;
using Microsoft.Data.SqlClient;

namespace KSquare.Idempotency.Providers;

public sealed class SqlServerIdempotencyGuard(IdempotencyOptions options) : IIdempotencyGuard
{
    public async Task<IdempotencyResult?> GetAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("IdempotencyOptions.ConnectionString must be configured for SqlServer provider.");
        }

        const string sql = """
            SELECT status_code, response_body, content_type, processed_at
            FROM idempotency_keys
            WHERE [key] = @key AND expires_at > SYSUTCDATETIME();
            """;

        await using var conn = new SqlConnection(options.ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@key", key);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var statusCode = reader.GetInt32(0);
        var responseBody = reader.GetString(1);
        var contentType = reader.GetString(2);
        var processedAt = reader.GetDateTimeOffset(3);

        return new IdempotencyResult(statusCode, responseBody, contentType, processedAt);
    }

    public async Task SetAsync(string key, IdempotencyResult result, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("IdempotencyOptions.ConnectionString must be configured for SqlServer provider.");
        }

        if (result.StatusCode < 0)
        {
            const string deleteSql = "DELETE FROM idempotency_keys WHERE [key] = @key;";

            await using var deleteConn = new SqlConnection(options.ConnectionString);
            await deleteConn.OpenAsync(ct).ConfigureAwait(false);

            await using var deleteCmd = new SqlCommand(deleteSql, deleteConn);
            deleteCmd.Parameters.AddWithValue("@key", key);
            await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        var effectiveTtl = ttl ?? options.DefaultHttpKeyTtl;
        var expiresAt = DateTimeOffset.UtcNow.Add(effectiveTtl);

        var sql = result.StatusCode == 0
            ? """
                INSERT INTO idempotency_keys
                    ([key], status_code, response_body, content_type, processed_at, expires_at)
                VALUES
                    (@key, @status_code, @response_body, @content_type, @processed_at, @expires_at);
                """
            : """
                BEGIN TRY
                    INSERT INTO idempotency_keys
                        ([key], status_code, response_body, content_type, processed_at, expires_at)
                    VALUES
                        (@key, @status_code, @response_body, @content_type, @processed_at, @expires_at);
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() IN (2601, 2627)
                    BEGIN
                        UPDATE idempotency_keys
                        SET status_code = @status_code,
                            response_body = @response_body,
                            content_type = @content_type,
                            processed_at = @processed_at,
                            expires_at = @expires_at
                        WHERE [key] = @key;
                    END
                    ELSE
                    BEGIN
                        THROW;
                    END
                END CATCH
                """;

        await using var conn = new SqlConnection(options.ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@status_code", result.StatusCode);
        cmd.Parameters.AddWithValue("@response_body", result.ResponseBody);
        cmd.Parameters.AddWithValue("@content_type", result.ContentType);
        cmd.Parameters.AddWithValue("@processed_at", result.ProcessedAt);
        cmd.Parameters.AddWithValue("@expires_at", expiresAt);

        if (result.StatusCode == 0)
        {
            try
            {
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
            }

            return;
        }

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> TryMarkProcessedAsync(string messageId, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("IdempotencyOptions.ConnectionString must be configured for SqlServer provider.");
        }

        var effectiveTtl = ttl ?? options.DefaultConsumerKeyTtl;
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(effectiveTtl);

        const string sql = """
            SET NOCOUNT ON;

            BEGIN TRANSACTION;

            DELETE FROM idempotency_consumer_keys
            WHERE message_id = @message_id AND expires_at <= SYSUTCDATETIME();

            INSERT INTO idempotency_consumer_keys (message_id, processed_at, expires_at)
            SELECT @message_id, @processed_at, @expires_at
            WHERE NOT EXISTS (
                SELECT 1
                FROM idempotency_consumer_keys WITH (UPDLOCK, HOLDLOCK)
                WHERE message_id = @message_id
            );

            SELECT @@ROWCOUNT;

            COMMIT TRANSACTION;
            """;

        await using var conn = new SqlConnection(options.ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@message_id", messageId);
        cmd.Parameters.AddWithValue("@processed_at", now);
        cmd.Parameters.AddWithValue("@expires_at", expiresAt);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(result) == 1;
    }
}

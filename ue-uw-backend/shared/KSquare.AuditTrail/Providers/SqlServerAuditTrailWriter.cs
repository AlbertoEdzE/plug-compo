using System.Data;
using System.Text.Json;
using KSquare.AuditTrail.Configuration;
using KSquare.AuditTrail.Contracts;
using KSquare.AuditTrail.Internal;
using KSquare.AuditTrail.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace KSquare.AuditTrail.Providers;

internal sealed class SqlServerAuditTrailWriter(
    AuditTrailOptions options,
    PiiMaskingSerializer masking,
    ILogger<SqlServerAuditTrailWriter> logger
) : IAuditTrailWriter
{
    public async Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException("ConnectionString is required for SqlServer provider.");
            }

            var stored = entry with
            {
                ServiceName = entry.ServiceName ?? options.ServiceName,
                Before = masking.MaskJson(entry.Before),
                After = masking.MaskJson(entry.After)
            };

            await using var conn = new SqlConnection(options.ConnectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = """
                INSERT INTO audit_trail (
                    entry_id,
                    resource_type,
                    resource_id,
                    action,
                    actor_user_id,
                    actor_name,
                    actor_role,
                    actor_type,
                    before_json,
                    after_json,
                    correlation_id,
                    service_name,
                    tags_json,
                    occurred_at
                )
                VALUES (
                    @entry_id,
                    @resource_type,
                    @resource_id,
                    @action,
                    @actor_user_id,
                    @actor_name,
                    @actor_role,
                    @actor_type,
                    @before_json,
                    @after_json,
                    @correlation_id,
                    @service_name,
                    @tags_json,
                    @occurred_at
                );
                """;

            cmd.Parameters.AddWithValue("@entry_id", stored.EntryId);
            cmd.Parameters.AddWithValue("@resource_type", stored.ResourceType);
            cmd.Parameters.AddWithValue("@resource_id", stored.ResourceId);
            cmd.Parameters.AddWithValue("@action", stored.Action);
            cmd.Parameters.AddWithValue("@actor_user_id", stored.Actor.UserId);
            cmd.Parameters.AddWithValue("@actor_name", stored.Actor.DisplayName);
            cmd.Parameters.AddWithValue("@actor_role", (object?)stored.Actor.Role ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@actor_type", stored.Actor.ActorType.ToString());
            cmd.Parameters.AddWithValue("@before_json", (object?)stored.Before ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@after_json", (object?)stored.After ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@correlation_id", (object?)stored.CorrelationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@service_name", (object?)stored.ServiceName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tags_json", stored.Tags is null ? DBNull.Value : JsonSerializer.Serialize(stored.Tags));
            cmd.Parameters.AddWithValue("@occurred_at", stored.OccurredAt);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audit write dropped for {ResourceType}/{ResourceId}", entry.ResourceType, entry.ResourceId);
        }
    }

    public async Task WriteChangeAsync<T>(
        string resourceType,
        string resourceId,
        string action,
        T? before,
        T? after,
        AuditActor actor,
        string? correlationId = null,
        CancellationToken ct = default
    )
        where T : class
    {
        try
        {
            var beforeJson = before is null ? null : JsonSerializer.Serialize(before);
            var afterJson = after is null ? null : JsonSerializer.Serialize(after);

            await WriteAsync(new AuditEntry
            {
                ResourceType = resourceType,
                ResourceId = resourceId,
                Action = action,
                Actor = actor,
                Before = beforeJson,
                After = afterJson,
                CorrelationId = correlationId
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audit change write dropped for {ResourceType}/{ResourceId}", resourceType, resourceId);
        }
    }

    public async IAsyncEnumerable<AuditEntry> QueryAsync(AuditQuery query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            yield break;
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 50 : query.PageSize;
        var offset = (page - 1) * pageSize;

        var where = new List<string>();
        var parameters = new List<SqlParameter>();

        if (!string.IsNullOrWhiteSpace(query.ResourceType))
        {
            where.Add("resource_type = @resource_type");
            parameters.Add(new SqlParameter("@resource_type", SqlDbType.NVarChar, 100) { Value = query.ResourceType });
        }

        if (!string.IsNullOrWhiteSpace(query.ResourceId))
        {
            where.Add("resource_id = @resource_id");
            parameters.Add(new SqlParameter("@resource_id", SqlDbType.NVarChar, 500) { Value = query.ResourceId });
        }

        if (!string.IsNullOrWhiteSpace(query.ActorUserId))
        {
            where.Add("actor_user_id = @actor_user_id");
            parameters.Add(new SqlParameter("@actor_user_id", SqlDbType.NVarChar, 500) { Value = query.ActorUserId });
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            where.Add("action = @action");
            parameters.Add(new SqlParameter("@action", SqlDbType.NVarChar, 200) { Value = query.Action });
        }

        if (query.From is not null)
        {
            where.Add("occurred_at >= @from");
            parameters.Add(new SqlParameter("@from", SqlDbType.DateTimeOffset) { Value = query.From.Value });
        }

        if (query.To is not null)
        {
            where.Add("occurred_at <= @to");
            parameters.Add(new SqlParameter("@to", SqlDbType.DateTimeOffset) { Value = query.To.Value });
        }

        var whereClause = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);

        var sql = $"""
            SELECT
                entry_id,
                resource_type,
                resource_id,
                action,
                actor_user_id,
                actor_name,
                actor_role,
                actor_type,
                before_json,
                after_json,
                correlation_id,
                service_name,
                tags_json,
                occurred_at
            FROM audit_trail
            {whereClause}
            ORDER BY occurred_at DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
            """;

        await using var conn = new SqlConnection(options.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.Text;
        cmd.CommandText = sql;
        cmd.Parameters.AddRange(parameters.ToArray());
        cmd.Parameters.Add(new SqlParameter("@offset", SqlDbType.Int) { Value = offset });
        cmd.Parameters.Add(new SqlParameter("@pageSize", SqlDbType.Int) { Value = pageSize });

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        while (await reader.ReadAsync(ct))
        {
            var actorTypeText = reader.GetString(reader.GetOrdinal("actor_type"));
            _ = Enum.TryParse<AuditActorType>(actorTypeText, ignoreCase: true, out var actorType);

            var tagsJson = reader.IsDBNull(reader.GetOrdinal("tags_json")) ? null : reader.GetString(reader.GetOrdinal("tags_json"));
            var tags = tagsJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(tagsJson);

            yield return new AuditEntry
            {
                EntryId = reader.GetGuid(reader.GetOrdinal("entry_id")),
                ResourceType = reader.GetString(reader.GetOrdinal("resource_type")),
                ResourceId = reader.GetString(reader.GetOrdinal("resource_id")),
                Action = reader.GetString(reader.GetOrdinal("action")),
                Actor = new AuditActor(
                    reader.GetString(reader.GetOrdinal("actor_user_id")),
                    reader.GetString(reader.GetOrdinal("actor_name")),
                    reader.IsDBNull(reader.GetOrdinal("actor_role")) ? null : reader.GetString(reader.GetOrdinal("actor_role")),
                    actorType
                ),
                Before = reader.IsDBNull(reader.GetOrdinal("before_json")) ? null : reader.GetString(reader.GetOrdinal("before_json")),
                After = reader.IsDBNull(reader.GetOrdinal("after_json")) ? null : reader.GetString(reader.GetOrdinal("after_json")),
                CorrelationId = reader.IsDBNull(reader.GetOrdinal("correlation_id")) ? null : reader.GetString(reader.GetOrdinal("correlation_id")),
                ServiceName = reader.IsDBNull(reader.GetOrdinal("service_name")) ? null : reader.GetString(reader.GetOrdinal("service_name")),
                Tags = tags,
                OccurredAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("occurred_at"))
            };
        }
    }
}

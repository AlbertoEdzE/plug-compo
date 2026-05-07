using System.Data;
using FluentAssertions;
using KSquare.AuditTrail.Configuration;
using KSquare.AuditTrail.Contracts;
using KSquare.AuditTrail.Extensions;
using KSquare.AuditTrail.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.AuditTrail.Tests;

public sealed class AuditTrailDockerIntegrationTests
{
    [Fact]
    public async Task WriteAsync_inserts_1000_events_concurrently_without_duplicates()
    {
        if (!await EnsureSqlReadyAsync())
        {
            return;
        }

        var correlationId = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsAuditTrail(options =>
        {
            options.Provider = AuditProvider.SqlServer;
            options.ConnectionString = SqlConnectionString;
            options.ServiceName = "kspl-audittrail-integration";
            options.MaskPiiInBeforeAfter = true;
            options.PiiFieldNames = new List<string> { "email" };
        });

        var writer = services.BuildServiceProvider().GetRequiredService<IAuditTrailWriter>();
        var actor = new AuditActor("integration", "Integration Test", ActorType: AuditActorType.System);

        var tasks = Enumerable.Range(0, 1000).Select(i =>
            writer.WriteAsync(new AuditEntry
            {
                ResourceType = "Submission",
                ResourceId = $"submission-{i}",
                Action = "Created",
                Actor = actor,
                CorrelationId = correlationId,
                Before = System.Text.Json.JsonSerializer.Serialize(new { email = $"user{i}@example.com" }),
                After = System.Text.Json.JsonSerializer.Serialize(new { email = $"user{i}@example.com" }),
                OccurredAt = DateTimeOffset.UtcNow
            })
        );

        await Task.WhenAll(tasks);

        var (total, distinct) = await CountByCorrelationAsync(correlationId);
        total.Should().Be(1000);
        distinct.Should().Be(1000);
    }

    private static async Task<bool> EnsureSqlReadyAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        Exception? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await using var conn = new SqlConnection(SqlConnectionString);
                await conn.OpenAsync();
                await EnsureSqlSchemaAsync(conn);
                return true;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(1000);
            }
        }

        _ = last;
        return false;
    }

    private static async Task EnsureSqlSchemaAsync(SqlConnection conn)
    {
        const string sql = """
            IF OBJECT_ID('dbo.audit_trail', 'U') IS NULL
            BEGIN
                CREATE TABLE audit_trail (
                    entry_id        UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
                    resource_type   NVARCHAR(100) NOT NULL,
                    resource_id     NVARCHAR(500) NOT NULL,
                    action          NVARCHAR(200) NOT NULL,
                    actor_user_id   NVARCHAR(500) NOT NULL,
                    actor_name      NVARCHAR(500) NOT NULL,
                    actor_role      NVARCHAR(200) NULL,
                    actor_type      NVARCHAR(50) NOT NULL DEFAULT 'User',
                    before_json     NVARCHAR(MAX) NULL,
                    after_json      NVARCHAR(MAX) NULL,
                    correlation_id  NVARCHAR(200) NULL,
                    service_name    NVARCHAR(200) NULL,
                    tags_json       NVARCHAR(MAX) NULL,
                    occurred_at     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
                );
                CREATE INDEX IX_audit_resource ON audit_trail (resource_type, resource_id, occurred_at DESC);
                CREATE INDEX IX_audit_actor ON audit_trail (actor_user_id, occurred_at DESC);
                CREATE INDEX IX_audit_occurred ON audit_trail (occurred_at DESC);
            END;
            """;

        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(int Total, int Distinct)> CountByCorrelationAsync(string correlationId)
    {
        await using var conn = new SqlConnection(SqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.Text;
        cmd.CommandText = """
            SELECT
                COUNT(1) AS total,
                COUNT(DISTINCT entry_id) AS distinct_total
            FROM audit_trail
            WHERE correlation_id = @correlation_id;
            """;

        cmd.Parameters.AddWithValue("@correlation_id", correlationId);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (
            Convert.ToInt32(reader["total"]),
            Convert.ToInt32(reader["distinct_total"])
        );
    }

    private const string SqlConnectionString =
        "Server=localhost,14333;User ID=sa;Password=LocalDev_SA_Password123!;TrustServerCertificate=true;Encrypt=false;";
}

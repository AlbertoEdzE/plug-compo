using System.Data;
using System.Net;
using FluentAssertions;
using KSquare.Idempotency.Configuration;
using KSquare.Idempotency.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace KSquare.Idempotency.Tests;

public sealed class IdempotencyDockerIntegrationTests
{
    [Fact]
    public async Task SqlServer_concurrent_requests_process_once_and_cache_others()
    {
        await EnsureSqlReadyAsync();
        await ResetSqlTablesAsync();

        var processedCount = 0;

        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddKsIdempotency(options =>
                {
                    options.Provider = IdempotencyProvider.SqlServer;
                    options.ConnectionString = SqlConnectionString;
                });
            })
            .Configure(app =>
            {
                app.UseKsIdempotency();
                app.Run(async ctx =>
                {
                    Interlocked.Increment(ref processedCount);
                    ctx.Response.StatusCode = StatusCodes.Status200OK;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"value\":42}", ctx.RequestAborted);
                });
            })
        );

        using var client = server.CreateClient();
        var tasks = Enumerable.Range(0, 100).Select(_ => SendWithKeyAsync(client, "same-key"));
        var results = await Task.WhenAll(tasks);

        processedCount.Should().Be(1);
        results.All(r => r.StatusCode == HttpStatusCode.OK).Should().BeTrue();
        results.Select(r => r.Body).Distinct().Should().ContainSingle().Which.Should().Be("{\"value\":42}");
    }

    [Fact]
    public async Task Redis_concurrent_requests_process_once_and_cache_others()
    {
        await EnsureRedisReadyAsync();
        await ResetRedisAsync();

        var processedCount = 0;

        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddKsIdempotency(options =>
                {
                    options.Provider = IdempotencyProvider.Redis;
                    options.RedisConnectionString = RedisConnectionString;
                });
            })
            .Configure(app =>
            {
                app.UseKsIdempotency();
                app.Run(async ctx =>
                {
                    Interlocked.Increment(ref processedCount);
                    ctx.Response.StatusCode = StatusCodes.Status200OK;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"value\":42}", ctx.RequestAborted);
                });
            })
        );

        using var client = server.CreateClient();
        var tasks = Enumerable.Range(0, 100).Select(_ => SendWithKeyAsync(client, "same-key"));
        var results = await Task.WhenAll(tasks);

        processedCount.Should().Be(1);
        results.All(r => r.StatusCode == HttpStatusCode.OK).Should().BeTrue();
        results.Select(r => r.Body).Distinct().Should().ContainSingle().Which.Should().Be("{\"value\":42}");
    }

    private static async Task<(HttpStatusCode StatusCode, string Body)> SendWithKeyAsync(HttpClient client, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://test.local/");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, body);
    }

    private static async Task EnsureSqlReadyAsync()
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
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(1000);
            }
        }

        throw new Xunit.Sdk.SkipException($"SQL Server not reachable for integration tests. {last?.Message}");
    }

    private static async Task EnsureSqlSchemaAsync(SqlConnection conn)
    {
        const string sql = """
            IF OBJECT_ID('dbo.idempotency_keys', 'U') IS NULL
            BEGIN
                CREATE TABLE idempotency_keys (
                    [key]           NVARCHAR(500) NOT NULL PRIMARY KEY,
                    status_code     INT NOT NULL,
                    response_body   NVARCHAR(MAX) NOT NULL,
                    content_type    NVARCHAR(200) NOT NULL,
                    processed_at    DATETIMEOFFSET NOT NULL,
                    expires_at      DATETIMEOFFSET NOT NULL
                );
                CREATE INDEX IX_idempotency_expires ON idempotency_keys (expires_at);
            END;

            IF OBJECT_ID('dbo.idempotency_consumer_keys', 'U') IS NULL
            BEGIN
                CREATE TABLE idempotency_consumer_keys (
                    message_id      NVARCHAR(500) NOT NULL PRIMARY KEY,
                    processed_at    DATETIMEOFFSET NOT NULL,
                    expires_at      DATETIMEOFFSET NOT NULL
                );
                CREATE INDEX IX_consumer_expires ON idempotency_consumer_keys (expires_at);
            END;
            """;

        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ResetSqlTablesAsync()
    {
        await using var conn = new SqlConnection(SqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM idempotency_consumer_keys; DELETE FROM idempotency_keys;";
        cmd.CommandType = CommandType.Text;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureRedisReadyAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        Exception? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var mux = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
                var pong = await mux.GetDatabase().PingAsync();
                await mux.CloseAsync();
                if (pong > TimeSpan.Zero)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(500);
            }
        }

        throw new Xunit.Sdk.SkipException($"Redis not reachable for integration tests. {last?.Message}");
    }

    private static async Task ResetRedisAsync()
    {
        var mux = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
        var endpoint = mux.GetEndPoints().First();
        var server = mux.GetServer(endpoint);
        await server.FlushDatabaseAsync();
        await mux.CloseAsync();
    }

    private const string SqlConnectionString =
        "Server=localhost,14333;User ID=sa;Password=LocalDev_SA_Password123!;TrustServerCertificate=true;Encrypt=false;";

    private const string RedisConnectionString = "localhost:16379";
}

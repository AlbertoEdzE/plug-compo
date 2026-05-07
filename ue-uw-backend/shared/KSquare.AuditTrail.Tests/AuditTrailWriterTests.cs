using FluentAssertions;
using KSquare.AuditTrail.Configuration;
using KSquare.AuditTrail.Contracts;
using KSquare.AuditTrail.Extensions;
using KSquare.AuditTrail.Models;
using KSquare.AuditTrail.Tests.Synthesizers;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.AuditTrail.Tests;

public sealed class AuditTrailWriterTests
{
    [Fact]
    public async Task WriteAsync_stores_entry_retrievable_via_QueryAsync()
    {
        var synthesizer = new AuditTrailDataSynthesizer();
        var writer = CreateInMemoryWriter();

        var resourceType = synthesizer.ResourceType();
        var resourceId = synthesizer.ResourceId();

        await writer.WriteAsync(new AuditEntry
        {
            ResourceType = resourceType,
            ResourceId = resourceId,
            Action = synthesizer.Action(),
            Actor = synthesizer.Actor(),
            OccurredAt = DateTimeOffset.UtcNow
        });

        var results = await CollectAsync(writer.QueryAsync(new AuditQuery(ResourceType: resourceType, ResourceId: resourceId)));

        results.Should().HaveCount(1);
        results[0].ResourceType.Should().Be(resourceType);
        results[0].ResourceId.Should().Be(resourceId);
    }

    [Fact]
    public async Task WriteChangeAsync_serializes_before_and_after()
    {
        var synthesizer = new AuditTrailDataSynthesizer();
        var writer = CreateInMemoryWriter();

        var before = new { Status = "Draft", Value = 1 };
        var after = new { Status = "Submitted", Value = 2 };

        await writer.WriteChangeAsync(
            synthesizer.ResourceType(),
            synthesizer.ResourceId(),
            "StatusChanged",
            before,
            after,
            synthesizer.Actor(),
            synthesizer.CorrelationId()
        );

        var results = await CollectAsync(writer.QueryAsync(new AuditQuery(PageSize: 10)));
        results.Should().ContainSingle();

        results[0].Before.Should().NotBeNull();
        results[0].After.Should().NotBeNull();
        results[0].Before!.Should().Contain("Draft");
        results[0].After!.Should().Contain("Submitted");
    }

    [Fact]
    public async Task Pii_masking_redacts_email_in_before_after()
    {
        var synthesizer = new AuditTrailDataSynthesizer();
        var email = synthesizer.Email();

        var writer = CreateInMemoryWriter(options =>
        {
            options.MaskPiiInBeforeAfter = true;
            options.PiiFieldNames = new List<string> { "email" };
        });

        await writer.WriteChangeAsync(
            synthesizer.ResourceType(),
            synthesizer.ResourceId(),
            synthesizer.Action(),
            before: new { email },
            after: new { email },
            synthesizer.Actor()
        );

        var results = await CollectAsync(writer.QueryAsync(new AuditQuery(PageSize: 10)));
        var entry = results.Should().ContainSingle().Subject;

        entry.Before.Should().NotContain(email);
        entry.After.Should().NotContain(email);
        entry.Before.Should().Contain("***REDACTED***");
        entry.After.Should().Contain("***REDACTED***");
    }

    [Fact]
    public async Task Pii_masking_handles_nested_json()
    {
        var synthesizer = new AuditTrailDataSynthesizer();
        var email = synthesizer.Email();

        var writer = CreateInMemoryWriter(options =>
        {
            options.MaskPiiInBeforeAfter = true;
            options.PiiFieldNames = new List<string> { "email" };
        });

        var nested = new { outer = new { inner = new { email } } };

        await writer.WriteChangeAsync(
            synthesizer.ResourceType(),
            synthesizer.ResourceId(),
            synthesizer.Action(),
            before: nested,
            after: nested,
            synthesizer.Actor()
        );

        var results = await CollectAsync(writer.QueryAsync(new AuditQuery(PageSize: 10)));
        var entry = results.Should().ContainSingle().Subject;

        entry.Before.Should().NotContain(email);
        entry.Before.Should().Contain("***REDACTED***");
    }

    [Fact]
    public async Task QueryAsync_filters_by_date_range()
    {
        var synthesizer = new AuditTrailDataSynthesizer();
        var writer = CreateInMemoryWriter();

        var actor = synthesizer.Actor();
        var now = DateTimeOffset.UtcNow;

        await writer.WriteAsync(new AuditEntry
        {
            ResourceType = "Submission",
            ResourceId = "1",
            Action = "Created",
            Actor = actor,
            OccurredAt = now.AddDays(-2)
        });

        await writer.WriteAsync(new AuditEntry
        {
            ResourceType = "Submission",
            ResourceId = "1",
            Action = "Created",
            Actor = actor,
            OccurredAt = now.AddHours(-1)
        });

        var results = await CollectAsync(writer.QueryAsync(new AuditQuery(
            From: now.AddDays(-1),
            To: now
        )));

        results.Should().HaveCount(1);
        results[0].OccurredAt.Should().BeAfter(now.AddDays(-1));
    }

    [Fact]
    public async Task QueryAsync_paginates_in_descending_occurred_at_order()
    {
        var writer = CreateInMemoryWriter();

        var actor = new AuditActor("u", "User");
        var baseTime = DateTimeOffset.UtcNow;

        for (var i = 0; i < 10; i++)
        {
            await writer.WriteAsync(new AuditEntry
            {
                ResourceType = "Submission",
                ResourceId = "1",
                Action = "Created",
                Actor = actor,
                OccurredAt = baseTime.AddMinutes(i)
            });
        }

        var page1 = await CollectAsync(writer.QueryAsync(new AuditQuery(Page: 1, PageSize: 3)));
        var page2 = await CollectAsync(writer.QueryAsync(new AuditQuery(Page: 2, PageSize: 3)));

        page1.Should().HaveCount(3);
        page2.Should().HaveCount(3);
        page1[0].OccurredAt.Should().BeAfter(page1[1].OccurredAt);
        page2[0].OccurredAt.Should().BeBefore(page1.Last().OccurredAt);
    }

    [Fact]
    public async Task WriteAsync_never_throws_when_backend_is_unreachable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsAuditTrail(options =>
        {
            options.Provider = AuditProvider.SqlServer;
            options.ConnectionString = "Server=localhost,1;User ID=sa;Password=invalid;TrustServerCertificate=true;Encrypt=false;";
            options.ServiceName = "kspl-test";
        });

        var writer = services.BuildServiceProvider().GetRequiredService<IAuditTrailWriter>();

        var act = async () => await writer.WriteAsync(new AuditEntry
        {
            ResourceType = "Submission",
            ResourceId = "1",
            Action = "Created",
            Actor = new AuditActor("u", "User")
        });

        await act.Should().NotThrowAsync();
    }

    private static IAuditTrailWriter CreateInMemoryWriter(Action<AuditTrailOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsAuditTrail(options =>
        {
            options.Provider = AuditProvider.InMemory;
            options.ServiceName = "kspl-test";
            configure?.Invoke(options);
        });

        return services.BuildServiceProvider().GetRequiredService<IAuditTrailWriter>();
    }

    private static async Task<List<AuditEntry>> CollectAsync(IAsyncEnumerable<AuditEntry> source)
    {
        var list = new List<AuditEntry>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }
}

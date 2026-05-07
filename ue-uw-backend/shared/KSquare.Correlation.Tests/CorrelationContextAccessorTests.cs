using FluentAssertions;
using KSquare.Correlation.Models;
using KSquare.Correlation.Tests.Synthesizers;

namespace KSquare.Correlation.Tests;

public sealed class CorrelationContextAccessorTests
{
    [Fact]
    public async Task Current_flows_across_awaits()
    {
        var synthesizer = new CorrelationDataSynthesizer();
        var accessor = new CorrelationContextAccessor();
        CorrelationContext context = synthesizer.Context();

        accessor.Current = context;

        await Task.Delay(1);

        accessor.Current.Should().NotBeNull();
        accessor.Current!.CorrelationId.Should().Be(context.CorrelationId);
        accessor.Current!.TenantId.Should().Be(context.TenantId);
        accessor.Current!.UserId.Should().Be(context.UserId);
    }

    [Fact]
    public async Task Current_flows_into_TaskRun()
    {
        var synthesizer = new CorrelationDataSynthesizer();
        var accessor = new CorrelationContextAccessor();
        CorrelationContext context = synthesizer.Context();

        accessor.Current = context;

        var observed = await Task.Run(() => accessor.Current?.CorrelationId);

        observed.Should().Be(context.CorrelationId);
    }
}

using System.Globalization;
using FluentAssertions;
using KSquare.ProposalOrchestrator.Configuration;
using KSquare.ProposalOrchestrator.Mapping;
using KSquare.ProposalOrchestrator.Tests.Synthesizers;

namespace KSquare.ProposalOrchestrator.Tests;

public sealed class GhostDraftPayloadBuilderTests
{
    [Fact]
    public void Build_includes_all_coverage_lines_and_formats_total_premium()
    {
        var synth = new ProposalOrchestratorDataSynthesizer(seed: 123);
        var request = synth.Request(coverageLines: 5);

        var options = new ProposalOrchestratorOptions();
        options.TemplateIdMap[request.ProposalType] = "test-template";

        var builder = new GhostDraftPayloadBuilder(options);
        var payload = builder.Build(request).Payload;

        payload.Should().ContainKey("templateId");
        payload["templateId"].Should().Be("test-template");

        payload.Should().ContainKey("data");
        var data = payload["data"].Should().BeOfType<Dictionary<string, object?>>().Subject;

        data.Should().ContainKey("coverageLines");
        var coverage = data["coverageLines"].Should().BeOfType<List<Dictionary<string, object?>>>().Subject;
        coverage.Should().HaveCount(request.CoverageLines.Count);

        var expected = request.CoverageLines.Sum(x => x.AnnualPremium)
            .ToString("C2", CultureInfo.GetCultureInfo("en-US"));

        data.Should().ContainKey("totalPremium");
        data["totalPremium"].Should().Be(expected);
    }
}


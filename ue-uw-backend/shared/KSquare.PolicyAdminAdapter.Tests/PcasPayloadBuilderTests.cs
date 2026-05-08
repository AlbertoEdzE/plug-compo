using FluentAssertions;
using System.Text.Json;
using KSquare.PolicyAdminAdapter.Configuration;
using KSquare.PolicyAdminAdapter.Providers.Pcas;
using KSquare.PolicyAdminAdapter.Tests.Synthesizers;

namespace KSquare.PolicyAdminAdapter.Tests;

public sealed class PcasPayloadBuilderTests
{
    [Fact]
    public void Maps_gl_product_code_to_cgl()
    {
        var options = new PolicyAdminAdapterOptions();
        var builder = new PcasPayloadBuilder(options);

        var req = new PolicyAdminDataSynthesizer(seed: 1).BindRequest(coverageLines: 1);
        req = req with
        {
            CoverageLines = new[]
            {
                req.CoverageLines[0] with { ProductCode = "GL" }
            }
        };

        var payload = builder.Build(req);

        var json = JsonSerializer.Serialize(payload.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().Contain("\"coverageCode\":\"CGL\"");
    }

    [Fact]
    public void Maps_all_coverage_lines_into_payload()
    {
        var options = new PolicyAdminAdapterOptions();
        var builder = new PcasPayloadBuilder(options);

        var req = new PolicyAdminDataSynthesizer(seed: 2).BindRequest(coverageLines: 3);
        var payload = builder.Build(req);

        var json = JsonSerializer.Serialize(payload.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().Contain("\"coverages\":");
        json.Should().Contain("\"coverageCode\":");
    }
}

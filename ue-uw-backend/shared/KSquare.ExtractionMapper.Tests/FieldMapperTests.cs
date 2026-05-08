using FluentAssertions;
using KSquare.DocumentExtraction.Models;
using KSquare.ExtractionMapper.Configuration;
using KSquare.ExtractionMapper.Contracts;
using KSquare.ExtractionMapper.Extensions;
using KSquare.ExtractionMapper.Models;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.ExtractionMapper.Tests;

public sealed class FieldMapperTests
{
    [Fact]
    public void R001_maps_named_insured_to_insured_name()
    {
        var mapper = CreateMapper();
        var extraction = BuildExtraction(new ExtractedField { Name = "NamedInsured", Value = "John Smith", Confidence = 0.99f });

        var result = mapper.Map<Acord125ExtractedData>(extraction, "ACORD125");

        result.Value.InsuredName.Should().Be("John Smith");
        result.MappedFields.Should().ContainSingle(f => f.CanonicalFieldName == "InsuredName" && (string?)f.Value == "John Smith" && f.RuleApplied == "R001");
    }

    [Fact]
    public void R001_tries_fallback_source_field_names_in_order()
    {
        var mapper = CreateMapper();
        var extraction = BuildExtraction(new ExtractedField { Name = "named_insured", Value = "Jane Doe", Confidence = 0.99f });

        var result = mapper.Map<Acord125ExtractedData>(extraction, "ACORD125");

        result.Value.InsuredName.Should().Be("Jane Doe");
        result.MappedFields.Should().ContainSingle(f => f.CanonicalFieldName == "InsuredName" && f.SourceFieldName == "named_insured");
    }

    [Fact]
    public void Required_field_missing_produces_warning()
    {
        var mapper = CreateMapper();
        var extraction = BuildExtraction();

        var result = mapper.Map<Acord125ExtractedData>(extraction, "ACORD125");

        result.Warnings.Should().Contain(w => w.FieldName == "InsuredName" && w.Severity == WarningSeverity.RequiredFieldMissing);
    }

    [Fact]
    public void ParseDate_transform_parses_mm_dd_yyyy_correctly()
    {
        var mapper = CreateMapper();
        var extraction = BuildExtraction(new ExtractedField { Name = "EffectiveDate", Value = "01/15/2025", Confidence = 0.99f });

        var result = mapper.Map<Acord125ExtractedData>(extraction, "ACORD125");

        result.Value.PolicyEffectiveDate.Should().Be(new DateOnly(2025, 1, 15));
    }

    [Fact]
    public void ParseDecimal_strips_currency_symbols_and_grouping_separators()
    {
        var mapper = CreateMapper();
        var extraction = BuildExtraction(new ExtractedField { Name = "TIV", Value = "$1,250,000", Confidence = 0.99f });

        var result = mapper.Map<Acord125ExtractedData>(extraction, "ACORD125");

        result.Value.TotalInsuredValue.Should().Be(1250000m);
    }

    [Fact]
    public void Low_confidence_field_produces_warning_and_flags_result()
    {
        var mapper = CreateMapper();
        var extraction = BuildExtraction(new ExtractedField { Name = "NamedInsured", Value = "John Smith", Confidence = 0.5f });

        var result = mapper.Map<Acord125ExtractedData>(extraction, "ACORD125");

        result.HasLowConfidenceFields.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.FieldName == "InsuredName" && w.Severity == WarningSeverity.LowConfidence);
    }

    [Fact]
    public void Full_acord125_mapping_round_trip_maps_multiple_fields()
    {
        var mapper = CreateMapper();
        var extraction = BuildExtraction(
            new ExtractedField { Name = "NamedInsured", Value = "John Smith", Confidence = 0.99f },
            new ExtractedField { Name = "EffectiveDate", Value = "01/15/2025", Confidence = 0.99f },
            new ExtractedField { Name = "TIV", Value = "$1,250,000", Confidence = 0.99f },
            new ExtractedField { Name = "Locations", Value = "3", Confidence = 0.99f },
            new ExtractedField { Name = "AgentName", Value = "Best Agency", Confidence = 0.99f }
        );

        var result = mapper.Map<Acord125ExtractedData>(extraction, "ACORD125");

        result.Value.InsuredName.Should().Be("John Smith");
        result.Value.PolicyEffectiveDate.Should().Be(new DateOnly(2025, 1, 15));
        result.Value.TotalInsuredValue.Should().Be(1250000m);
        result.Value.NumberOfLocations.Should().Be(3);
        result.Value.BrokerName.Should().Be("Best Agency");
        result.MappedFields.Should().Contain(f => f.CanonicalFieldName == "InsuredName" && f.RuleApplied == "R001");
        result.MappedFields.Should().Contain(f => f.CanonicalFieldName == "PolicyEffectiveDate" && f.RuleApplied == "R002");
        result.MappedFields.Should().Contain(f => f.CanonicalFieldName == "TotalInsuredValue" && f.RuleApplied == "R003");
        result.MappedFields.Should().Contain(f => f.CanonicalFieldName == "NumberOfLocations" && f.RuleApplied == "R004");
        result.MappedFields.Should().Contain(f => f.CanonicalFieldName == "BrokerName" && f.RuleApplied == "R005");
    }

    [Fact]
    public void MapToDictionary_returns_typed_values()
    {
        var mapper = CreateMapper();
        var extraction = BuildExtraction(
            new ExtractedField { Name = "NamedInsured", Value = "John Smith", Confidence = 0.99f },
            new ExtractedField { Name = "EffectiveDate", Value = "01/15/2025", Confidence = 0.99f },
            new ExtractedField { Name = "TIV", Value = "$1,250,000", Confidence = 0.99f }
        );

        var result = mapper.MapToDictionary(extraction, "ACORD125");

        result.Value["InsuredName"].Should().Be("John Smith");
        result.Value["PolicyEffectiveDate"].Should().Be(new DateOnly(2025, 1, 15));
        result.Value["TotalInsuredValue"].Should().Be(1250000m);
    }

    private static IExtractionMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddKsExtractionMapper(o =>
        {
            o.RuleSource = MappingRuleSource.EmbeddedYaml;
            o.StrictMode = false;
        });
        return services.BuildServiceProvider().GetRequiredService<IExtractionMapper>();
    }

    private static ExtractionResult BuildExtraction(params ExtractedField[] fields)
    {
        return new ExtractionResult
        {
            DocumentId = "doc-1",
            ProviderOperationId = "op-1",
            Status = ExtractionStatus.Succeeded,
            Fields = fields.ToArray(),
            Tables = Array.Empty<ExtractedTable>(),
            Pages = Array.Empty<ExtractedPage>()
        };
    }

    private sealed class Acord125ExtractedData
    {
        public string? InsuredName { get; set; }
        public DateOnly? PolicyEffectiveDate { get; set; }
        public decimal? TotalInsuredValue { get; set; }
        public int? NumberOfLocations { get; set; }
        public string? BrokerName { get; set; }
    }
}


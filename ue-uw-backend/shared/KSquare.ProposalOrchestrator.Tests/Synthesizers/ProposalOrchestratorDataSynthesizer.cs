using Bogus;
using KSquare.ProposalOrchestrator.Models;

namespace KSquare.ProposalOrchestrator.Tests.Synthesizers;

public sealed class ProposalOrchestratorDataSynthesizer
{
    private readonly Faker _faker;

    public ProposalOrchestratorDataSynthesizer(int seed = 1337)
    {
        Randomizer.Seed = new Random(seed);
        _faker = new Faker();
    }

    public ProposalGenerationRequest Request(int coverageLines = 3)
    {
        var effective = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));
        var expiration = effective.AddYears(1);

        var lines = Enumerable.Range(0, Math.Max(1, coverageLines)).Select(_ => new ProposalCoverageLine
        {
            ProductName = _faker.Commerce.ProductName(),
            Limit = _faker.Random.Decimal(100_000m, 5_000_000m),
            Retention = _faker.Random.Decimal(1_000m, 250_000m),
            AnnualPremium = _faker.Random.Decimal(2_000m, 250_000m),
            AggregateLimit = _faker.Random.Bool(0.5f) ? _faker.Random.Decimal(100_000m, 10_000_000m) : null,
            CoverageConditions = _faker.Random.Bool(0.5f) ? _faker.Lorem.Sentence() : null
        }).ToList();

        return new ProposalGenerationRequest
        {
            QuoteId = _faker.Random.Guid().ToString("N"),
            SubmissionId = _faker.Random.Guid().ToString("N"),
            ProposalType = _faker.PickRandom(new[] { "NBI", "QuoteProposal", "Binder" }),
            InstitutionName = _faker.Company.CompanyName(),
            BrokerName = _faker.Name.FullName(),
            BrokerEmail = _faker.Internet.Email(),
            EffectiveDate = effective,
            ExpirationDate = expiration,
            CoverageLines = lines,
            UnderwriterName = _faker.Name.FullName(),
            SpecialConditions = _faker.Random.Bool(0.4f) ? _faker.Lorem.Paragraph() : null,
            OutputFormat = "pdf",
            CorrelationId = _faker.Random.Guid().ToString()
        };
    }
}


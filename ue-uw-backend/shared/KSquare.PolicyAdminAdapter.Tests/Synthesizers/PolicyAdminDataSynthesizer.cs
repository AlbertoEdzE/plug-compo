using Bogus;
using KSquare.PolicyAdminAdapter.Models;

namespace KSquare.PolicyAdminAdapter.Tests.Synthesizers;

public sealed class PolicyAdminDataSynthesizer
{
    private readonly Faker _faker;

    public PolicyAdminDataSynthesizer(int seed)
    {
        Randomizer.Seed = new Random(seed);
        _faker = new Faker();
    }

    public BindRequest BindRequest(int coverageLines = 2)
    {
        var quoteId = "quote-" + _faker.Random.AlphaNumeric(10);
        var submissionId = "sub-" + _faker.Random.AlphaNumeric(10);

        return new BindRequest
        {
            QuoteId = quoteId,
            SubmissionId = submissionId,
            InstitutionLegalName = _faker.Company.CompanyName(),
            InstitutionDba = _faker.Company.CompanyName(),
            NaicsCode = _faker.Random.ReplaceNumbers("######"),
            InstitutionAddress = new Address
            {
                Line1 = _faker.Address.StreetAddress(),
                City = _faker.Address.City(),
                State = _faker.Address.StateAbbr(),
                Zip = _faker.Address.ZipCode("#####")
            },
            ProducerLicenseNumber = _faker.Random.ReplaceNumbers("########"),
            ProducerCode = _faker.Random.AlphaNumeric(8),
            ProducerName = _faker.Name.FullName(),
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)),
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)),
            CoverageLines = Enumerable.Range(0, Math.Max(1, coverageLines)).Select(i => new BindCoverageLine
            {
                ProductCode = i == 0 ? "GL" : "PROP",
                ProductName = i == 0 ? "General Liability" : "Property",
                Limit = _faker.Random.Decimal(100_000m, 5_000_000m),
                Retention = _faker.Random.Decimal(0m, 100_000m),
                AnnualPremium = _faker.Random.Decimal(1_000m, 100_000m),
                AggregateLimit = _faker.Random.Decimal(100_000m, 10_000_000m),
                CoverageConditions = _faker.Lorem.Sentence()
            }).ToList(),
            TotalAnnualPremium = _faker.Random.Decimal(5_000m, 500_000m),
            BrokerEmail = _faker.Internet.Email(),
            UnderwriterUserId = "uw-" + _faker.Random.AlphaNumeric(6),
            SpecialConditions = _faker.Lorem.Sentence(),
            CorrelationId = Guid.NewGuid().ToString()
        };
    }
}


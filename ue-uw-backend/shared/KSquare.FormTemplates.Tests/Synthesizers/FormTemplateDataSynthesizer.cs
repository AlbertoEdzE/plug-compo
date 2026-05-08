using Bogus;

namespace KSquare.FormTemplates.Tests.Synthesizers;

public sealed class FormTemplateDataSynthesizer
{
    private readonly Faker _faker;

    public FormTemplateDataSynthesizer(int seed = 1337)
    {
        Randomizer.Seed = new Random(seed);
        _faker = new Faker();
    }

    public SubmissionModel Submission()
    {
        var effective = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));
        return new SubmissionModel
        {
            InsuredName = _faker.Company.CompanyName(),
            InsuredAddress = _faker.Address.FullAddress(),
            PolicyEffectiveDate = effective,
            PolicyExpirationDate = effective.AddYears(1),
            TotalInsuredValue = _faker.Random.Decimal(100_000m, 50_000_000m),
            BrokerName = _faker.Name.FullName(),
            BrokerLicenseNumber = _faker.Random.ReplaceNumbers("########"),
            QuoteNumber = _faker.Random.ReplaceNumbers("Q-######"),
            EffectiveDate = effective,
            ExpirationDate = effective.AddYears(1),
            TotalPremium = _faker.Random.Decimal(5_000m, 500_000m),
            BinderNumber = _faker.Random.ReplaceNumbers("B-######"),
            BindDate = effective,
            UnderwriterName = _faker.Name.FullName()
        };
    }
}

public sealed record SubmissionModel
{
    public required string InsuredName { get; init; }
    public required string InsuredAddress { get; init; }
    public required DateOnly PolicyEffectiveDate { get; init; }
    public required DateOnly PolicyExpirationDate { get; init; }
    public required decimal TotalInsuredValue { get; init; }
    public required string BrokerName { get; init; }
    public required string BrokerLicenseNumber { get; init; }
    public required string QuoteNumber { get; init; }
    public required DateOnly EffectiveDate { get; init; }
    public required DateOnly ExpirationDate { get; init; }
    public required decimal TotalPremium { get; init; }
    public required string BinderNumber { get; init; }
    public required DateOnly BindDate { get; init; }
    public string? UnderwriterName { get; init; }
}

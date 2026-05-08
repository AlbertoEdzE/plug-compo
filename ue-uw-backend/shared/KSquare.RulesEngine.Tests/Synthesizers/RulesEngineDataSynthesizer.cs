using Bogus;
using KSquare.RulesEngine.Context;

namespace KSquare.RulesEngine.Tests.Synthesizers;

public sealed class RulesEngineDataSynthesizer
{
    private readonly Faker _faker;

    public RulesEngineDataSynthesizer(int seed = 1337)
    {
        Randomizer.Seed = new Random(seed);
        _faker = new Faker();
    }

    public IntakeRoutingContext IntakeContext() => new()
    {
        TotalInsuredValue = _faker.Random.Decimal(10_000m, 25_000_000m),
        BrokerTenureMonths = _faker.Random.Int(0, 120),
        NaicsCode = _faker.Random.ReplaceNumbers("####"),
        MissingRequiredFields = _faker.Random.Bool(0.5f)
            ? new List<string>()
            : _faker.Random.WordsArray(3).ToList(),
        NumberOfLocations = _faker.Random.Int(1, 50),
        SubmissionSource = _faker.PickRandom(new[] { "email", "portal", "api" })
    };

    public ReferralContext ReferralContext() => new()
    {
        LargestSingleLoss = _faker.Random.Decimal(0m, 2_000_000m),
        FiveYearLossRatio = _faker.Random.Decimal(0m, 1m),
        NumberOfLocations = _faker.Random.Int(1, 50),
        NaicsCode = _faker.Random.ReplaceNumbers("####"),
        OutOfAppetiteNaicsCodes = new List<string> { "2381", "4841", "9999" },
        TotalInsuredValue = _faker.Random.Decimal(10_000m, 25_000_000m)
    };

    public BindReadinessContext BindContext() => new()
    {
        QuoteStatus = _faker.PickRandom(new[] { "Draft", "InReview", "Approved" }),
        HasSignedApplication = _faker.Random.Bool(),
        PremiumAgreedByBroker = _faker.Random.Bool(),
        ComplianceCheckPassed = _faker.Random.Bool(),
        ReferralApproved = _faker.Random.Bool()
    };
}


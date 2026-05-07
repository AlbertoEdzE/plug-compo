using Bogus;
using KSquare.Correlation.Models;

namespace KSquare.Correlation.Tests.Synthesizers;

public sealed class CorrelationDataSynthesizer
{
    private readonly Faker _faker;

    public CorrelationDataSynthesizer(int seed = 1337)
    {
        Randomizer.Seed = new Random(seed);
        _faker = new Faker();
    }

    public string CorrelationId() => _faker.Random.Guid().ToString();

    public string TenantId() => _faker.Random.Guid().ToString();

    public string UserId() => _faker.Random.Guid().ToString();

    public CorrelationContext Context() => new(CorrelationId(), TenantId(), UserId());
}

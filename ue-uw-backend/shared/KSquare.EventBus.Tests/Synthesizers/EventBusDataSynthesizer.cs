using Bogus;

namespace KSquare.EventBus.Tests.Synthesizers;

public sealed class EventBusDataSynthesizer
{
    private readonly Faker _faker;

    public EventBusDataSynthesizer(int seed = 1337)
    {
        Randomizer.Seed = new Random(seed);
        _faker = new Faker();
    }

    public string Topic() => $"topic-{_faker.Random.AlphaNumeric(8).ToLowerInvariant()}";

    public string Subscription() => $"sub-{_faker.Random.AlphaNumeric(8).ToLowerInvariant()}";

    public string EventType() => $"{_faker.Hacker.Noun()}.{_faker.Hacker.Verb()}".ToLowerInvariant();

    public string MessageId() => _faker.Random.Guid().ToString("N");

    public string CorrelationId() => _faker.Random.Guid().ToString();
}

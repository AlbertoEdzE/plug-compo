using Bogus;
using KSquare.AuditTrail.Models;

namespace KSquare.AuditTrail.Tests.Synthesizers;

public sealed class AuditTrailDataSynthesizer
{
    private readonly Faker _faker;

    public AuditTrailDataSynthesizer(int seed = 1337)
    {
        Randomizer.Seed = new Random(seed);
        _faker = new Faker();
    }

    public string ResourceType() => _faker.PickRandom(new[] { "Submission", "Quote", "Referral" });

    public string ResourceId() => _faker.Random.Guid().ToString();

    public string Action() => _faker.PickRandom(new[] { "Created", "StatusChanged", "Assigned" });

    public AuditActor Actor()
    {
        return new AuditActor(
            _faker.Random.Guid().ToString("N"),
            _faker.Name.FullName(),
            Role: _faker.PickRandom(new[] { "UNDERWRITER", "AGENT", "SYSTEM" }),
            ActorType: _faker.PickRandom<AuditActorType>()
        );
    }

    public string CorrelationId() => _faker.Random.Guid().ToString();

    public string Email() => _faker.Internet.Email();

    public DateTimeOffset RecentUtc() => DateTimeOffset.UtcNow.AddMinutes(-_faker.Random.Int(0, 120));
}

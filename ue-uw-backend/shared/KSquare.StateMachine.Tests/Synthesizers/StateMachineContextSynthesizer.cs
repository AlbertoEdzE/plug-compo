using Bogus;
using KSquare.StateMachine.Models;

namespace KSquare.StateMachine.Tests.Synthesizers;

public sealed class StateMachineContextSynthesizer
{
    private readonly Faker _faker;

    public StateMachineContextSynthesizer(int seed)
    {
        Randomizer.Seed = new Random(seed);
        _faker = new Faker();
    }

    public StateMachineContext Context(string? correlationId = null)
    {
        return new StateMachineContext
        {
            ActorId = "user-" + _faker.Random.AlphaNumeric(8),
            ActorName = _faker.Name.FullName(),
            Reason = _faker.Lorem.Sentence(),
            CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "tests"
            }
        };
    }
}


using Bogus;
using KSquare.Idempotency.Models;

namespace KSquare.Idempotency.Tests.Synthesizers;

public sealed class IdempotencyDataSynthesizer
{
    private readonly Faker _faker;

    public IdempotencyDataSynthesizer(int seed = 1337)
    {
        Randomizer.Seed = new Random(seed);
        _faker = new Faker();
    }

    public string Key() => _faker.Random.Guid().ToString();

    public string MessageId() => _faker.Random.Guid().ToString();

    public IdempotencyResult Result(int statusCode = 200)
    {
        return new IdempotencyResult(
            statusCode,
            ResponseBodyJson(),
            "application/json",
            DateTimeOffset.UtcNow
        );
    }

    public string ResponseBodyJson()
    {
        var payload = new
        {
            id = _faker.Random.Guid().ToString(),
            value = _faker.Lorem.Sentence()
        };

        return System.Text.Json.JsonSerializer.Serialize(payload);
    }
}

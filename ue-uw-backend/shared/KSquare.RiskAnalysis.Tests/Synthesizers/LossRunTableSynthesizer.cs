using Bogus;
using KSquare.DocumentExtraction.Models;

namespace KSquare.RiskAnalysis.Tests.Synthesizers;

public sealed class LossRunTableSynthesizer
{
    private readonly Faker _faker;

    public LossRunTableSynthesizer(int seed = 1337)
    {
        Randomizer.Seed = new Random(seed);
        _faker = new Faker();
    }

    public ExtractedTable Table(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string?>> rows,
        string? tableName = null
    )
    {
        return new ExtractedTable
        {
            TableName = tableName ?? _faker.Random.Word(),
            PageNumber = _faker.Random.Int(1, 10),
            Headers = headers,
            Rows = rows,
            Confidence = 0.95f
        };
    }
}


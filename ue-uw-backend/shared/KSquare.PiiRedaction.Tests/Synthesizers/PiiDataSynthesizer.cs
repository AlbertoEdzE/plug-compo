using Bogus;

namespace KSquare.PiiRedaction.Tests.Synthesizers;

public sealed class PiiDataSynthesizer
{
    private readonly Faker _faker;

    public PiiDataSynthesizer(int seed = 1337)
    {
        Randomizer.Seed = new Random(seed);
        _faker = new Faker();
    }

    public string Email() => _faker.Internet.Email();

    public string Phone() => _faker.Phone.PhoneNumber();

    public string PhoneWithFormat(string format) => _faker.Random.ReplaceNumbers(format);

    public string SsnHyphenated() => _faker.Random.ReplaceNumbers("###-##-####");

    public string SsnNumeric() => _faker.Random.ReplaceNumbers("#########");

    public string NonPiiString() => _faker.Lorem.Sentence();
}

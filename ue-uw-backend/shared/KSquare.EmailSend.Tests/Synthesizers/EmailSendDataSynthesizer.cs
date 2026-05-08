using Bogus;
using KSquare.EmailSend.Models;

namespace KSquare.EmailSend.Tests.Synthesizers;

public sealed class EmailSendDataSynthesizer
{
    private readonly Faker _faker;

    public EmailSendDataSynthesizer(int seed = 1337)
    {
        Randomizer.Seed = new Random(seed);
        _faker = new Faker();
    }

    public EmailAddress From() => new(_faker.Internet.Email(), _faker.Company.CompanyName());

    public EmailAddress To() => new(_faker.Internet.Email(), _faker.Name.FullName());

    public string Subject() => _faker.Lorem.Sentence();

    public string HtmlBody() => $"<p>{_faker.Lorem.Sentence()}</p>";

    public byte[] AttachmentBytes(int size = 16) => _faker.Random.Bytes(size);
}

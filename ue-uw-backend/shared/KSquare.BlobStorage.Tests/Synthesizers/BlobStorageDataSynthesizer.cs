using Bogus;

namespace KSquare.BlobStorage.Tests.Synthesizers;

public sealed class BlobStorageDataSynthesizer
{
    private readonly Faker _faker;

    public BlobStorageDataSynthesizer(int seed = 1337)
    {
        Randomizer.Seed = new Random(seed);
        _faker = new Faker();
    }

    public string ContainerName() => _faker.Random.AlphaNumeric(12).ToLowerInvariant();

    public string BlobPath(string fileName = "file.pdf")
    {
        var year = _faker.Date.Past(1).Year;
        var month = _faker.Random.Int(1, 12).ToString("00");
        var day = _faker.Random.Int(1, 28).ToString("00");
        var correlationId = _faker.Random.Guid().ToString("N");
        return $"incoming/{year}/{month}/{day}/{correlationId}/{fileName}";
    }

    public string ContentTypePdf() => "application/pdf";

    public Dictionary<string, string> Metadata()
    {
        return new Dictionary<string, string>
        {
            ["correlationId"] = _faker.Random.Guid().ToString("N"),
            ["uploadedBy"] = _faker.Internet.UserName(),
            ["documentType"] = _faker.Random.Word()
        };
    }

    public string TempRootPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "kspl-blob-storage-tests",
            _faker.Random.Guid().ToString("N")
        );
    }
}

using FluentAssertions;
using KSquare.BlobStorage.Contracts;
using KSquare.FormTemplates.Configuration;
using KSquare.FormTemplates.FieldMaps;
using KSquare.FormTemplates.Internal;
using KSquare.FormTemplates.Tests.Synthesizers;

namespace KSquare.FormTemplates.Tests;

public sealed class ReflectionFieldMapperTests
{
    [Fact]
    public void Maps_fields_using_yaml_and_formats_values()
    {
        var options = new FormTemplateOptions();
        var blobs = new NotUsedBlobStorageConnector();
        var loader = new FieldMapLoader(options, blobs);
        var mapper = new ReflectionFieldMapper(loader);

        var synth = new FormTemplateDataSynthesizer(seed: 10);
        var submission = synth.Submission();

        var fields = mapper.MapFields("acord125", submission);

        fields["NamedInsured"].Should().Be(submission.InsuredName);
        fields["MailingAddress"].Should().Be(submission.InsuredAddress);
        fields["EffectiveDate"].Should().Be(submission.PolicyEffectiveDate.ToString("MM/dd/yyyy"));
        fields["BrokerLicenseNo"].Should().Be(submission.BrokerLicenseNumber);
        fields["TotalInsuredValue"].Should().Contain("$");
    }

    [Fact]
    public void Missing_source_properties_produce_nulls()
    {
        var options = new FormTemplateOptions();
        var blobs = new NotUsedBlobStorageConnector();
        var loader = new FieldMapLoader(options, blobs);
        var mapper = new ReflectionFieldMapper(loader);

        var source = new { InsuredName = "X", InsuredAddress = "Y", PolicyEffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow.Date), PolicyExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.Date) };
        var fields = mapper.MapFields("acord125", source);

        fields["BrokerLicenseNo"].Should().BeNull();
    }

    private sealed class NotUsedBlobStorageConnector : IBlobStorageConnector
    {
        public Task<KSquare.BlobStorage.Models.BlobUploadResult> UploadAsync(KSquare.BlobStorage.Models.BlobUploadRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<KSquare.BlobStorage.Models.BlobDownloadResult> DownloadAsync(string blobPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<KSquare.BlobStorage.Models.BlobSasResult> GenerateSasUrlAsync(KSquare.BlobStorage.Models.BlobSasRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string blobPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ArchiveAsync(string blobPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string blobPath, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<KSquare.BlobStorage.Models.BlobListItem> ListAsync(string prefix, CancellationToken ct = default) => throw new NotSupportedException();
    }
}

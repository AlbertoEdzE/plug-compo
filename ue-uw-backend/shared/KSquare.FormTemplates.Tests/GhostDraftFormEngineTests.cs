using System.Text;
using FluentAssertions;
using iText.Kernel.Pdf;
using KSquare.BlobStorage.Contracts;
using KSquare.Correlation.Extensions;
using KSquare.FormTemplates.Configuration;
using KSquare.FormTemplates.Contracts;
using KSquare.FormTemplates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace KSquare.FormTemplates.Tests;

public sealed class GhostDraftFormEngineTests
{
    [Fact]
    public async Task RenderAsync_posts_to_ghostdraft_with_template_id_and_returns_pdf_bytes()
    {
        var expectedPdf = CreateMinimalPdfBytes();

        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/api/v1/render").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/pdf").WithBody(expectedPdf));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsCorrelation();
        services.AddSingleton<IBlobStorageConnector, NotUsedBlobStorageConnector>();

        services.AddKsFormTemplates(o =>
        {
            o.Provider = FormTemplateProvider.GhostDraft;
            o.GhostDraftApiUrl = server.Url;
            o.GhostDraftApiKey = "test-key";
            o.GhostDraftTemplateIdMap["quote-proposal"] = "tpl-quote";
        });

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var engine = scope.ServiceProvider.GetRequiredService<IFormTemplateEngine>();
        var result = await engine.RenderAsync(new KSquare.FormTemplates.Models.FormRenderRequest
        {
            TemplateName = "quote-proposal",
            Fields = new Dictionary<string, string?> { ["InsuredName"] = "Acme Schools", ["QuoteNumber"] = "Q-1" },
            OutputFormat = "pdf",
            RelatedResourceId = "q-1"
        });

        result.Content.Should().Equal(expectedPdf);

        server.LogEntries.Should().ContainSingle();
        var entry = server.LogEntries.Single();
        var body = entry.RequestMessage.Body;
        body.Should().Contain("\"templateId\":\"tpl-quote\"");
        body.Should().Contain("\"InsuredName\":\"Acme Schools\"");

        var keyHeader = entry.RequestMessage.Headers["X-Api-Key"].FirstOrDefault();
        keyHeader.Should().Be("test-key");
    }

    private static byte[] CreateMinimalPdfBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new PdfWriter(ms);
        using var pdf = new PdfDocument(writer);
        pdf.AddNewPage();
        pdf.Close();
        return ms.ToArray();
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

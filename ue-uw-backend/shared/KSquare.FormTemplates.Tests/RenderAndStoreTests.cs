using FluentAssertions;
using KSquare.BlobStorage.Configuration;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Extensions;
using KSquare.Correlation.Extensions;
using KSquare.FormTemplates.Configuration;
using KSquare.FormTemplates.Contracts;
using KSquare.FormTemplates.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.FormTemplates.Tests;

public sealed class RenderAndStoreTests
{
    [Fact]
    public async Task RenderAndStoreAsync_uploads_to_blob_and_returns_sas_url()
    {
        var root = Path.Combine(Path.GetTempPath(), "kspl-formstore-" + Guid.NewGuid().ToString("N"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsCorrelation();
        services.AddKsBlobStorage(o =>
        {
            o.Provider = BlobProvider.LocalFileSystem;
            o.LocalRootPath = root;
        });

        services.AddKsFormTemplates(o =>
        {
            o.Provider = FormTemplateProvider.Mock;
            o.OutputBlobContainer = "generated-forms";
            o.OutputPathTemplate = "forms/{year}/{month}/{resourceId}/{templateName}-{timestamp}.pdf";
        });

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IFormTemplateEngine>();
        var blobs = scope.ServiceProvider.GetRequiredService<IBlobStorageConnector>();

        var result = await engine.RenderAndStoreAsync(new KSquare.FormTemplates.Models.FormRenderRequest
        {
            TemplateName = "binder",
            Fields = new Dictionary<string, string?> { ["InsuredName"] = "X", ["BinderNumber"] = "B-1", ["BindDate"] = "01/01/2026", ["TotalPremium"] = "100.00" },
            OutputFormat = "pdf",
            RelatedResourceId = "sub-123"
        });

        result.BlobPath.Should().StartWith("generated-forms/");
        result.SasUrl.Should().StartWith("file://");
        (await blobs.ExistsAsync(result.BlobPath)).Should().BeTrue();
    }
}


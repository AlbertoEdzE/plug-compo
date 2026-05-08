using iText.Kernel.Pdf;
using KSquare.BlobStorage.Contracts;
using KSquare.Correlation.Contracts;
using KSquare.FormTemplates.Configuration;
using KSquare.FormTemplates.FieldMaps;
using KSquare.FormTemplates.Models;

namespace KSquare.FormTemplates.Providers;

internal sealed class MockFormEngine(
    FormTemplateOptions options,
    FieldMapLoader maps,
    IBlobStorageConnector blobs,
    ICorrelationContextAccessor correlation
) : FormTemplateEngineBase(options, maps, blobs, correlation)
{
    protected override Task<byte[]> RenderCoreAsync(
        FieldMapDefinition map,
        FormRenderRequest request,
        string outputFormat,
        string? correlationId,
        CancellationToken ct
    )
    {
        _ = map;
        _ = request;
        _ = outputFormat;
        _ = correlationId;
        ct.ThrowIfCancellationRequested();

        using var ms = new MemoryStream();
        using var writer = new PdfWriter(ms);
        using var pdf = new PdfDocument(writer);
        pdf.AddNewPage();
        pdf.Close();

        return Task.FromResult(ms.ToArray());
    }
}

using iText.Forms;
using iText.Kernel.Pdf;
using KSquare.BlobStorage.Contracts;
using KSquare.Correlation.Contracts;
using KSquare.FormTemplates.Configuration;
using KSquare.FormTemplates.Exceptions;
using KSquare.FormTemplates.FieldMaps;
using KSquare.FormTemplates.Models;

namespace KSquare.FormTemplates.Providers;

internal sealed class ITextPdfFormEngine(
    FormTemplateOptions options,
    FieldMapLoader maps,
    IBlobStorageConnector blobs,
    ICorrelationContextAccessor correlation
) : FormTemplateEngineBase(options, maps, blobs, correlation)
{
    protected override async Task<byte[]> RenderCoreAsync(
        FieldMapDefinition map,
        FormRenderRequest request,
        string outputFormat,
        string? correlationId,
        CancellationToken ct
    )
    {
        _ = map;
        _ = outputFormat;
        _ = correlationId;

        try
        {
            var template = await DownloadTemplateAsync(request.TemplateName, ct);
            await using (template)
            await using (template.Content)
            {
                using var output = new MemoryStream();
                using var reader = new PdfReader(template.Content);
                using var writer = new PdfWriter(output);
                using var pdf = new PdfDocument(reader, writer);

                var form = PdfAcroForm.GetAcroForm(pdf, true);
                foreach (var (key, value) in request.Fields)
                {
                    if (string.IsNullOrWhiteSpace(key) || value is null)
                    {
                        continue;
                    }

                    var field = form.GetField(key);
                    if (field is null)
                    {
                        continue;
                    }

                    field.SetValue(value);
                }

                form.FlattenFields();
                pdf.Close();
                return output.ToArray();
            }
        }
        catch (FormTemplateNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FormTemplateCorruptException(request.TemplateName, ex);
        }
    }
}

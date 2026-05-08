using FluentAssertions;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using KSquare.BlobStorage.Configuration;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Extensions;
using KSquare.BlobStorage.Models;
using KSquare.Correlation.Extensions;
using KSquare.FormTemplates.Configuration;
using KSquare.FormTemplates.Contracts;
using KSquare.FormTemplates.Extensions;
using KSquare.FormTemplates.Tests.Synthesizers;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.FormTemplates.Tests;

public sealed class ITextPdfFormEngineTests
{
    [Fact]
    public async Task Renders_filled_pdf_from_blob_template()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kspl-formtemplates-" + Guid.NewGuid().ToString("N"));

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
            o.Provider = FormTemplateProvider.ITextPdfFill;
            o.TemplateBlobContainer = "form-templates";
            o.OutputBlobContainer = "generated-forms";
        });

        var sp = services.BuildServiceProvider();
        var blobs = sp.GetRequiredService<IBlobStorageConnector>();

        var templateBytes = CreateTemplatePdf(new[]
        {
            "NamedInsured",
            "MailingAddress",
            "EffectiveDate",
            "ExpirationDate",
            "TotalInsuredValue",
            "BrokerName",
            "BrokerLicenseNo"
        });

        await using (var ms = new MemoryStream(templateBytes))
        {
            await blobs.UploadAsync(new BlobUploadRequest("form-templates", "acord125.pdf", ms, "application/pdf", null));
        }

        using var scope = sp.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<IFormFieldMapper>();
        var engine = scope.ServiceProvider.GetRequiredService<IFormTemplateEngine>();

        var submission = new FormTemplateDataSynthesizer(seed: 1).Submission();
        var fields = mapper.MapFields("acord125", submission);

        var result = await engine.RenderAsync(new KSquare.FormTemplates.Models.FormRenderRequest
        {
            TemplateName = "acord125",
            Fields = fields,
            OutputFormat = "pdf",
            RelatedResourceId = "sub-1"
        });

        var text = ExtractText(result.Content);
        text.Should().Contain(submission.InsuredName);
        text.Should().Contain(submission.BrokerName);
    }

    private static byte[] CreateTemplatePdf(IReadOnlyList<string> fieldNames)
    {
        using var ms = new MemoryStream();
        using var writer = new PdfWriter(ms);
        using var pdf = new PdfDocument(writer);
        var page = pdf.AddNewPage();

        var form = PdfAcroForm.GetAcroForm(pdf, true);
        var y = 760f;

        foreach (var name in fieldNames)
        {
            var rect = new Rectangle(50f, y, 500f, 18f);
            var field = new TextFormFieldBuilder(pdf, name)
                .SetWidgetRectangle(rect)
                .SetPage(page)
                .CreateText();
            form.AddField(field, page);
            y -= 26f;
        }

        pdf.Close();
        return ms.ToArray();
    }

    private static string ExtractText(byte[] pdfBytes)
    {
        using var reader = new PdfReader(new MemoryStream(pdfBytes));
        using var pdf = new PdfDocument(reader);
        var page = pdf.GetFirstPage();
        return PdfTextExtractor.GetTextFromPage(page);
    }
}

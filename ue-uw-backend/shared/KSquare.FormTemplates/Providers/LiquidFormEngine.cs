using Fluid;
using Fluid.Values;
using KSquare.BlobStorage.Contracts;
using KSquare.Correlation.Contracts;
using KSquare.FormTemplates.Configuration;
using KSquare.FormTemplates.Exceptions;
using KSquare.FormTemplates.FieldMaps;
using KSquare.FormTemplates.Models;

namespace KSquare.FormTemplates.Providers;

internal sealed class LiquidFormEngine(
    FormTemplateOptions options,
    FieldMapLoader maps,
    IBlobStorageConnector blobs,
    ICorrelationContextAccessor correlation
) : FormTemplateEngineBase(options, maps, blobs, correlation)
{
    private static readonly FluidParser Parser = new();
    private static readonly TemplateOptions TemplateOptions = new();

    protected override Task<byte[]> RenderCoreAsync(
        FieldMapDefinition map,
        FormRenderRequest request,
        string outputFormat,
        string? correlationId,
        CancellationToken ct
    )
    {
        _ = correlationId;
        ct.ThrowIfCancellationRequested();

        if (!outputFormat.Equals("html", StringComparison.OrdinalIgnoreCase))
        {
            outputFormat = "html";
        }

        const string templateText = """
            <html>
              <head><meta charset="utf-8"/><title>{{ display_name }}</title></head>
              <body>
                <h1>{{ display_name }}</h1>
                <p>Template: {{ template_name }} ({{ version }})</p>
                <table border="1" cellpadding="6" cellspacing="0">
                  <thead><tr><th>Field</th><th>Value</th></tr></thead>
                  <tbody>
                  {% for kv in fields %}
                    <tr><td>{{ kv[0] }}</td><td>{{ kv[1] }}</td></tr>
                  {% endfor %}
                  </tbody>
                </table>
              </body>
            </html>
            """;

        if (!Parser.TryParse(templateText, out var template, out var error))
        {
            throw new FormRenderException(error);
        }

        var ctx = new TemplateContext(TemplateOptions);
        ctx.SetValue("template_name", map.TemplateName);
        ctx.SetValue("display_name", map.DisplayName);
        ctx.SetValue("version", map.Version);

        var array = request.Fields.Select(kv => new[] { kv.Key, kv.Value ?? "" }).ToList();
        ctx.SetValue("fields", FluidValue.Create(array, TemplateOptions));

        var rendered = template.Render(ctx);
        return Task.FromResult(System.Text.Encoding.UTF8.GetBytes(rendered));
    }
}

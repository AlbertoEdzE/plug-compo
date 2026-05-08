using System.Reflection;
using KSquare.EmailSend.Exceptions;

namespace KSquare.EmailSend.Templates.TemplateLoader;

public sealed class EmbeddedResourceTemplateLoader : ITemplateLoader
{
    private readonly Assembly _assembly;
    private readonly string _resourcePrefix;

    public EmbeddedResourceTemplateLoader()
    {
        _assembly = typeof(EmbeddedResourceTemplateLoader).Assembly;
        _resourcePrefix = "KSquare.EmailSend.Templates.Resources";
    }

    public async Task<(string HtmlTemplate, string TextTemplate)> LoadAsync(string templateName, CancellationToken ct = default)
    {
        var html = await ReadResourceAsync(templateName, $"{_resourcePrefix}.{templateName}.liquid.html", ct);
        var text = await ReadResourceAsync(templateName, $"{_resourcePrefix}.{templateName}.liquid.txt", ct);

        return (html, text);
    }

    private async Task<string> ReadResourceAsync(string templateName, string resourceName, CancellationToken ct)
    {
        await using var stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new EmailTemplateNotFoundException(templateName);
        }

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(ct);
        return content;
    }
}

using KSquare.EmailSend.Exceptions;

namespace KSquare.EmailSend.Templates.TemplateLoader;

public sealed class FileSystemTemplateLoader : ITemplateLoader
{
    private readonly string _rootPath;

    public FileSystemTemplateLoader(string? rootPath = null)
    {
        _rootPath = string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(AppContext.BaseDirectory, "email-templates")
            : rootPath;
    }

    public async Task<(string HtmlTemplate, string TextTemplate)> LoadAsync(string templateName, CancellationToken ct = default)
    {
        var htmlPath = Path.Combine(_rootPath, $"{templateName}.liquid.html");
        var textPath = Path.Combine(_rootPath, $"{templateName}.liquid.txt");

        if (!File.Exists(htmlPath) || !File.Exists(textPath))
        {
            throw new EmailTemplateNotFoundException(templateName);
        }

        var html = await File.ReadAllTextAsync(htmlPath, ct);
        var text = await File.ReadAllTextAsync(textPath, ct);
        return (html, text);
    }
}

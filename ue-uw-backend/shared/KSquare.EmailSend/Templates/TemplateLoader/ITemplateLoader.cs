namespace KSquare.EmailSend.Templates.TemplateLoader;

public interface ITemplateLoader
{
    Task<(string HtmlTemplate, string TextTemplate)> LoadAsync(string templateName, CancellationToken ct = default);
}

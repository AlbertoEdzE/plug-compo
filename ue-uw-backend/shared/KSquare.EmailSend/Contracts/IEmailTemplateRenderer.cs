using KSquare.EmailSend.Models;

namespace KSquare.EmailSend.Contracts;

public interface IEmailTemplateRenderer
{
    Task<RenderedEmail> RenderAsync<TModel>(string templateName, TModel model, CancellationToken ct = default);
}

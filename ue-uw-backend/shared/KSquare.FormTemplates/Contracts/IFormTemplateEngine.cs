namespace KSquare.FormTemplates.Contracts;

using KSquare.FormTemplates.Models;

public interface IFormTemplateEngine
{
    Task<IReadOnlyList<FormTemplateDescriptor>> ListTemplatesAsync(CancellationToken ct = default);
    Task<FormRenderResult> RenderAsync(FormRenderRequest request, CancellationToken ct = default);
    Task<FormRenderAndStoreResult> RenderAndStoreAsync(FormRenderRequest request, CancellationToken ct = default);
}


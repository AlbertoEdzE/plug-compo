namespace KSquare.FormTemplates.Models;

public sealed record FormRenderAndStoreResult(
    FormRenderResult RenderResult,
    string BlobPath,
    string SasUrl,
    DateTimeOffset SasExpiry
);


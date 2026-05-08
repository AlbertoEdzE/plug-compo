using System.Net;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Models;
using KSquare.Correlation.Contracts;
using KSquare.FormTemplates.Configuration;
using KSquare.FormTemplates.Contracts;
using KSquare.FormTemplates.Exceptions;
using KSquare.FormTemplates.FieldMaps;
using KSquare.FormTemplates.Models;

namespace KSquare.FormTemplates.Providers;

internal abstract class FormTemplateEngineBase(
    FormTemplateOptions options,
    FieldMapLoader maps,
    IBlobStorageConnector blobs,
    ICorrelationContextAccessor correlation
) : IFormTemplateEngine
{
    public async Task<IReadOnlyList<FormTemplateDescriptor>> ListTemplatesAsync(CancellationToken ct = default)
    {
        var names = maps.ListEmbeddedTemplates();
        var results = new List<FormTemplateDescriptor>();
        foreach (var name in names)
        {
            var map = await maps.LoadAsync(name, ct);
            results.Add(ToDescriptor(map));
        }

        return results;
    }

    public async Task<FormRenderResult> RenderAsync(FormRenderRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.TemplateName))
        {
            throw new ArgumentException("TemplateName is required.", nameof(request));
        }

        var map = await maps.LoadAsync(request.TemplateName, ct);
        var unfilled = FindUnfilledRequired(map, request.Fields);

        if (options.StrictRequiredFieldValidation && unfilled.Count > 0)
        {
            throw new FormRenderException($"Required fields missing: {string.Join(", ", unfilled)}");
        }

        var outputFormat = request.OutputFormat ?? map.OutputFormat ?? "pdf";
        var (contentType, extension) = ContentTypeAndExtension(outputFormat);

        var correlationId = request.CorrelationId ?? correlation.Current?.CorrelationId;
        var bytes = await RenderCoreAsync(map, request, outputFormat, correlationId, ct);

        return new FormRenderResult
        {
            Content = bytes,
            ContentType = contentType,
            FileName = $"{request.TemplateName}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{extension}",
            TemplateName = request.TemplateName,
            TemplateVersion = map.Version,
            UnfilledRequiredFields = unfilled
        };
    }

    public async Task<FormRenderAndStoreResult> RenderAndStoreAsync(FormRenderRequest request, CancellationToken ct = default)
    {
        var render = await RenderAsync(request, ct);

        var resourceId = string.IsNullOrWhiteSpace(request.RelatedResourceId) ? "unknown" : request.RelatedResourceId;
        var blobPath = BuildOutputPath(options.OutputPathTemplate, resourceId, request.TemplateName, render.ContentType);

        await using var ms = new MemoryStream(render.Content, writable: false);
        var upload = await blobs.UploadAsync(
            new BlobUploadRequest(
                options.OutputBlobContainer,
                blobPath,
                ms,
                render.ContentType,
                new Dictionary<string, string>
                {
                    ["templateName"] = request.TemplateName,
                    ["templateVersion"] = render.TemplateVersion,
                    ["resourceId"] = resourceId
                }
            ),
            ct
        );

        var sas = await blobs.GenerateSasUrlAsync(
            new BlobSasRequest(
                options.OutputBlobContainer,
                blobPath,
                BlobSasPermissions.Read,
                options.OutputSasTtl,
                $"attachment; filename=\"{render.FileName}\""
            ),
            ct
        );

        return new FormRenderAndStoreResult(render, upload.BlobPath, sas.SasUrl, sas.ExpiresAt);
    }

    protected abstract Task<byte[]> RenderCoreAsync(
        FieldMapDefinition map,
        FormRenderRequest request,
        string outputFormat,
        string? correlationId,
        CancellationToken ct
    );

    protected async Task<BlobDownloadResult> DownloadTemplateAsync(string templateName, CancellationToken ct)
    {
        var blobPath = $"{options.TemplateBlobContainer}/{templateName}.pdf";
        return await blobs.DownloadAsync(blobPath, ct);
    }

    protected string ResolveTemplateId(string templateName)
    {
        if (!options.GhostDraftTemplateIdMap.TryGetValue(templateName, out var id) || string.IsNullOrWhiteSpace(id))
        {
            throw new FormTemplateNotFoundException(templateName);
        }

        return id;
    }

    private static List<string> FindUnfilledRequired(FieldMapDefinition map, IDictionary<string, string?> fields)
    {
        return map.Fields
            .Where(f => f.Required)
            .Select(f => f.Placeholder)
            .Where(p => !fields.TryGetValue(p, out var v) || string.IsNullOrWhiteSpace(v))
            .ToList();
    }

    private static FormTemplateDescriptor ToDescriptor(FieldMapDefinition map)
    {
        return new FormTemplateDescriptor
        {
            TemplateName = map.TemplateName,
            DisplayName = map.DisplayName,
            Version = map.Version,
            OutputFormat = map.OutputFormat,
            Fields = map.Fields.Select(f => new FormFieldDescriptor(
                f.Placeholder,
                string.IsNullOrWhiteSpace(f.DisplayLabel) ? f.Placeholder : f.DisplayLabel,
                f.Required,
                f.Type
            )).ToList()
        };
    }

    private static string BuildOutputPath(string template, string resourceId, string templateName, string contentType)
    {
        var now = DateTimeOffset.UtcNow;
        var timestamp = now.ToString("yyyyMMddHHmmss");
        var year = now.Year.ToString("D4");
        var month = now.Month.ToString("D2");

        var ext = contentType.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase) ? "docx"
            : contentType.Contains("html", StringComparison.OrdinalIgnoreCase) ? "html"
            : "pdf";

        var path = template
            .Replace("{year}", year, StringComparison.OrdinalIgnoreCase)
            .Replace("{month}", month, StringComparison.OrdinalIgnoreCase)
            .Replace("{resourceId}", resourceId, StringComparison.OrdinalIgnoreCase)
            .Replace("{templateName}", templateName, StringComparison.OrdinalIgnoreCase)
            .Replace("{timestamp}", timestamp, StringComparison.OrdinalIgnoreCase);

        if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !ext.Equals("pdf", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(0, path.Length - 4) + "." + ext;
        }

        return path.TrimStart('/');
    }

    private static (string ContentType, string Extension) ContentTypeAndExtension(string outputFormat)
    {
        if (outputFormat.Equals("docx", StringComparison.OrdinalIgnoreCase))
        {
            return ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx");
        }

        if (outputFormat.Equals("html", StringComparison.OrdinalIgnoreCase))
        {
            return ("text/html", "html");
        }

        return ("application/pdf", "pdf");
    }
}


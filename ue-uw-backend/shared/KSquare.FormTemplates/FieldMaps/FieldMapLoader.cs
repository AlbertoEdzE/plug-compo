using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using KSquare.BlobStorage.Contracts;
using KSquare.FormTemplates.Configuration;
using KSquare.FormTemplates.Exceptions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KSquare.FormTemplates.FieldMaps;

internal sealed class FieldMapLoader(FormTemplateOptions options, IBlobStorageConnector blobs)
{
    private static readonly Assembly Assembly = typeof(FieldMapLoader).Assembly;

    private readonly ConcurrentDictionary<string, FieldMapDefinition> _cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public async Task<FieldMapDefinition> LoadAsync(string templateName, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(templateName, out var cached))
        {
            return cached;
        }

        var yaml = await LoadYamlAsync(templateName, ct);
        FieldMapDefinition parsed;
        try
        {
            parsed = _deserializer.Deserialize<FieldMapDefinition>(yaml) ?? throw new FormRenderException($"Field map for '{templateName}' is empty.");
        }
        catch (Exception ex) when (ex is not FormRenderException)
        {
            throw new FormRenderException($"Failed to parse field map YAML for '{templateName}'.", ex);
        }

        if (!string.Equals(parsed.TemplateName, templateName, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormRenderException($"Field map template_name '{parsed.TemplateName}' does not match requested '{templateName}'.");
        }

        _cache[templateName] = parsed;
        return parsed;
    }

    public IReadOnlyList<string> ListEmbeddedTemplates()
    {
        return Assembly.GetManifestResourceNames()
            .Where(n => n.Contains(".FieldMaps.Resources.", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            .Select(n =>
            {
                var marker = ".FieldMaps.Resources.";
                var idx = n.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                var start = idx + marker.Length;
                var rest = n.Substring(start);
                var file = rest.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ? rest.Substring(0, rest.Length - 4) : rest;
                return file.EndsWith("-field-map", StringComparison.OrdinalIgnoreCase) ? file.Substring(0, file.Length - "-field-map".Length) : file;
            })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    private async Task<string> LoadYamlAsync(string templateName, CancellationToken ct)
    {
        var embedded = TryLoadEmbeddedYaml(templateName);
        if (embedded is not null)
        {
            return embedded;
        }

        var blobPath = $"{options.TemplateBlobContainer}/templates/{templateName}-field-map.yml";
        try
        {
            var dl = await blobs.DownloadAsync(blobPath, ct);
            await using (dl)
            {
                using var reader = new StreamReader(dl.Content, Encoding.UTF8, leaveOpen: false);
                return await reader.ReadToEndAsync(ct);
            }
        }
        catch (Exception)
        {
            throw new FormTemplateNotFoundException(templateName);
        }
    }

    private static string? TryLoadEmbeddedYaml(string templateName)
    {
        var expectedSuffix1 = $".FieldMaps.Resources.{templateName}.yml";
        var expectedSuffix2 = $".FieldMaps.Resources.{templateName}-field-map.yml";
        var resource = Assembly.GetManifestResourceNames().FirstOrDefault(n =>
            n.EndsWith(expectedSuffix1, StringComparison.OrdinalIgnoreCase) ||
            n.EndsWith(expectedSuffix2, StringComparison.OrdinalIgnoreCase)
        );
        if (resource is null)
        {
            return null;
        }

        using var stream = Assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

internal sealed record FieldMapDefinition
{
    public required string TemplateName { get; init; }
    public required string Version { get; init; }
    public required string OutputFormat { get; init; }
    public required string DisplayName { get; init; }
    public required List<FieldMapField> Fields { get; init; }
}

internal sealed record FieldMapField
{
    public required string Placeholder { get; init; }
    public required string DomainPath { get; init; }
    public bool Required { get; init; }
    public string Type { get; init; } = "text";
    public string? Format { get; init; }
    public string? DisplayLabel { get; init; }
}

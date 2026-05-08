using System.Collections.Concurrent;
using System.Text;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Exceptions;
using KSquare.EmailSend.Configuration;
using KSquare.EmailSend.Exceptions;

namespace KSquare.EmailSend.Templates.TemplateLoader;

public sealed class BlobTemplateLoader(EmailSendOptions options, IBlobStorageConnector blob) : ITemplateLoader
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<(string HtmlTemplate, string TextTemplate)> LoadAsync(string templateName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.TemplateBlobContainerName))
        {
            throw new InvalidOperationException("TemplateBlobContainerName must be set for BlobStorage template source.");
        }

        var html = await GetOrLoadAsync($"{templateName}.liquid.html", templateName, ct);
        var text = await GetOrLoadAsync($"{templateName}.liquid.txt", templateName, ct);
        return (html, text);
    }

    private async Task<string> GetOrLoadAsync(string fileName, string templateName, CancellationToken ct)
    {
        var key = $"{options.TemplateBlobContainerName}/{fileName}";

        if (_cache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return entry.Content;
        }

        try
        {
            await using var download = await blob.DownloadAsync($"{options.TemplateBlobContainerName}/{fileName}", ct);
            using var reader = new StreamReader(download.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var content = await reader.ReadToEndAsync(ct);

            _cache[key] = new CacheEntry(content, DateTimeOffset.UtcNow.AddMinutes(5));
            return content;
        }
        catch (BlobNotFoundException)
        {
            throw new EmailTemplateNotFoundException(templateName);
        }
        catch (Exception ex)
        {
            throw new EmailSendException($"Failed to load template '{templateName}' from blob storage.", ex);
        }
    }

    private sealed record CacheEntry(string Content, DateTimeOffset ExpiresAt);
}

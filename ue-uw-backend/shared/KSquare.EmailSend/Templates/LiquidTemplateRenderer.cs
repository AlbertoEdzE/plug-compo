using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Fluid;
using KSquare.EmailSend.Contracts;
using KSquare.EmailSend.Exceptions;
using KSquare.EmailSend.Models;
using KSquare.EmailSend.Templates.TemplateLoader;

namespace KSquare.EmailSend.Templates;

public sealed class LiquidTemplateRenderer(ITemplateLoader loader) : IEmailTemplateRenderer
{
    private static readonly Regex TitleRegex = new("<title\\s*>\\s*(?<t>.*?)\\s*</title\\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex H1Regex = new("<h1\\b[^>]*>\\s*(?<t>.*?)\\s*</h1\\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private readonly ConcurrentDictionary<string, IFluidTemplate> _templateCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Type, byte> _registeredTypes = new();
    private readonly TemplateOptions _options = new();
    private readonly FluidParser _parser = new();

    public async Task<RenderedEmail> RenderAsync<TModel>(string templateName, TModel model, CancellationToken ct = default)
    {
        try
        {
            var (htmlTemplate, textTemplate) = await loader.LoadAsync(templateName, ct);

            var html = await RenderTemplateAsync($"{templateName}.html", htmlTemplate, model);
            var text = await RenderTemplateAsync($"{templateName}.txt", textTemplate, model);

            var subject = ExtractSubject(html) ?? templateName;
            return new RenderedEmail(subject, html, text);
        }
        catch (EmailTemplateNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EmailTemplateRenderException($"Failed to render template '{templateName}'.", ex);
        }
    }

    private async Task<string> RenderTemplateAsync<TModel>(string cacheKey, string template, TModel model)
    {
        if (!_templateCache.TryGetValue(cacheKey, out var parsed))
        {
            if (!_parser.TryParse(template, out var fluidTemplate, out var error))
            {
                throw new EmailTemplateRenderException(error);
            }

            parsed = fluidTemplate;
            _templateCache[cacheKey] = parsed;
        }

        RegisterType(typeof(TModel));
        var context = new TemplateContext(_options);
        foreach (var (key, value) in FlattenModel(model))
        {
            context.SetValue(key, value);
        }

        return await parsed.RenderAsync(context);
    }

    private void RegisterType(Type type)
    {
        if (_registeredTypes.TryAdd(type, 0))
        {
            _options.MemberAccessStrategy.Register(type);
        }
    }

    private static IReadOnlyDictionary<string, object?> FlattenModel<TModel>(TModel model)
    {
        if (model is null)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead)
            {
                continue;
            }

            dict[prop.Name] = prop.GetValue(model);
        }

        return dict;
    }

    private static string? ExtractSubject(string html)
    {
        var title = TitleRegex.Match(html);
        if (title.Success)
        {
            return NormalizeSubject(title.Groups["t"].Value);
        }

        var h1 = H1Regex.Match(html);
        if (h1.Success)
        {
            return NormalizeSubject(h1.Groups["t"].Value);
        }

        return null;
    }

    private static string NormalizeSubject(string input)
    {
        var plain = Regex.Replace(input, "<.*?>", string.Empty, RegexOptions.Singleline);
        return System.Net.WebUtility.HtmlDecode(plain).Trim();
    }
}

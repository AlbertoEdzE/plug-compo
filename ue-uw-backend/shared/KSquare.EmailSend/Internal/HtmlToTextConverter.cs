using System.Text.RegularExpressions;

namespace KSquare.EmailSend.Internal;

internal static class HtmlToTextConverter
{
    private static readonly Regex TagsRegex = new("<.*?>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);

    public static string Convert(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withoutTags = TagsRegex.Replace(html, " ");
        var normalized = WhitespaceRegex.Replace(withoutTags, " ").Trim();
        return System.Net.WebUtility.HtmlDecode(normalized);
    }
}

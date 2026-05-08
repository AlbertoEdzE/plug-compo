using KSquare.EmailIngestion.Models;

namespace KSquare.EmailIngestion.Internal;

internal static class IntentHintDetector
{
    public static string? DetectIntentHint(EmailMessage email)
    {
        var bodySnippet = email.BodyText.Length <= 0
            ? string.Empty
            : email.BodyText[..Math.Min(500, email.BodyText.Length)];

        var lower = (email.Subject + " " + bodySnippet).ToLowerInvariant();

        if (lower.Contains("new submission") || lower.Contains("new account") || lower.Contains("new business"))
        {
            return "NewSubmission";
        }

        if (lower.Contains("renewal") || lower.Contains("re-quote"))
        {
            return "Renewal";
        }

        if (lower.Contains("update") || lower.Contains("additional info") || lower.Contains("follow up"))
        {
            return "Update";
        }

        return "Unknown";
    }
}

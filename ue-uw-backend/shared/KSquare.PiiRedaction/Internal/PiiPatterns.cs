using System.Text.RegularExpressions;

namespace KSquare.PiiRedaction.Internal;

internal static class PiiPatterns
{
    internal static readonly Regex Email = new(
        @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    internal static readonly Regex Phone = new(
        @"\b(\+?1?\s?)?(\(?\d{3}\)?[\s.\-]?)?\d{3}[\s.\-]?\d{4}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    internal static readonly Regex Ssn = new(
        @"\b(\d{3}-\d{2}-\d{4}|\d{9})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );
}

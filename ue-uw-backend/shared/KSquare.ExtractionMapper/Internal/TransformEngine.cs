using System.Globalization;

namespace KSquare.ExtractionMapper.Internal;

internal static class TransformEngine
{
    public static bool TryApplyAndConvert(
        string? input,
        string? transformExpression,
        string targetType,
        out object? value,
        out string? failureMessage)
    {
        failureMessage = null;
        value = null;

        if (input is null)
        {
            return true;
        }

        var normalizedTargetType = NormalizeTargetType(targetType);
        var transforms = ParseTransformPipeline(transformExpression);

        object current = input;

        foreach (var t in transforms)
        {
            if (current is null)
            {
                value = null;
                return true;
            }

            if (t.Name.Equals("Trim", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not string s)
                {
                    failureMessage = "Trim transform requires a string input.";
                    return false;
                }

                current = s.Trim();
                continue;
            }

            if (t.Name.Equals("ToUpper", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not string s)
                {
                    failureMessage = "ToUpper transform requires a string input.";
                    return false;
                }

                current = s.ToUpperInvariant();
                continue;
            }

            if (t.Name.Equals("StripCurrency", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not string s)
                {
                    failureMessage = "StripCurrency transform requires a string input.";
                    return false;
                }

                current = StripCurrency(s);
                continue;
            }

            if (t.Name.Equals("ParseDate", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not string s)
                {
                    failureMessage = "ParseDate transform requires a string input.";
                    return false;
                }

                if (!TryParseDateOnly(s, t.Arg, out var date))
                {
                    failureMessage = $"Could not parse date '{s}'.";
                    return false;
                }

                current = date;
                continue;
            }

            if (t.Name.Equals("ParseDecimal", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not string s)
                {
                    failureMessage = "ParseDecimal transform requires a string input.";
                    return false;
                }

                if (!TryParseDecimal(s, out var dec))
                {
                    failureMessage = $"Could not parse decimal '{s}'.";
                    return false;
                }

                current = dec;
                continue;
            }

            if (t.Name.Equals("ParseBool", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not string s)
                {
                    failureMessage = "ParseBool transform requires a string input.";
                    return false;
                }

                if (!TryParseBool(s, out var b))
                {
                    failureMessage = $"Could not parse boolean '{s}'.";
                    return false;
                }

                current = b;
                continue;
            }

            failureMessage = $"Unsupported transform '{t.Name}'.";
            return false;
        }

        if (current is null)
        {
            value = null;
            return true;
        }

        if (normalizedTargetType == "string")
        {
            value = current.ToString();
            return true;
        }

        if (current is string finalString)
        {
            if (!TryConvertFromString(finalString, normalizedTargetType, out value))
            {
                failureMessage = $"Could not convert '{finalString}' to {normalizedTargetType}.";
                return false;
            }

            return true;
        }

        if (normalizedTargetType == "date" && current is DateOnly d)
        {
            value = d;
            return true;
        }

        if (normalizedTargetType == "decimal" && current is decimal decValue)
        {
            value = decValue;
            return true;
        }

        if (normalizedTargetType == "int" && current is int i)
        {
            value = i;
            return true;
        }

        if (normalizedTargetType == "bool" && current is bool bValue)
        {
            value = bValue;
            return true;
        }

        failureMessage = $"Value type '{current.GetType().Name}' does not match target type '{normalizedTargetType}'.";
        return false;
    }

    private static string NormalizeTargetType(string targetType)
    {
        var t = targetType.Trim().ToLowerInvariant();
        return t switch
        {
            "string" => "string",
            "decimal" => "decimal",
            "date" => "date",
            "bool" => "bool",
            "boolean" => "bool",
            "int" => "int",
            "integer" => "int",
            _ => t
        };
    }

    private static IReadOnlyList<TransformCall> ParseTransformPipeline(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return Array.Empty<TransformCall>();
        }

        var parts = expression.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var results = new List<TransformCall>(parts.Length);

        foreach (var p in parts)
        {
            var idx = p.IndexOf(':', StringComparison.Ordinal);
            if (idx < 0)
            {
                results.Add(new TransformCall(p, null));
                continue;
            }

            results.Add(new TransformCall(p[..idx], p[(idx + 1)..]));
        }

        return results;
    }

    private static bool TryConvertFromString(string input, string targetType, out object? value)
    {
        value = null;

        switch (targetType)
        {
            case "int":
                if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                {
                    value = i;
                    return true;
                }

                return false;
            case "decimal":
                if (TryParseDecimal(input, out var dec))
                {
                    value = dec;
                    return true;
                }

                return false;
            case "bool":
                if (TryParseBool(input, out var b))
                {
                    value = b;
                    return true;
                }

                return false;
            case "date":
                if (TryParseDateOnly(input, null, out var d))
                {
                    value = d;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static bool TryParseDateOnly(string input, string? format, out DateOnly value)
    {
        if (!string.IsNullOrWhiteSpace(format))
        {
            return DateOnly.TryParseExact(
                input.Trim(),
                format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out value);
        }

        return DateOnly.TryParse(input.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static bool TryParseDecimal(string input, out decimal value)
    {
        var trimmed = input.Trim();
        var negative = false;

        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
        {
            negative = true;
            trimmed = trimmed[1..^1];
        }

        trimmed = StripCurrency(trimmed);

        var ok = decimal.TryParse(
            trimmed,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);

        if (!ok)
        {
            return false;
        }

        if (negative)
        {
            value = -value;
        }

        return true;
    }

    private static bool TryParseBool(string input, out bool value)
    {
        var s = input.Trim();

        if (bool.TryParse(s, out value))
        {
            return true;
        }

        if (s.Equals("y", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (s.Equals("n", StringComparison.OrdinalIgnoreCase) || s.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        if (s.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (s.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = default;
        return false;
    }

    private static string StripCurrency(string input)
    {
        return input
            .Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private readonly record struct TransformCall(string Name, string? Arg);
}


using System.Globalization;
using System.Reflection;
using KSquare.FormTemplates.Contracts;
using KSquare.FormTemplates.FieldMaps;

namespace KSquare.FormTemplates.Internal;

internal sealed class ReflectionFieldMapper(FieldMapLoader loader) : IFormFieldMapper
{
    public IDictionary<string, string?> MapFields<TSource>(string templateName, TSource source)
        where TSource : class
    {
        var map = loader.LoadAsync(templateName).GetAwaiter().GetResult();

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in map.Fields)
        {
            var raw = ReadByPath(source, field.DomainPath);
            result[field.Placeholder] = FormatValue(raw, field.Type, field.Format);
        }

        return result;
    }

    private static object? ReadByPath(object source, string path)
    {
        object? current = source;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is null)
            {
                return null;
            }

            var type = current.GetType();
            var prop = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));

            if (prop is null)
            {
                return null;
            }

            current = prop.GetValue(current);
        }

        return current;
    }

    private static string? FormatValue(object? value, string fieldType, string? format)
    {
        if (value is null)
        {
            return null;
        }

        var type = fieldType.Trim().ToLowerInvariant();
        switch (type)
        {
            case "text":
            case "multiline":
                return value.ToString();
            case "date":
                return FormatDate(value, format);
            case "decimal":
                return FormatDecimal(value, format);
            case "boolean":
                return FormatBoolean(value, format);
            default:
                return value.ToString();
        }
    }

    private static string FormatDate(object value, string? format)
    {
        var fmt = string.IsNullOrWhiteSpace(format) ? "MM/dd/yyyy" : format;
        return value switch
        {
            DateOnly d => d.ToString(fmt, CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString(fmt, CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString(fmt, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
    }

    private static string FormatDecimal(object value, string? format)
    {
        var fmt = string.IsNullOrWhiteSpace(format) ? "0.##" : format;
        if (value is decimal dec)
        {
            return dec.ToString(fmt, CultureInfo.GetCultureInfo("en-US"));
        }

        if (value is double dbl)
        {
            return dbl.ToString(fmt, CultureInfo.GetCultureInfo("en-US"));
        }

        if (value is float fl)
        {
            return fl.ToString(fmt, CultureInfo.GetCultureInfo("en-US"));
        }

        if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed.ToString(fmt, CultureInfo.GetCultureInfo("en-US"));
        }

        return value.ToString() ?? "";
    }

    private static string FormatBoolean(object value, string? format)
    {
        var truthy = value switch
        {
            bool b => b,
            string s => s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || s.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        if (string.Equals(format, "YesNo", StringComparison.OrdinalIgnoreCase))
        {
            return truthy ? "Yes" : "No";
        }

        return truthy ? "true" : "false";
    }
}

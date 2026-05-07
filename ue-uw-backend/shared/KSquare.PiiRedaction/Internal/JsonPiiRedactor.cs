using System.Text.Json;
using System.Text.Json.Nodes;
using KSquare.PiiRedaction.Configuration;
using KSquare.PiiRedaction.Contracts;
using Microsoft.Extensions.Logging;

namespace KSquare.PiiRedaction.Internal;

internal sealed class JsonPiiRedactor(
    PiiRedactionOptions options,
    ILogger<JsonPiiRedactor> logger
) : IPiiRedactor
{
    private readonly HashSet<string> _piiFields = new(options.PiiFieldNames, StringComparer.OrdinalIgnoreCase);

    public string RedactJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node is null)
            {
                return json;
            }

            RedactNode(node, parentFieldName: null);
            return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid JSON passed to RedactJson");
            return json;
        }
    }

    public string RedactValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (options.DetectEmailPatterns && PiiPatterns.Email.IsMatch(value))
        {
            return options.RedactionToken;
        }

        if (options.DetectPhonePatterns && PiiPatterns.Phone.IsMatch(value))
        {
            return options.RedactionToken;
        }

        if (options.DetectSsnPatterns && PiiPatterns.Ssn.IsMatch(value))
        {
            return options.RedactionToken;
        }

        return value;
    }

    public bool IsPiiField(string fieldName) => _piiFields.Contains(fieldName);

    private void RedactNode(JsonNode node, string? parentFieldName)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                var name = property.Key;
                var value = property.Value;

                if (IsPiiField(name))
                {
                    obj[name] = options.RedactionToken;
                    continue;
                }

                if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
                {
                    var redacted = RedactValue(stringValue);
                    if (!ReferenceEquals(redacted, stringValue) && !string.Equals(redacted, stringValue, StringComparison.Ordinal))
                    {
                        obj[name] = redacted;
                    }

                    continue;
                }

                if (value is not null)
                {
                    RedactNode(value, name);
                }
            }

            return;
        }

        if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var item = array[i];
                if (item is null)
                {
                    continue;
                }

                if (item is JsonValue arrayValue && arrayValue.TryGetValue<string>(out var stringValue))
                {
                    var redacted = RedactValue(stringValue);
                    if (!string.Equals(redacted, stringValue, StringComparison.Ordinal))
                    {
                        array[i] = redacted;
                    }

                    continue;
                }

                RedactNode(item, parentFieldName);
            }
        }
    }
}

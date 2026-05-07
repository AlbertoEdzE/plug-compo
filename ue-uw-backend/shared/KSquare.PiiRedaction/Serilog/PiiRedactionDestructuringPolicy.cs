using KSquare.PiiRedaction.Contracts;
using System.Text.Json;

namespace KSquare.PiiRedaction.Serilog;

public sealed class PiiRedactionDestructuringPolicy(IPiiRedactor redactor) : global::Serilog.Core.IDestructuringPolicy
{
    public bool TryDestructure(
        object value,
        global::Serilog.Core.ILogEventPropertyValueFactory propertyValueFactory,
        out global::Serilog.Events.LogEventPropertyValue result
    )
    {
        if (value is string stringValue)
        {
            result = propertyValueFactory.CreatePropertyValue(redactor.RedactValue(stringValue));
            return true;
        }

        try
        {
            var json = JsonSerializer.Serialize(value);
            var redactedJson = redactor.RedactJson(json);
            result = propertyValueFactory.CreatePropertyValue(redactedJson);
            return true;
        }
        catch
        {
            result = null!;
            return false;
        }
    }
}

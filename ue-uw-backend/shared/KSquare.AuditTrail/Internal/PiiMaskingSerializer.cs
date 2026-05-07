using KSquare.AuditTrail.Configuration;
using KSquare.PiiRedaction.Contracts;

namespace KSquare.AuditTrail.Internal;

internal sealed class PiiMaskingSerializer(AuditTrailOptions options, IPiiRedactor redactor)
{
    public string? MaskJson(string? json)
    {
        if (!options.MaskPiiInBeforeAfter)
        {
            return json;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        return redactor.RedactJson(json);
    }
}

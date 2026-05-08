using System.Text.Json.Serialization;

namespace KSquare.DocumentNarrative.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NarrativeType
{
    RiskSummary,
    LossRunNarrative,
    ReferralMemo,
    UnderwriterFileNote
}

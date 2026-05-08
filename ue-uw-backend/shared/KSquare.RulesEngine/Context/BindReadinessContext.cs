namespace KSquare.RulesEngine.Context;

public sealed class BindReadinessContext
{
    public string QuoteStatus { get; init; } = "";
    public bool HasSignedApplication { get; init; }
    public bool PremiumAgreedByBroker { get; init; }
    public bool ComplianceCheckPassed { get; init; }
    public bool ReferralApproved { get; init; }
}


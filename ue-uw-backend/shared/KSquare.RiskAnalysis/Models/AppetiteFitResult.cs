namespace KSquare.RiskAnalysis.Models;

public sealed record AppetiteFitResult
{
    public required float Score { get; init; }
    public required string Classification { get; init; }
    public required IReadOnlyList<string> FiredRules { get; init; }
    public required IReadOnlyList<string> RisksIdentified { get; init; }
    public bool RequiresReferral { get; init; }
    public string? ReferralReason { get; init; }
}


namespace KSquare.PolicyAdminAdapter.Models;

public sealed record BindReadinessResult
{
    public required bool IsReady { get; init; }
    public IReadOnlyList<BindReadinessIssue> Issues { get; init; } = [];
}


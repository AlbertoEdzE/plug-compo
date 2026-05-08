namespace KSquare.RulesEngine.Context;

public sealed class IntakeRoutingContext
{
    public decimal TotalInsuredValue { get; init; }
    public int BrokerTenureMonths { get; init; }
    public string NaicsCode { get; init; } = "";
    public IReadOnlyList<string> MissingRequiredFields { get; init; } = Array.Empty<string>();
    public int NumberOfLocations { get; init; }
    public string? SubmissionSource { get; init; }
}


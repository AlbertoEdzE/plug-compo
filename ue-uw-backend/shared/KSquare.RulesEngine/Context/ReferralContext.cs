namespace KSquare.RulesEngine.Context;

public sealed class ReferralContext
{
    public decimal LargestSingleLoss { get; init; }
    public decimal FiveYearLossRatio { get; init; }
    public int NumberOfLocations { get; init; }
    public string NaicsCode { get; init; } = "";
    public IReadOnlyList<string> OutOfAppetiteNaicsCodes { get; init; } = Array.Empty<string>();
    public decimal TotalInsuredValue { get; init; }
}


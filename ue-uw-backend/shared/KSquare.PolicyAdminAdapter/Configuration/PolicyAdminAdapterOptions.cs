namespace KSquare.PolicyAdminAdapter.Configuration;

using KSquare.PolicyAdminAdapter.Models;

public sealed class PolicyAdminAdapterOptions
{
    public PolicyAdminProvider Provider { get; set; } = PolicyAdminProvider.Pcas;

    public string? PcasBaseUrl { get; set; }
    public string? PcasApiKey { get; set; }
    public string? PcasEnvironment { get; set; } = "production";
    public string? PcasLineOfBusiness { get; set; } = "ED";
    public string? PcasTransactionType { get; set; } = "NEW_BUSINESS";

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(10);
    public int MaxPollingAttempts { get; set; } = 30;

    public int MaxRetryAttempts { get; set; } = 3;

    public string BoundEventTopic { get; set; } = "policy-events";
    public string FailedEventTopic { get; set; } = "policy-events";

    public string? SqlConnectionString { get; set; }
}


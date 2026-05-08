namespace KSquare.ProposalOrchestrator.Configuration;

public class ProposalOrchestratorOptions
{
    public ProposalProvider Provider { get; set; } = ProposalProvider.GhostDraft;

    public string? ConnectionString { get; set; }

    public string? GhostDraftApiUrl { get; set; }
    public string? GhostDraftApiKey { get; set; }
    public string? GhostDraftEnvironment { get; set; } = "production";

    public IDictionary<string, string> TemplateIdMap { get; set; } = new Dictionary<string, string>
    {
        ["NBI"] = "ue-nbi-template-v3",
        ["QuoteProposal"] = "ue-quote-proposal-v2",
        ["Binder"] = "ue-binder-v2"
    };

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxPollingAttempts { get; set; } = 60;

    public string OutputBlobContainer { get; set; } = "generated-proposals";
    public string OutputPathTemplate { get; set; } = "proposals/{year}/{month}/{quoteId}/{proposalType}-{timestamp}.pdf";
    public TimeSpan SasUrlTtl { get; set; } = TimeSpan.FromHours(24);

    public int MaxRetryAttempts { get; set; } = 3;

    public string CompletionEventTopic { get; set; } = "proposal-events";
}

public enum ProposalProvider
{
    GhostDraft,
    IText,
    Mock
}


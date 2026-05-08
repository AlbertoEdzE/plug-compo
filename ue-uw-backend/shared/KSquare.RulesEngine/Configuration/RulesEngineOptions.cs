namespace KSquare.RulesEngine.Configuration;

public sealed class RulesEngineOptions
{
    public RuleSetSource RuleSource { get; set; } = RuleSetSource.EmbeddedYaml;
    public string RulesBlobContainerName { get; set; } = "rules";
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(5);
}

public enum RuleSetSource
{
    EmbeddedYaml,
    BlobStorage
}


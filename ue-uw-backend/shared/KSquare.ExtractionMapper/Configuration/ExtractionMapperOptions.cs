namespace KSquare.ExtractionMapper.Configuration;

public class ExtractionMapperOptions
{
    public MappingRuleSource RuleSource { get; set; } = MappingRuleSource.EmbeddedYaml;
    public string? RulesBlobContainerName { get; set; } = "mapping-rules";
    public bool StrictMode { get; set; } = false;
    public TimeSpan RuleCacheTtl { get; set; } = TimeSpan.FromMinutes(10);
}

public enum MappingRuleSource
{
    EmbeddedYaml,
    BlobStorage,
    FileSystem
}


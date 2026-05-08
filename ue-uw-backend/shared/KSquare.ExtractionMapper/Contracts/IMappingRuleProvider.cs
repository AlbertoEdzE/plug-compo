using KSquare.ExtractionMapper.Models;

namespace KSquare.ExtractionMapper.Contracts;

public interface IMappingRuleProvider
{
    Task<MappingRuleSet> GetRulesAsync(string documentType, CancellationToken ct = default);
}


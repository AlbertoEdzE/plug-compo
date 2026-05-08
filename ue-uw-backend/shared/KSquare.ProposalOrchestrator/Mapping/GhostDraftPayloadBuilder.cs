using System.Globalization;
using KSquare.ProposalOrchestrator.Configuration;
using KSquare.ProposalOrchestrator.Contracts;
using KSquare.ProposalOrchestrator.Exceptions;
using KSquare.ProposalOrchestrator.Models;

namespace KSquare.ProposalOrchestrator.Mapping;

public sealed class GhostDraftPayloadBuilder(ProposalOrchestratorOptions options) : IProposalPayloadBuilder
{
    public ProposalProviderPayload Build(ProposalGenerationRequest request)
    {
        if (!options.TemplateIdMap.TryGetValue(request.ProposalType, out var templateId) || string.IsNullOrWhiteSpace(templateId))
        {
            throw new ProposalTemplateNotFoundException(request.ProposalType);
        }

        var culture = CultureInfo.GetCultureInfo("en-US");
        var generatedDate = DateTimeOffset.UtcNow.ToString("MMMM dd, yyyy", culture);

        var payload = new Dictionary<string, object?>
        {
            ["templateId"] = templateId,
            ["outputFormat"] = request.OutputFormat ?? "pdf",
            ["data"] = new Dictionary<string, object?>
            {
                ["insuredName"] = request.InstitutionName,
                ["brokerName"] = request.BrokerName,
                ["brokerEmail"] = request.BrokerEmail,
                ["effectiveDate"] = request.EffectiveDate.ToString("MMMM dd, yyyy", culture),
                ["expirationDate"] = request.ExpirationDate.ToString("MMMM dd, yyyy", culture),
                ["underwriterName"] = request.UnderwriterName ?? "Underwriting Team",
                ["quoteReference"] = request.QuoteId,
                ["specialConditions"] = request.SpecialConditions ?? "",
                ["generatedDate"] = generatedDate,
                ["coverageLines"] = request.CoverageLines.Select(c => new Dictionary<string, object?>
                {
                    ["productName"] = c.ProductName,
                    ["limit"] = c.Limit.ToString("C0", culture),
                    ["retention"] = c.Retention.ToString("C0", culture),
                    ["premium"] = c.AnnualPremium.ToString("C2", culture),
                    ["aggregate"] = c.AggregateLimit?.ToString("C0", culture) ?? "N/A",
                    ["conditions"] = c.CoverageConditions ?? ""
                }).ToList(),
                ["totalPremium"] = request.CoverageLines.Sum(c => c.AnnualPremium).ToString("C2", culture)
            }
        };

        return new ProposalProviderPayload(payload);
    }
}


using System.Text.Json;
using KSquare.RatingAdapter.Contracts;
using KSquare.RatingAdapter.Models;

namespace KSquare.RatingAdapter.Mapping;

public sealed class UeRatingEnginePricingMapper : ICoveragePricingMapper
{
    public RatingEngineInput MapToRatingInput(CoveragePricingRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["requestId"] = request.CorrelationId ?? Guid.NewGuid().ToString(),
            ["institutionType"] = MapInstitutionType(request.InstitutionType),
            ["state"] = request.State,
            ["naicsCode"] = request.NaicsCode,
            ["effectiveDate"] = request.EffectiveDate.ToString("yyyy-MM-dd"),
            ["expirationDate"] = request.ExpirationDate.ToString("yyyy-MM-dd"),
            ["riskCharacteristics"] = new
            {
                totalInsuredValue = request.TotalInsuredValue,
                totalEnrollment = request.TotalEnrollment,
                fteEmployees = request.FteEmployees,
                operatingExpenses = request.OperatingExpenses,
                locationCount = request.NumberOfLocations,
                fiveYearLossRatio = request.LossHistory.FiveYearAverageLossRatio,
                largestSingleLoss = request.LossHistory.LargestSingleLoss,
                priorClaimsCount = request.LossHistory.TotalClaimsCount
            },
            ["coverageLines"] = request.CoverageLines.Select(c => new
            {
                lineCode = c.ProductCode,
                limit = c.RequestedLimit,
                retention = c.RequestedRetention,
                aggregate = c.RequestedAggregateLimit
            }).ToList()
        };

        return new RatingEngineInput(payload);
    }

    public RatingResult MapFromRatingOutput(RatingEngineOutput output, string correlationId)
    {
        var messages = new List<RatingMessage>();
        var data = output.Response;

        if (output.HttpStatusCode != 200 || data.ContainsKey("error"))
        {
            var errorText = TryGetString(data, "error") ?? "Unknown";
            messages.Add(new RatingMessage(RatingMessageLevel.Error, "RATING_FAILED", errorText));
            return new RatingResult
            {
                SubmissionId = string.Empty,
                QuoteId = string.Empty,
                Status = RatingStatus.RatingFailed,
                PremiumLines = Array.Empty<CoverageLinePremium>(),
                TotalAnnualPremium = 0m,
                Messages = messages,
                CorrelationId = correlationId
            };
        }

        if (TryGetBoolean(data, "referral_required") == true)
        {
            messages.Add(new RatingMessage(RatingMessageLevel.Warning, "REFERRAL_REQUIRED", "Rating engine requires referral."));
            return new RatingResult
            {
                SubmissionId = string.Empty,
                QuoteId = string.Empty,
                Status = RatingStatus.Referral,
                PremiumLines = Array.Empty<CoverageLinePremium>(),
                TotalAnnualPremium = 0m,
                Messages = messages,
                CorrelationId = correlationId
            };
        }

        var premiumLines = new List<CoverageLinePremium>();

        if (TryGetArray(data, "premiumLines", out var elements))
        {
            foreach (var el in elements)
            {
                if (!TryReadObject(el, out var obj))
                {
                    messages.Add(new RatingMessage(RatingMessageLevel.Warning, "MAPPING_WARNING", "Unsupported premium line shape."));
                    continue;
                }

                var lineCode = TryGetString(obj, "lineCode") ?? string.Empty;
                var lineName = TryGetString(obj, "lineName") ?? string.Empty;
                var annualPremium = TryGetDecimal(obj, "annualPremium") ?? 0m;
                var minimumPremium = TryGetDecimal(obj, "minimumPremium");
                var surcharge = TryGetDecimal(obj, "surcharge");
                var credit = TryGetDecimal(obj, "credit");

                premiumLines.Add(new CoverageLinePremium
                {
                    ProductCode = lineCode,
                    ProductName = lineName,
                    AnnualPremium = annualPremium,
                    MinimumPremium = minimumPremium,
                    SurchargeAmount = surcharge,
                    CreditAmount = credit
                });
            }
        }
        else
        {
            messages.Add(new RatingMessage(RatingMessageLevel.Warning, "MAPPING_WARNING", "premiumLines missing from rating response."));
        }

        var total = premiumLines.Sum(l => l.AnnualPremium);

        return new RatingResult
        {
            SubmissionId = correlationId,
            QuoteId = correlationId,
            Status = RatingStatus.Rated,
            PremiumLines = premiumLines,
            TotalAnnualPremium = total,
            RatingEngineReferenceId = TryGetString(data, "ratingReferenceId"),
            RatingBasis = TryGetString(data, "ratingBasis"),
            Messages = messages,
            CorrelationId = correlationId
        };
    }

    internal static string MapInstitutionType(string internalType) => internalType switch
    {
        "K-12 Public District" => "K12_PUBLIC",
        "Higher Ed" => "HIGHER_ED",
        "Private School" => "PRIVATE_SCHOOL",
        _ => "OTHER"
    };

    private static bool? TryGetBoolean(IDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is bool b)
        {
            return b;
        }

        if (raw is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.True) return true;
            if (je.ValueKind == JsonValueKind.False) return false;
            if (je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out var parsed)) return parsed;
            return null;
        }

        if (raw is string s && bool.TryParse(s, out var parsedString))
        {
            return parsedString;
        }

        return null;
    }

    private static string? TryGetString(IDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is string s)
        {
            return s;
        }

        if (raw is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        return raw.ToString();
    }

    private static decimal? TryGetDecimal(IDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is decimal d)
        {
            return d;
        }

        if (raw is double dbl)
        {
            return (decimal)dbl;
        }

        if (raw is int i)
        {
            return i;
        }

        if (raw is long l)
        {
            return l;
        }

        if (raw is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number)
            {
                if (je.TryGetDecimal(out var jd)) return jd;
                if (je.TryGetDouble(out var jdbl)) return (decimal)jdbl;
            }

            if (je.ValueKind == JsonValueKind.String && decimal.TryParse(je.GetString(), out var parsed))
            {
                return parsed;
            }

            return null;
        }

        if (raw is string s && decimal.TryParse(s, out var parsedString))
        {
            return parsedString;
        }

        return null;
    }

    private static bool TryGetArray(IDictionary<string, object?> data, string key, out IReadOnlyList<object?> elements)
    {
        elements = Array.Empty<object?>();
        if (!data.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        if (raw is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            elements = je.EnumerateArray().Select(e => (object?)e).ToArray();
            return true;
        }

        if (raw is IEnumerable<object?> objEnumerable)
        {
            elements = objEnumerable.ToArray();
            return true;
        }

        return false;
    }

    private static bool TryReadObject(object? raw, out Dictionary<string, object?> obj)
    {
        obj = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (raw is null)
        {
            return false;
        }

        if (raw is Dictionary<string, object?> dict)
        {
            obj = new Dictionary<string, object?>(dict, StringComparer.OrdinalIgnoreCase);
            return true;
        }

        if (raw is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in je.EnumerateObject())
            {
                obj[p.Name] = p.Value;
            }
            return true;
        }

        if (raw is IEnumerable<KeyValuePair<string, object?>> kvps)
        {
            foreach (var kv in kvps)
            {
                obj[kv.Key] = kv.Value;
            }
            return obj.Count > 0;
        }

        return false;
    }
}

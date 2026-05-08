using KSquare.PolicyAdminAdapter.Configuration;
using KSquare.PolicyAdminAdapter.Contracts;
using KSquare.PolicyAdminAdapter.Models;

namespace KSquare.PolicyAdminAdapter.Providers.Pcas;

public sealed class PcasPayloadBuilder(PolicyAdminAdapterOptions options) : IPolicyAdminPayloadBuilder
{
    public PolicyAdminPayload Build(BindRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["transactionType"] = options.PcasTransactionType,
            ["lineOfBusiness"] = options.PcasLineOfBusiness,
            ["effectiveDate"] = request.EffectiveDate.ToString("yyyy-MM-dd"),
            ["expirationDate"] = request.ExpirationDate.ToString("yyyy-MM-dd"),
            ["insured"] = new
            {
                legalName = request.InstitutionLegalName,
                dba = request.InstitutionDba,
                address1 = request.InstitutionAddress.Line1,
                address2 = request.InstitutionAddress.Line2,
                city = request.InstitutionAddress.City,
                state = request.InstitutionAddress.State,
                zip = request.InstitutionAddress.Zip,
                naicsCode = request.NaicsCode
            },
            ["producer"] = new
            {
                licenseNumber = request.ProducerLicenseNumber,
                producerCode = request.ProducerCode,
                name = request.ProducerName
            },
            ["coverages"] = request.CoverageLines.Select(c => new
            {
                coverageCode = MapProductCodeToPcas(c.ProductCode),
                limit = c.Limit,
                retention = c.Retention,
                annualPremium = c.AnnualPremium,
                aggregateLimit = c.AggregateLimit,
                conditions = c.CoverageConditions ?? ""
            }).ToList(),
            ["totalAnnualPremium"] = request.TotalAnnualPremium,
            ["specialConditions"] = request.SpecialConditions ?? ""
        };

        return new PolicyAdminPayload(payload);
    }

    private static string MapProductCodeToPcas(string productCode) =>
        productCode switch
        {
            "GL" => "CGL",
            "PROP" => "CPP",
            "ELL" => "ELL",
            "SA" => "SAC",
            _ => productCode
        };
}


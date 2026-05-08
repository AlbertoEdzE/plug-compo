from __future__ import annotations

from dataclasses import dataclass
from datetime import date

from .base_synthesizer import BaseSynthesizer


@dataclass(frozen=True)
class CoverageLineSynth:
    product_code: str
    product_name: str
    requested_limit: int
    requested_retention: int
    requested_aggregate_limit: int | None


class QuoteSynthesizer(BaseSynthesizer):
    def quote_id(self) -> str:
        return f"Q-{self.faker.random_int(min=10000, max=99999)}"

    def submission_id(self) -> str:
        return f"SUB-{self.faker.random_int(min=1000, max=9999)}"

    def institution_type(self) -> str:
        return "K-12 Public District"

    def naics_code(self) -> str:
        return self.faker.random_element(elements=["611110", "611310", "923110"])

    def state(self) -> str:
        return self.faker.random_element(elements=["CA", "TX", "FL", "NY"])

    def effective_date(self) -> date:
        return date(2026, 7, 1)

    def expiration_date(self) -> date:
        return date(2027, 7, 1)

    def total_insured_value_usd(self) -> int:
        return self.faker.random_int(min=25_000_000, max=35_000_000)

    def total_enrollment(self) -> int:
        return self.faker.random_int(min=2500, max=7500)

    def fte_employees(self) -> int:
        return self.faker.random_int(min=300, max=900)

    def operating_expenses_usd(self) -> int:
        return self.faker.random_int(min=40_000_000, max=120_000_000)

    def number_of_locations(self) -> int:
        return self.faker.random_int(min=2, max=5)

    def coverage_lines(self) -> list[CoverageLineSynth]:
        return [
            CoverageLineSynth("GL", "General Liability", 1_000_000, 25_000, 2_000_000),
            CoverageLineSynth("PROP", "Property", 10_000_000, 100_000, None),
            CoverageLineSynth("ELL", "Excess Liability", 5_000_000, 25_000, 5_000_000),
            CoverageLineSynth("SA", "Student Accident", 0, 0, None),
        ]

    def loss_history(self) -> dict:
        return {
            "fiveYearAverageLossRatio": float(self.faker.random_int(min=12, max=24)) / 100.0,
            "largestSingleLoss": float(self.faker.random_int(min=50_000, max=250_000)),
            "totalClaimsCount": int(self.faker.random_int(min=0, max=5)),
            "dataYearsAvailable": 5,
        }

    def expected_premium_lines(self, tiv: int, enrollment: int, lines: list[CoverageLineSynth]) -> dict:
        premiums: dict[str, float] = {}
        for line in lines:
            if line.product_code == "GL":
                premiums[line.product_code] = float(tiv) * 0.00068
            elif line.product_code == "PROP":
                premiums[line.product_code] = float(tiv) * 0.00122
            elif line.product_code == "ELL":
                premiums[line.product_code] = float(tiv) * 0.00047
            elif line.product_code == "SA":
                premiums[line.product_code] = float(enrollment) * 12.80
            else:
                premiums[line.product_code] = 0.0
        total = sum(premiums.values())
        return {"byProductCode": premiums, "totalAnnualPremium": total}

    def payload(self) -> dict:
        quote_id = self.quote_id()
        submission_id = self.submission_id()
        institution_name = self.faker.company()
        broker_name = self.faker.name()
        broker_email = "broker@example.com"

        tiv = self.total_insured_value_usd()
        enrollment = self.total_enrollment()
        lines = self.coverage_lines()
        expected = self.expected_premium_lines(tiv, enrollment, lines)

        eff = self.effective_date()
        exp = self.expiration_date()

        return {
            "quote": {
                "quoteId": quote_id,
                "submissionId": submission_id,
                "institutionType": self.institution_type(),
                "institutionName": institution_name,
                "naicsCode": self.naics_code(),
                "state": self.state(),
                "numberOfLocations": self.number_of_locations(),
                "totalEnrollment": enrollment,
                "fteEmployees": self.fte_employees(),
                "totalInsuredValue": tiv,
                "operatingExpenses": self.operating_expenses_usd(),
                "effectiveDate": eff.isoformat(),
                "expirationDate": exp.isoformat(),
                "coverageLines": [
                    {
                        "productCode": l.product_code,
                        "productName": l.product_name,
                        "requestedLimit": l.requested_limit,
                        "requestedRetention": l.requested_retention,
                        "requestedAggregateLimit": l.requested_aggregate_limit,
                    }
                    for l in lines
                ],
                "lossHistory": self.loss_history(),
                "broker": {"name": broker_name, "email": broker_email},
                "underwriter": {"userId": "uw-1", "name": "Underwriter One"},
                "expectedPremium": expected,
                "expectedPolicyNumber": "POL-999",
                "wiremock": {
                    "ghostDraftProviderJobId": "gd-job-123",
                    "pcasTransactionId": "TXN-123",
                },
            }
        }

from __future__ import annotations

from dataclasses import dataclass
from datetime import date, timedelta

from .base_synthesizer import BaseSynthesizer


@dataclass(frozen=True)
class LossRunRow:
    year: int
    claims_count: int
    incurred_usd: int
    loss_ratio_percent: int


@dataclass(frozen=True)
class Acord125Document:
    insured_name: str
    broker_name: str
    naics_code: str
    total_insured_value_usd: int
    number_of_locations: int
    policy_effective_date: str
    coverage_lines: list[str]
    loss_run_rows: list[LossRunRow]


class DocumentSynthesizer(BaseSynthesizer):
    def acord125_document(self) -> Acord125Document:
        insured_name = self.faker.company()
        broker_name = self.faker.name()
        naics_code = self.faker.random_element(elements=["238910", "611110", "484121"])

        total_insured_value_usd = self.faker.random_int(min=25_000_000, max=35_000_000)
        number_of_locations = self.faker.random_int(min=2, max=4)

        start = date(2025, 1, 1) + timedelta(days=self.faker.random_int(min=0, max=120))
        policy_effective_date = start.strftime("%m/%d/%Y")

        coverage_lines = ["GeneralLiability", "Property", "Auto"]

        base_year = 2020
        loss_run_rows: list[LossRunRow] = []
        for i in range(5):
            year = base_year + i
            claims = self.faker.random_int(min=0, max=2)
            incurred = self.faker.random_int(min=500, max=2500)
            ratio = self.faker.random_int(min=10, max=25)
            loss_run_rows.append(LossRunRow(year, claims, incurred, ratio))

        return Acord125Document(
            insured_name=insured_name,
            broker_name=broker_name,
            naics_code=naics_code,
            total_insured_value_usd=total_insured_value_usd,
            number_of_locations=number_of_locations,
            policy_effective_date=policy_effective_date,
            coverage_lines=coverage_lines,
            loss_run_rows=loss_run_rows,
        )

    def acord125_like_text(self, doc: Acord125Document) -> str:
        lines = [
            "ACORD 125 (2016/07)",
            "COMMERCIAL INSURANCE APPLICATION",
            f"Named Insured: {doc.insured_name}",
            f"Producer: {doc.broker_name}",
            f"NAICS: {doc.naics_code}",
            f"Policy Effective Date: {doc.policy_effective_date}",
            f"Total Insured Value (TIV): ${doc.total_insured_value_usd:,}",
            f"Number Of Locations: {doc.number_of_locations}",
            "Coverage Lines: " + ", ".join(doc.coverage_lines),
            "",
            "LOSS RUN SCHEDULE",
            "Year | Claims | Incurred | Loss Ratio",
        ]
        for r in doc.loss_run_rows:
            lines.append(f"{r.year} | {r.claims_count} | ${r.incurred_usd:,} | {r.loss_ratio_percent}%")
        return "\n".join(lines)

    def payload(self) -> dict:
        doc = self.acord125_document()
        return {
            "acord125": {
                "insuredName": doc.insured_name,
                "brokerName": doc.broker_name,
                "naicsCode": doc.naics_code,
                "totalInsuredValueUsd": doc.total_insured_value_usd,
                "numberOfLocations": doc.number_of_locations,
                "policyEffectiveDate": doc.policy_effective_date,
                "coverageLines": doc.coverage_lines,
                "lossRunRows": [
                    {
                        "year": r.year,
                        "claimsCount": r.claims_count,
                        "incurredUsd": r.incurred_usd,
                        "lossRatioPercent": r.loss_ratio_percent,
                    }
                    for r in doc.loss_run_rows
                ],
                "text": self.acord125_like_text(doc),
            }
        }

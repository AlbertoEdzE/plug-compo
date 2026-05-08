from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone

from .base_synthesizer import BaseSynthesizer


@dataclass(frozen=True)
class UnmappedFieldSynth:
    canonical_field: str
    display_label: str
    expected_type: str
    description: str


class AiSynthesizer(BaseSynthesizer):
    def email_id(self) -> str:
        return f"email-{self.faker.uuid4().replace('-', '')}"

    def submission_id(self) -> str:
        return f"SUB-{self.faker.random_int(min=1000, max=9999)}"

    def received_at_iso(self) -> str:
        return datetime.now(timezone.utc).isoformat()

    def institution_name(self) -> str:
        return f"{self.faker.company()} School District"

    def broker_firm(self) -> str:
        return f"{self.faker.company()} Insurance Services"

    def state(self) -> str:
        return self.faker.random_element(elements=["CA", "TX", "FL", "NY"])

    def effective_date_text(self) -> str:
        return "09/01/2026"

    def coverage_types(self) -> list[str]:
        return ["GL", "Property", "ELL", "Student Accident"]

    def email_subject(self) -> str:
        return f"New submission — {self.institution_name()} ({self.state()})"

    def sender_email(self) -> str:
        return "broker@example.com"

    def sender_name(self) -> str:
        return self.faker.name()

    def attachment_names(self) -> list[str]:
        return ["ACORD125.pdf", "LossRuns.pdf"]

    def email_body_text(self) -> str:
        tiv = self.faker.random_int(min=25_000_000, max=35_000_000)
        enrollment = self.faker.random_int(min=2500, max=7500)
        return (
            f"Hello UW Team,\n\n"
            f"Please quote a new public K-12 school district account: {self.institution_name()}.\n"
            f"Broker: {self.broker_firm()}.\n"
            f"State: {self.state()}.\n"
            f"Effective {self.effective_date_text()}.\n"
            f"Requested coverages: {', '.join(self.coverage_types())}.\n"
            f"TIV approximately ${tiv:,}. Enrollment approx {enrollment:,}.\n\n"
            f"Thank you.\n"
        )

    def triage_expected(self) -> dict:
        return {
            "intent": "NewSubmission",
            "intent_confidence": 0.91,
            "routing_suggestion": "K12-UW-Queue",
            "urgency": "Normal",
            "urgency_signals": [],
            "summary": "Broker submitted a new K-12 account for quoting.",
            "entities": [
                {
                    "field_name": "institution_name",
                    "value": self.institution_name(),
                    "confidence": 0.88,
                    "source_text": self.institution_name(),
                },
                {
                    "field_name": "broker_firm",
                    "value": self.broker_firm(),
                    "confidence": 0.86,
                    "source_text": self.broker_firm(),
                },
                {
                    "field_name": "state",
                    "value": self.state(),
                    "confidence": 0.94,
                    "source_text": self.state(),
                },
            ],
        }

    def unmapped_fields_20(self) -> list[UnmappedFieldSynth]:
        required = [
            UnmappedFieldSynth(
                "total_enrollment",
                "Total Enrollment",
                "integer",
                "Total number of enrolled students across all grades",
            ),
            UnmappedFieldSynth("naics_code", "NAICS Code", "string", "NAICS industry classification code"),
            UnmappedFieldSynth("effective_date", "Effective Date", "date", "Policy effective date"),
            UnmappedFieldSynth("institution_name", "Institution Name", "string", "Named insured institution name"),
            UnmappedFieldSynth("broker_firm", "Broker Firm", "string", "Broker agency name"),
        ]

        filler: list[UnmappedFieldSynth] = []
        for i in range(15):
            filler.append(
                UnmappedFieldSynth(
                    canonical_field=f"custom_field_{i + 1:02d}",
                    display_label=f"Custom Field {i + 1}",
                    expected_type=self.faker.random_element(elements=["string", "integer", "decimal", "date", "boolean"]),
                    description=f"Additional underwriting field {i + 1}",
                )
            )

        return required + filler

    def prefill_expected_results(self, fields: list[UnmappedFieldSynth]) -> list[dict]:
        by_name: dict[str, dict] = {
            "total_enrollment": {
                "value": "5200",
                "confidence": 0.84,
                "source_text": "Enrollment approx 5,200",
                "reasoning": "Enrollment is explicitly stated in the email body.",
            },
            "naics_code": {
                "value": None,
                "confidence": 0.0,
                "source_text": "",
                "reasoning": "NAICS code not present in the provided text.",
            },
            "effective_date": {
                "value": self.effective_date_text(),
                "confidence": 0.92,
                "source_text": f"Effective {self.effective_date_text()}",
                "reasoning": "Effective date is explicitly stated.",
            },
            "institution_name": {
                "value": self.institution_name(),
                "confidence": 0.90,
                "source_text": self.institution_name(),
                "reasoning": "Institution name appears in the body.",
            },
            "broker_firm": {
                "value": self.broker_firm(),
                "confidence": 0.88,
                "source_text": self.broker_firm(),
                "reasoning": "Broker firm appears in the body.",
            },
        }

        results: list[dict] = []
        for f in fields:
            seed = by_name.get(
                f.canonical_field,
                {
                    "value": None,
                    "confidence": 0.0,
                    "source_text": "",
                    "reasoning": "",
                },
            )
            results.append({"canonical_field": f.canonical_field, **seed})
        return results

    def narrative_context(self) -> dict:
        submission_id = self.submission_id()
        return {
            "submissionId": submission_id,
            "submissionContext": {
                "submission_id": submission_id,
                "institution_name": self.institution_name(),
                "institution_type": "K-12 Public District",
                "state": self.state(),
                "naics_code": "611110",
                "total_insured_value": 30000000.0,
                "enrollment": 5200,
                "fte_employees": 650,
                "effective_date": "2026-09-01",
                "expiration_date": "2027-09-01",
                "coverage_lines": [
                    {"product": "GL", "limit": 1000000, "premium": 20400},
                    {"product": "Property", "limit": 10000000, "premium": 36600},
                ],
                "risk_indicators": {"CampusSafetyRating": 82, "ClaimsSeverity": 18, "PolicyComplexity": 20},
                "appetite_fit_score": 0.84,
                "appetite_classification": "In Appetite",
            },
            "lossHistory": {
                "five_year_avg_loss_ratio": 0.18,
                "largest_single_loss": 120000.0,
                "total_claims_count": 4,
                "loss_trend": "Stable",
                "loss_run_years": [
                    {"year": 2021, "incurred": 20000, "claims": 1},
                    {"year": 2022, "incurred": 35000, "claims": 1},
                    {"year": 2023, "incurred": 25000, "claims": 1},
                    {"year": 2024, "incurred": 40000, "claims": 1},
                    {"year": 2025, "incurred": 30000, "claims": 0},
                ],
            },
        }

    def referral_reason(self) -> str:
        return "High TIV and multiple locations; request senior underwriter review."

    def payload(self) -> dict:
        fields = self.unmapped_fields_20()
        return {
            "triage": {
                "request": {
                    "email_id": self.email_id(),
                    "subject": self.email_subject(),
                    "body_text": self.email_body_text(),
                    "sender_email": self.sender_email(),
                    "sender_name": self.sender_name(),
                    "received_at": self.received_at_iso(),
                    "attachment_names": self.attachment_names(),
                    "correlation_id": "canvas6",
                },
                "expected": self.triage_expected(),
            },
            "prefill": {
                "document_id": f"doc-{self.faker.uuid4().replace('-', '')}",
                "document_text": self.email_body_text(),
                "document_type": "ApplicationForm",
                "unmapped_fields": [
                    {
                        "canonical_field": f.canonical_field,
                        "display_label": f.display_label,
                        "expected_type": f.expected_type,
                        "description": f.description,
                    }
                    for f in fields
                ],
                "expected_results": self.prefill_expected_results(fields),
            },
            "narrative": self.narrative_context(),
            "agentic": {
                "submission_id": self.submission_id(),
                "draft_referral": {
                    "submission_id": self.submission_id(),
                    "referral_reason": self.referral_reason(),
                    "priority": "Normal",
                    "assigned_to_queue": "SeniorUW",
                },
            },
        }

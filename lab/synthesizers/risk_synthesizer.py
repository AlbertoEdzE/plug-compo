from __future__ import annotations

from dataclasses import dataclass

from .base_synthesizer import BaseSynthesizer


@dataclass(frozen=True)
class BindReadinessContextSynth:
    quote_status: str
    has_signed_application: bool
    premium_agreed_by_broker: bool
    compliance_check_passed: bool
    referral_approved: bool


class RiskSynthesizer(BaseSynthesizer):
    def institution_type(self) -> str:
        return "K-12 Public District"

    def form_responses(self) -> dict[str, str]:
        yes = ["Yes", "yes", "Y"]
        no = ["No", "no", "N"]

        return {
            "SecurityPersonnelOnSite": self.faker.random_element(elements=yes),
            "SurveillanceCamerasInstalled": self.faker.random_element(elements=yes),
            "EmergencyResponsePlanInPlace": self.faker.random_element(elements=yes),
            "SafetyTrainingConductedAnnually": self.faker.random_element(elements=yes),
            "IncidentReportingSystem": self.faker.random_element(elements=yes),
            "CrisisManagementTeam": self.faker.random_element(elements=yes),
            "SchoolResourceOfficer": self.faker.random_element(elements=yes),
            "RecentSecurityIncidents": "0",
            "TrespassingIncidents": "0",
            "MultiStateOperations": self.faker.random_element(elements=no),
            "InternationalExposure": self.faker.random_element(elements=no),
            "IntercollegiateFootballRevenuePercent": "0%",
        }

    def bind_readiness_complete(self) -> BindReadinessContextSynth:
        return BindReadinessContextSynth(
            quote_status="Approved",
            has_signed_application=True,
            premium_agreed_by_broker=True,
            compliance_check_passed=True,
            referral_approved=True,
        )

    def bind_readiness_incomplete(self) -> BindReadinessContextSynth:
        return BindReadinessContextSynth(
            quote_status="Approved",
            has_signed_application=False,
            premium_agreed_by_broker=True,
            compliance_check_passed=True,
            referral_approved=True,
        )

    def payload(self) -> dict:
        complete = self.bind_readiness_complete()
        incomplete = self.bind_readiness_incomplete()
        return {
            "risk": {
                "institutionType": self.institution_type(),
                "formResponses": self.form_responses(),
                "bindReadiness": {
                    "complete": {
                        "quoteStatus": complete.quote_status,
                        "hasSignedApplication": complete.has_signed_application,
                        "premiumAgreedByBroker": complete.premium_agreed_by_broker,
                        "complianceCheckPassed": complete.compliance_check_passed,
                        "referralApproved": complete.referral_approved,
                    },
                    "incomplete": {
                        "quoteStatus": incomplete.quote_status,
                        "hasSignedApplication": incomplete.has_signed_application,
                        "premiumAgreedByBroker": incomplete.premium_agreed_by_broker,
                        "complianceCheckPassed": incomplete.compliance_check_passed,
                        "referralApproved": incomplete.referral_approved,
                    },
                },
            }
        }

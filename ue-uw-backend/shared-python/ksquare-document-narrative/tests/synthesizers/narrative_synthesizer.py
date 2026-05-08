from __future__ import annotations

from faker import Faker

from contracts import (
    LossHistoryContext,
    NarrativeRequest,
    NarrativeType,
    SubmissionContext,
)


class NarrativeSynthesizer:
    def __init__(self, seed: int) -> None:
        self._faker = Faker()
        self._faker.seed_instance(seed)

    def submission_context(self) -> SubmissionContext:
        return SubmissionContext(
            submission_id=self._faker.uuid4(),
            institution_name=self._faker.company(),
            institution_type="K-12 Public District",
            state=self._faker.state_abbr(),
            naics_code=str(self._faker.random_int(min=611000, max=611999)),
            total_insured_value=float(self._faker.random_int(min=10_000_000, max=250_000_000)),
            enrollment=int(self._faker.random_int(min=500, max=50_000)),
            fte_employees=int(self._faker.random_int(min=50, max=5_000)),
            effective_date="2026-09-01",
            expiration_date="2027-09-01",
            coverage_lines=[
                {"product": "GL", "limit": 5_000_000, "premium": 42_000},
                {"product": "Property", "limit": 25_000_000, "premium": 85_000},
            ],
            risk_indicators={
                "financial_stability": "Stable",
                "crime_index": "Moderate",
                "prior_losses_flag": False,
            },
            appetite_fit_score=0.72,
            appetite_classification="Borderline",
        )

    def loss_history(self) -> LossHistoryContext:
        years = []
        base_year = 2021
        for i in range(5):
            years.append(
                {
                    "year": base_year + i,
                    "incurred": int(self._faker.random_int(min=10_000, max=250_000)),
                    "claims": int(self._faker.random_int(min=0, max=6)),
                }
            )
        return LossHistoryContext(
            five_year_avg_loss_ratio=0.18,
            largest_single_loss=250_000.0,
            total_claims_count=sum(int(y["claims"]) for y in years),
            loss_trend="Stable",
            loss_run_years=years,
        )

    def request(
        self,
        narrative_type: NarrativeType = NarrativeType.RISK_SUMMARY,
        with_loss_history: bool = True,
    ) -> NarrativeRequest:
        sc = self.submission_context()
        return NarrativeRequest(
            submission_id=sc.submission_id,
            narrative_type=narrative_type,
            submission_context=sc,
            loss_history=self.loss_history() if with_loss_history else None,
            underwriter_name=self._faker.name(),
            additional_notes=self._faker.sentence(nb_words=10),
            correlation_id=self._faker.uuid4(),
        )

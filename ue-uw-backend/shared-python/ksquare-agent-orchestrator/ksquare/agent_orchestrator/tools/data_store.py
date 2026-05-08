from __future__ import annotations

import hashlib


class SynthesizedSubmissionStore:
    def submission_summary(self, submission_id: str) -> dict:
        seed = self._seed(submission_id)
        return {
            "submission_id": submission_id,
            "submission_number": f"SUB-{seed:04d}",
            "institution_name": f"Institution {seed}",
            "status": "InReview",
            "broker_name": "Broker 1",
            "effective_date": "2026-01-01",
            "underwriter": "UW-1",
        }

    def coverage_summary(self, submission_id: str) -> dict:
        seed = self._seed(submission_id)
        return {
            "submission_id": submission_id,
            "coverage_lines": [
                {"line": "GL", "limit": 1000000 + seed, "retention": 10000, "premium": 25000},
                {"line": "Auto", "limit": 2000000 + seed, "retention": 25000, "premium": 12000},
            ],
        }

    def loss_history(self, submission_id: str, years: int = 5) -> dict:
        seed = self._seed(submission_id)
        rows = []
        for i in range(years):
            year = 2025 - i
            claims = (seed + i) % 7
            incurred = (seed * 1000) + (i * 5000)
            premium = 100000 + (seed * 10)
            loss_ratio = round(incurred / max(premium, 1), 4)
            rows.append(
                {
                    "year": year,
                    "claims_count": claims,
                    "incurred": incurred,
                    "premium": premium,
                    "loss_ratio": loss_ratio,
                }
            )
        return {"submission_id": submission_id, "years": years, "history": rows}

    def risk_indicators(self, submission_id: str) -> dict:
        seed = self._seed(submission_id)
        return {
            "submission_id": submission_id,
            "campus_safety": (seed * 7) % 100,
            "claims_severity": (seed * 11) % 100,
            "policy_complexity": (seed * 13) % 100,
            "litigation_exposure": (seed * 17) % 100,
            "appetite_fit": (seed * 19) % 100,
        }

    def checklist_status(self, submission_id: str) -> dict:
        seed = self._seed(submission_id)
        missing = []
        if seed % 2 == 0:
            missing.append("LossRun")
        if seed % 3 == 0:
            missing.append("Financial")
        return {
            "submission_id": submission_id,
            "received": ["ACORD125"],
            "missing": missing,
        }

    @staticmethod
    def _seed(submission_id: str) -> int:
        return int(hashlib.md5(submission_id.encode("utf-8")).hexdigest(), 16) % 10_000


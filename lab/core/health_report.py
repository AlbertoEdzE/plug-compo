from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone


@dataclass(frozen=True)
class AssertionRecord:
    name: str
    passed: bool
    details: str = ""


@dataclass
class HealthReport:
    scenario_name: str
    canvas: str
    timestamp_utc: str
    components_exercised: list[str] = field(default_factory=list)
    assertions_passed: list[AssertionRecord] = field(default_factory=list)
    assertions_failed: list[AssertionRecord] = field(default_factory=list)
    status: str = "SKIP"

    @staticmethod
    def now_iso() -> str:
        return datetime.now(timezone.utc).isoformat()

    def to_dict(self) -> dict:
        return {
            "scenario": self.scenario_name,
            "canvas": self.canvas,
            "timestampUtc": self.timestamp_utc,
            "components": list(self.components_exercised),
            "assertionsPassed": [asdict(a) for a in self.assertions_passed],
            "assertionsFailed": [asdict(a) for a in self.assertions_failed],
            "status": self.status,
        }

    def write_json(self, path: str) -> None:
        payload = self.to_dict()
        with open(path, "w", encoding="utf-8") as f:
            json.dump(payload, f, indent=2)

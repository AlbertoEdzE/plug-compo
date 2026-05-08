from __future__ import annotations

from dataclasses import dataclass
from typing import Callable, Coroutine, Optional

from .health_report import AssertionRecord, HealthReport


@dataclass(frozen=True)
class ScenarioMetadata:
    scenario_name: str
    canvas: str
    components_exercised: list[str]


class ScenarioRunner:
    def __init__(self, metadata: ScenarioMetadata) -> None:
        self._meta = metadata
        self._passed: list[AssertionRecord] = []
        self._failed: list[AssertionRecord] = []

    async def setup(self) -> None:
        return

    async def run(self) -> None:
        raise NotImplementedError

    async def teardown(self) -> None:
        return

    def assert_true(self, name: str, predicate: bool, details: str = "") -> None:
        if predicate:
            self._passed.append(AssertionRecord(name=name, passed=True, details=details))
        else:
            self._failed.append(AssertionRecord(name=name, passed=False, details=details))

    def report(self) -> HealthReport:
        status = "PASS" if not self._failed else "FAIL"
        return HealthReport(
            scenario_name=self._meta.scenario_name,
            canvas=self._meta.canvas,
            timestamp_utc=HealthReport.now_iso(),
            components_exercised=list(self._meta.components_exercised),
            assertions_passed=list(self._passed),
            assertions_failed=list(self._failed),
            status=status,
        )

from __future__ import annotations

import argparse
import importlib.util
import os
import sys
from pathlib import Path

from core.health_report import AssertionRecord, HealthReport
from core.scenario_runner import ScenarioMetadata, ScenarioRunner
from core.snapshot import create_snapshot_tag


def _scenario_path_for_phase(phase: str) -> Path | None:
    mapping = {
        "1": "canvas_1_infrastructure.py",
        "2": "canvas_2_communication.py",
        "3": "canvas_3_document_intelligence.py",
        "4": "canvas_4_ai_agent.py",
        "5": "canvas_5_quote_bind.py",
        "6": "canvas_6_ai_intelligence.py",
        "full": "canvas_full_system.py",
    }
    file_name = mapping.get(phase)
    if not file_name:
        return None
    return Path(__file__).resolve().parent / "scenarios" / file_name


def _default_report_path(report_dir: Path, phase: str) -> Path:
    if phase == "full":
        return report_dir / "canvas-full-report.json"
    return report_dir / f"canvas-{phase}-report.json"


def _write_skip_report(report_path: Path, phase: str, reason: str) -> None:
    report = HealthReport(
        scenario_name=f"canvas_{phase}",
        canvas=str(phase),
        timestamp_utc=HealthReport.now_iso(),
        components_exercised=[],
        assertions_passed=[],
        assertions_failed=[],
        status="SKIP",
    )
    report.assertions_failed.append(AssertionRecord(name="scenario_available", passed=False, details=reason))
    report.write_json(str(report_path))


async def _run_scenario(scenario: ScenarioRunner) -> HealthReport:
    try:
        await scenario.setup()
        await scenario.run()
    finally:
        await scenario.teardown()
    return scenario.report()


def _load_scenario_runner(path: Path) -> ScenarioRunner | None:
    if not path.exists():
        return None

    spec = importlib.util.spec_from_file_location(path.stem, str(path))
    if spec is None or spec.loader is None:
        return None
    module = importlib.util.module_from_spec(spec)
    sys.modules[path.stem] = module
    spec.loader.exec_module(module)

    factory = getattr(module, "create_scenario", None)
    if factory is None:
        return None
    return factory()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--phase", required=True, choices=["1", "2", "3", "4", "5", "6", "full"])
    parser.add_argument("--no-tag", action="store_true")
    parser.add_argument("--report-dir", default=str(Path(__file__).resolve().parent / "reports"))
    args = parser.parse_args(argv)

    report_dir = Path(args.report_dir)
    report_dir.mkdir(parents=True, exist_ok=True)
    report_path = _default_report_path(report_dir, args.phase)

    scenario_path = _scenario_path_for_phase(args.phase)
    if scenario_path is None:
        _write_skip_report(report_path, args.phase, "Unknown phase.")
        return 0

    scenario = _load_scenario_runner(scenario_path)
    if scenario is None:
        _write_skip_report(report_path, args.phase, f"Scenario file not found or invalid: {scenario_path.name}")
        return 0

    import asyncio

    report = asyncio.run(_run_scenario(scenario))
    report.write_json(str(report_path))

    if report.status == "PASS" and not args.no_tag:
        tag = "canvas-full-stable" if args.phase == "full" else f"canvas-{args.phase}-stable"
        create_snapshot_tag(str(report_path), tag)

    return 0 if report.status in ("PASS", "SKIP") else 1


if __name__ == "__main__":
    raise SystemExit(main())

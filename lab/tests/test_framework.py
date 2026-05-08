from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest


def _add_lab_to_path() -> Path:
    lab_dir = Path(__file__).resolve().parents[1]
    sys.path.insert(0, str(lab_dir))
    return lab_dir


def test_run_canvas_phase_1_without_scenario_file_exits_with_skip_report(tmp_path) -> None:
    _add_lab_to_path()
    import run_canvas

    exit_code = run_canvas.main(["--phase", "1", "--no-tag", "--report-dir", str(tmp_path)])
    assert exit_code == 0

    report_path = tmp_path / "canvas-1-report.json"
    assert report_path.exists()
    payload = json.loads(report_path.read_text(encoding="utf-8"))
    assert payload["status"] == "SKIP"


def test_snapshot_refuses_non_pass_report(tmp_path) -> None:
    _add_lab_to_path()
    from core.snapshot import create_snapshot_tag

    report = {"status": "FAIL"}
    report_path = tmp_path / "canvas-1-report.json"
    report_path.write_text(json.dumps(report), encoding="utf-8")

    with pytest.raises(RuntimeError):
        create_snapshot_tag(str(report_path), "canvas-1-stable")

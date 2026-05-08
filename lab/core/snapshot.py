from __future__ import annotations

import json
import subprocess
from dataclasses import dataclass


@dataclass(frozen=True)
class SnapshotResult:
    created: bool
    tag: str


def create_snapshot_tag(report_path: str, tag: str) -> SnapshotResult:
    with open(report_path, "r", encoding="utf-8") as f:
        report = json.load(f)

    status = str(report.get("status", "")).upper()
    if status != "PASS":
        raise RuntimeError(f"Cannot create snapshot tag; report status is {status}.")

    subprocess.run(["git", "tag", tag], check=True)
    subprocess.run(["git", "push", "origin", tag], check=True)
    return SnapshotResult(created=True, tag=tag)

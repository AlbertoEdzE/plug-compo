from __future__ import annotations

import json
import socket
import subprocess
from dataclasses import dataclass
from pathlib import Path
from time import monotonic, sleep

from core.scenario_runner import ScenarioMetadata, ScenarioRunner


@dataclass(frozen=True)
class HarnessResult:
    assertions: list[dict]


class Canvas1InfrastructureScenario(ScenarioRunner):
    def __init__(self) -> None:
        super().__init__(
            ScenarioMetadata(
                scenario_name="canvas_1_infrastructure",
                canvas="1",
                components_exercised=[
                    "KSquare.Correlation",
                    "KSquare.PiiRedaction",
                    "KSquare.Idempotency",
                    "KSquare.BlobStorage",
                    "KSquare.EventBus",
                    "KSquare.AuditTrail",
                ],
            )
        )

    async def setup(self) -> None:
        compose = Path(__file__).resolve().parents[1] / "docker-compose.lab.yml"
        if not compose.exists():
            return

        _try_run(["docker", "compose", "-f", str(compose), "up", "-d"])

    async def run(self) -> None:
        infra_ok, infra_details = _wait_for_infra(timeout_seconds=60)
        if not infra_ok:
            self.assert_true("infra_ready", False, infra_details)
            return

        harness_proj = Path(__file__).resolve().parent / "canvas_1_infrastructure_harness" / "Canvas1.csproj"
        if not harness_proj.exists():
            self.assert_true("harness_exists", False, "Harness project missing.")
            return

        proc = subprocess.run(
            ["dotnet", "run", "-c", "Release", "--project", str(harness_proj)],
            capture_output=True,
            text=True,
        )

        stdout = (proc.stdout or "").strip()
        if not stdout:
            self.assert_true("harness_output", False, (proc.stderr or "").strip())
            return

        try:
            payload = json.loads(stdout)
        except Exception as ex:
            self.assert_true("harness_output", False, f"Failed to parse harness JSON: {ex}")
            return

        assertions = payload.get("assertions", []) or []
        expected = [
            "synthesize_ids",
            "correlation_propagates",
            "synthesize_pii_payload",
            "pii_redaction_before_audit",
            "eventbus_single_delivery",
            "idempotency_blocks_duplicate",
            "azurite_blob_roundtrip",
            "audit_append_only_two_writes",
        ]

        by_name = {a.get("name"): a for a in assertions if isinstance(a, dict)}
        for name in expected:
            item = by_name.get(name)
            if item is None:
                self.assert_true(name, False, "Missing assertion from harness output.")
                continue
            self.assert_true(name, bool(item.get("passed")), str(item.get("details", "") or ""))


def _try_run(cmd: list[str]) -> None:
    try:
        subprocess.run(cmd, check=False, capture_output=True, text=True)
    except Exception:
        return


def _wait_for_infra(timeout_seconds: int) -> tuple[bool, str]:
    started = monotonic()
    targets = [
        ("sqlserver", "127.0.0.1", 1433),
        ("redis", "127.0.0.1", 6379),
        ("azurite", "127.0.0.1", 10000),
    ]

    last_error = ""
    while monotonic() - started < timeout_seconds:
        all_ok = True
        for name, host, port in targets:
            ok, err = _can_connect(host, port)
            if not ok:
                all_ok = False
                last_error = f"{name} not reachable on {host}:{port} ({err})"
                break
        if all_ok:
            return True, ""
        sleep(1.0)

    return False, f"Infrastructure not ready after {timeout_seconds}s. {last_error}. Ensure Docker is running and 'docker compose -f lab/docker-compose.lab.yml up -d' succeeds."


def _can_connect(host: str, port: int) -> tuple[bool, str]:
    try:
        with socket.create_connection((host, port), timeout=1.0):
            return True, ""
    except Exception as ex:
        return False, str(ex)


def create_scenario() -> ScenarioRunner:
    return Canvas1InfrastructureScenario()

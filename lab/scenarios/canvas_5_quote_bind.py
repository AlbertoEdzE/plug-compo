from __future__ import annotations

import json
import os
import socket
import subprocess
from pathlib import Path
from time import monotonic, sleep

from core.scenario_runner import ScenarioMetadata, ScenarioRunner
from synthesizers.quote_synthesizer import QuoteSynthesizer


class Canvas5QuoteBindScenario(ScenarioRunner):
    def __init__(self) -> None:
        super().__init__(
            ScenarioMetadata(
                scenario_name="canvas_5_quote_bind",
                canvas="5",
                components_exercised=[
                    "KSquare.RatingAdapter",
                    "KSquare.ProposalOrchestrator",
                    "KSquare.PolicyAdminAdapter",
                    "KSquare.StateMachine",
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
            for name in CANVAS_1_ASSERTIONS + CANVAS_2_ASSERTIONS + CANVAS_3_ASSERTIONS + CANVAS_4_ASSERTIONS + CANVAS_5_ASSERTIONS:
                self.assert_true(name, False, infra_details)
            return

        seed = int((os.environ.get("CANVAS_SEED") or "42").strip() or "42")
        payload = QuoteSynthesizer(seed=seed).payload()

        env = dict(os.environ)
        env["CANVAS_SEED"] = str(seed)
        env["CANVAS5_SYNTH_JSON"] = json.dumps(payload)

        canvas1 = _run_harness("canvas_1_infrastructure_harness/Canvas1.csproj", env=env)
        if canvas1 is None:
            for name in CANVAS_1_ASSERTIONS:
                self.assert_true(name, False, "Canvas 1 harness failed to run or produced invalid JSON.")
        else:
            _emit_assertions(self, canvas1, CANVAS_1_ASSERTIONS)

        canvas2 = _run_harness("canvas_2_communication_harness/Canvas2.csproj", env=env)
        if canvas2 is None:
            for name in CANVAS_2_ASSERTIONS:
                self.assert_true(name, False, "Canvas 2 harness failed to run or produced invalid JSON.")
            return
        _emit_assertions(self, canvas2, CANVAS_2_ASSERTIONS)

        canvas3 = _run_harness("canvas_3_document_intelligence_harness/Canvas3.csproj", env=env)
        if canvas3 is None:
            for name in CANVAS_3_ASSERTIONS:
                self.assert_true(name, False, "Canvas 3 harness failed to run or produced invalid JSON.")
            return
        _emit_assertions(self, canvas3, CANVAS_3_ASSERTIONS)

        canvas4 = _run_harness("canvas_4_ai_agent_harness/Canvas4.csproj", env=env)
        if canvas4 is None:
            for name in CANVAS_4_ASSERTIONS:
                self.assert_true(name, False, "Canvas 4 harness failed to run or produced invalid JSON.")
            return
        _emit_assertions(self, canvas4, CANVAS_4_ASSERTIONS)

        canvas5 = _run_harness("canvas_5_quote_bind_harness/Canvas5.csproj", env=env)
        if canvas5 is None:
            for name in CANVAS_5_ASSERTIONS:
                self.assert_true(name, False, "Canvas 5 harness failed to run or produced invalid JSON.")
            return
        _emit_assertions(self, canvas5, CANVAS_5_ASSERTIONS)


def _run_harness(relative_csproj: str, env: dict[str, str]) -> dict | None:
    harness_proj = Path(__file__).resolve().parent / relative_csproj
    if not harness_proj.exists():
        return None

    proc = subprocess.run(
        ["dotnet", "run", "-c", "Release", "--project", str(harness_proj)],
        capture_output=True,
        text=True,
        env=env,
    )
    stdout = (proc.stdout or "").strip()
    if not stdout:
        return None
    try:
        return json.loads(stdout)
    except Exception:
        return None


def _emit_assertions(scenario: ScenarioRunner, payload: dict, expected: list[str]) -> None:
    assertions = payload.get("assertions", []) or []
    by_name = {a.get("name"): a for a in assertions if isinstance(a, dict)}
    for name in expected:
        item = by_name.get(name)
        if item is None:
            scenario.assert_true(name, False, "Missing assertion from harness output.")
            continue
        scenario.assert_true(name, bool(item.get("passed")), str(item.get("details", "") or ""))


def _wait_for_infra(timeout_seconds: int) -> tuple[bool, str]:
    started = monotonic()
    targets = [
        ("sqlserver", "127.0.0.1", 1433),
        ("redis", "127.0.0.1", 6379),
        ("azurite", "127.0.0.1", 10000),
        ("wiremock", "127.0.0.1", 8080),
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

    return False, f"Infrastructure not ready after {timeout_seconds}s. {last_error}."


def _can_connect(host: str, port: int) -> tuple[bool, str]:
    try:
        with socket.create_connection((host, port), timeout=1.0):
            return True, ""
    except Exception as ex:
        return False, str(ex)


def _try_run(cmd: list[str]) -> None:
    try:
        subprocess.run(cmd, check=False, capture_output=True, text=True)
    except Exception:
        return


CANVAS_1_ASSERTIONS = [
    "synthesize_ids",
    "correlation_propagates",
    "synthesize_pii_payload",
    "pii_redaction_before_audit",
    "eventbus_single_delivery",
    "idempotency_blocks_duplicate",
    "azurite_blob_roundtrip",
    "audit_append_only_two_writes",
]

CANVAS_2_ASSERTIONS = [
    "synthesize_canvas2_ids",
    "wiremock_stub_graph_and_sendgrid",
    "email_ingestion_publishes_one_event_and_stores_two_attachments",
    "email_send_template_renders_variable_and_hits_wiremock_sendgrid",
    "notification_dispatch_persists_in_sql_and_dedups_to_one_row",
]

CANVAS_3_ASSERTIONS = [
    "synthesize_canvas3_inputs",
    "wiremock_stub_document_intelligence",
    "document_extraction_confidence_routing_autoaccepted",
    "document_classification_acord125",
    "extraction_mapper_maps_required_fields_and_collects_unmapped",
    "rules_intake_routing_routes_to_senior_underwriter",
    "risk_analysis_composite_score_matches_hand_calculated",
    "risk_analysis_appetite_fit_classification",
    "rules_bind_readiness_ready_and_not_ready",
    "form_templates_itext_renders_non_empty_pdf",
]

CANVAS_4_ASSERTIONS = [
    "synthesize_canvas4_inputs",
    "wiremock_stub_agent_and_llm_endpoints",
    "sse_event_sequence_matches_spec",
    "sql_conversation_audit_redacts_pii",
    "sql_llm_cost_daily_written",
    "prompt_injection_blocked_no_llm_call",
]

CANVAS_5_ASSERTIONS = [
    "synthesize_canvas5_inputs",
    "wiremock_stub_rating_proposal_pcas",
    "rating_mock_deterministic_premium",
    "quote_fsm_transitions_six_events_in_order",
    "proposal_generation_stores_pdf_and_publishes_event",
    "policy_bind_publishes_policy_bound_event_and_audit",
    "invalid_transition_no_audit_no_event",
    "concurrent_bind_one_bound_one_concurrency_exception",
]


def create_scenario() -> ScenarioRunner:
    return Canvas5QuoteBindScenario()

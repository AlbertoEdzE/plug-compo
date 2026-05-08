from __future__ import annotations

import json
import os
import socket
import subprocess
import tempfile
from pathlib import Path
from time import monotonic, sleep

import httpx

from core.scenario_runner import ScenarioMetadata, ScenarioRunner
from synthesizers.ai_synthesizer import AiSynthesizer


class Canvas6AiIntelligenceScenario(ScenarioRunner):
    def __init__(self) -> None:
        super().__init__(
            ScenarioMetadata(
                scenario_name="canvas_6_ai_intelligence",
                canvas="6",
                components_exercised=[
                    "KSquare.AiEmailTriage",
                    "KSquare.IntelligentPrefill",
                    "KSquare.DocumentNarrative",
                    "KSquare.AgenticActions",
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
            for name in (
                CANVAS_1_ASSERTIONS
                + CANVAS_2_ASSERTIONS
                + CANVAS_3_ASSERTIONS
                + CANVAS_4_ASSERTIONS
                + CANVAS_5_ASSERTIONS
                + CANVAS_6_ASSERTIONS
            ):
                self.assert_true(name, False, infra_details)
            return

        seed = int((os.environ.get("CANVAS_SEED") or "42").strip() or "42")
        synth = AiSynthesizer(seed=seed).payload()

        self.assert_true(
            "synthesize_canvas6_inputs",
            bool(synth.get("triage")) and bool(synth.get("prefill")) and bool(synth.get("narrative")) and bool(synth.get("agentic")),
            f"seed={seed}",
        )

        env = dict(os.environ)
        env["CANVAS_SEED"] = str(seed)
        env["CANVAS6_SYNTH_JSON"] = json.dumps(synth)

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

        wiremock = os.environ.get("LAB_WIREMOCK") or "http://localhost:8080"
        ok, details = await _wiremock_stub_openai(wiremock, synth)
        self.assert_true("wiremock_stub_openai_for_ai_components", ok, details)
        if not ok:
            for name in [n for n in CANVAS_6_ASSERTIONS if n != "wiremock_stub_openai_for_ai_components"]:
                self.assert_true(name, False, "WireMock stubbing failed.")
            return

        triage_out = _python_call(
            package_dir=_repo_root() / "ue-uw-backend" / "shared-python" / "ksquare-ai-email-triage",
            env=env,
            code=_TRIAGE_SNIPPET,
        )
        self.assert_true(
            "triage_intent_and_entities",
            triage_out.get("intent") == synth["triage"]["expected"]["intent"]
            and any(e.get("field_name") == "institution_name" for e in triage_out.get("extracted_entities", [])),
            json.dumps({"intent": triage_out.get("intent"), "entities": triage_out.get("extracted_entities", [])})[:500],
        )
        self.assert_true(
            "triage_routing_suggestion_matches_k12",
            triage_out.get("routing_suggestion") == synth["triage"]["expected"]["routing_suggestion"],
            f"routing_suggestion={triage_out.get('routing_suggestion')}",
        )

        _ = await _wiremock_clear_requests(wiremock)
        prefill_out = _python_call(
            package_dir=_repo_root() / "ue-uw-backend" / "shared-python" / "ksquare-intelligent-prefill",
            env=env,
            code=_PREFILL_SNIPPET,
        )

        count_prefill_calls = await _wiremock_count_requests(wiremock, "/openai/deployments/gpt-4o/chat/completions")
        self.assert_true(
            "prefill_batching_20_fields_to_2_llm_calls",
            int(count_prefill_calls) == 2 and prefill_out.get("total_fields_requested") == 20,
            f"llm_calls={count_prefill_calls} total_fields_requested={prefill_out.get('total_fields_requested')}",
        )

        narrative_out = _python_call(
            package_dir=_repo_root() / "ue-uw-backend" / "shared-python" / "ksquare-document-narrative",
            env=env,
            code=_NARRATIVE_SNIPPET,
        )
        referral_sections = narrative_out.get("referral_memo", {}).get("sections", {}) or {}
        self.assert_true(
            "referral_narrative_sections_ge_4",
            isinstance(referral_sections, dict) and len(referral_sections.keys()) >= 4,
            f"section_keys={list(referral_sections.keys())}",
        )

        agentic_db = tempfile.NamedTemporaryFile(prefix="canvas6-drafts-", suffix=".db", delete=False)
        agentic_db.close()
        env2 = dict(env)
        env2["CANVAS6_DRAFT_DB_URL"] = f"sqlite+aiosqlite:///{agentic_db.name}"

        agentic_out = _python_call(
            package_dir=_repo_root() / "ue-uw-backend" / "shared-python" / "ksquare-agentic-actions",
            env=env2,
            code=_AGENTIC_SNIPPET,
        )
        try:
            os.unlink(agentic_db.name)
        except Exception:
            pass
        self.assert_true(
            "draft_execute_publishes_one_event_and_is_idempotent",
            bool(agentic_out.get("first_success"))
            and bool(agentic_out.get("second_success"))
            and int(agentic_out.get("published_events", 0)) == 1
            and bool(agentic_out.get("idempotent_same_result")),
            json.dumps(agentic_out)[:500],
        )
        self.assert_true(
            "draft_expired_exception_no_event",
            bool(agentic_out.get("expired_raised")) and int(agentic_out.get("published_events_after_expired", 0)) == 1,
            json.dumps(agentic_out)[:500],
        )


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def _python_call(*, package_dir: Path, env: dict[str, str], code: str) -> dict:
    pythonpath = str(package_dir)
    merged_env = dict(env)
    merged_env["PYTHONPATH"] = pythonpath
    proc = subprocess.run(["python", "-c", code], capture_output=True, text=True, env=merged_env)
    out = (proc.stdout or "").strip()
    if proc.returncode != 0 or not out:
        raise RuntimeError((proc.stderr or "python snippet failed").strip()[:800])
    return json.loads(out)


async def _wiremock_stub_openai(wiremock_base_url: str, synth: dict) -> tuple[bool, str]:
    triage_expected = synth["triage"]["expected"]
    prefill_expected = synth["prefill"]["expected_results"]

    referral_text = (
        "1. EXECUTIVE SUMMARY:\n"
        "This is a referral memo summary.\n\n"
        "2. KEY RISK FACTORS:\n"
        "Key risk factors summarized.\n\n"
        "3. LOSS HISTORY:\n"
        "Loss history summarized.\n\n"
        "4. ROUTING / NEXT STEPS:\n"
        "Route to SeniorUW for review.\n"
    )

    async with httpx.AsyncClient(base_url=wiremock_base_url, timeout=10.0) as http:
        try:
            await http.post("/__admin/reset")
            await http.delete("/__admin/requests")

            await http.post(
                "/__admin/mappings",
                json={
                    "request": {"method": "POST", "urlPath": "/openai/deployments/gpt-4o-mini/chat/completions"},
                    "response": {
                        "status": 200,
                        "headers": {"Content-Type": "application/json"},
                        "jsonBody": {
                            "id": "chatcmpl-triage",
                            "model": "gpt-4o-mini",
                            "choices": [
                                {
                                    "index": 0,
                                    "message": {"role": "assistant", "content": json.dumps(triage_expected)},
                                }
                            ],
                            "usage": {"prompt_tokens": 10, "completion_tokens": 20, "total_tokens": 30},
                        },
                    },
                },
            )

            await http.post(
                "/__admin/mappings",
                json={
                    "request": {"method": "POST", "urlPath": "/openai/deployments/gpt-4o/chat/completions"},
                    "response": {
                        "status": 200,
                        "headers": {"Content-Type": "application/json"},
                        "jsonBody": {
                            "id": "chatcmpl-prefill",
                            "model": "gpt-4o",
                            "choices": [
                                {
                                    "index": 0,
                                    "message": {
                                        "role": "assistant",
                                        "content": json.dumps({"results": prefill_expected}),
                                    },
                                }
                            ],
                            "usage": {"prompt_tokens": 50, "completion_tokens": 100, "total_tokens": 150},
                        },
                    },
                },
            )

            await http.post(
                "/__admin/mappings",
                json={
                    "request": {"method": "POST", "urlPath": "/openai/deployments/gpt-4o-narrative/chat/completions"},
                    "response": {
                        "status": 200,
                        "headers": {"Content-Type": "application/json"},
                        "jsonBody": {
                            "id": "chatcmpl-narrative",
                            "model": "gpt-4o-narrative",
                            "choices": [{"index": 0, "message": {"role": "assistant", "content": referral_text}}],
                            "usage": {"prompt_tokens": 80, "completion_tokens": 140, "total_tokens": 220},
                        },
                    },
                },
            )

            return True, ""
        except Exception as ex:
            return False, str(ex)


async def _wiremock_clear_requests(wiremock_base_url: str) -> bool:
    async with httpx.AsyncClient(base_url=wiremock_base_url, timeout=10.0) as http:
        try:
            await http.delete("/__admin/requests")
            return True
        except Exception:
            return False


async def _wiremock_count_requests(wiremock_base_url: str, url_path: str) -> int:
    async with httpx.AsyncClient(base_url=wiremock_base_url, timeout=10.0) as http:
        resp = await http.get("/__admin/requests")
        resp.raise_for_status()
        data = resp.json()
        requests = data.get("requests", []) or []
        return sum(
            1
            for r in requests
            if ((r.get("request", {}) or {}).get("url") or "").startswith(url_path)
        )


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


_TRIAGE_SNIPPET = r"""
import asyncio
import json
from dataclasses import asdict

from options import AiEmailTriageOptions
from providers.azure_openai_triage import AzureOpenAiEmailTriageAdapter
from contracts import EmailTriageRequest


async def main() -> None:
    synth = json.loads(__import__("os").environ["CANVAS6_SYNTH_JSON"])
    req = synth["triage"]["request"]
    options = AiEmailTriageOptions(
        provider="AzureOpenAi",
        azure_openai_endpoint=__import__("os").environ.get("LAB_WIREMOCK") or "http://localhost:8080",
        deployment_name="gpt-4o-mini",
        prompt_version="v1",
        max_body_chars=2000,
        temperature=0.0,
    )
    adapter = AzureOpenAiEmailTriageAdapter(options, azure_ad_token_provider=lambda: "test-token")
    result = await adapter.triage_async(
        EmailTriageRequest(
            email_id=req["email_id"],
            subject=req["subject"],
            body_text=req["body_text"],
            sender_email=req["sender_email"],
            sender_name=req["sender_name"],
            received_at=req["received_at"],
            attachment_names=req["attachment_names"],
            correlation_id=req.get("correlation_id"),
        )
    )
    print(json.dumps(asdict(result)))


asyncio.run(main())
"""

_PREFILL_SNIPPET = r"""
import asyncio
import json
from dataclasses import asdict

from options import IntelligentPrefillOptions
from providers.azure_openai_prefill import AzureOpenAiPrefillAdapter
from contracts import PrefillRequest, UnmappedField


async def main() -> None:
    synth = json.loads(__import__("os").environ["CANVAS6_SYNTH_JSON"])
    req = synth["prefill"]
    options = IntelligentPrefillOptions(
        provider="AzureOpenAi",
        azure_openai_endpoint=__import__("os").environ.get("LAB_WIREMOCK") or "http://localhost:8080",
        deployment_name="gpt-4o",
        prompt_version="v1",
        max_document_chars=8000,
        review_confidence_threshold=0.75,
        fields_per_batch=15,
    )
    adapter = AzureOpenAiPrefillAdapter(options, azure_ad_token_provider=lambda: "test-token")
    result = await adapter.prefill_async(
        PrefillRequest(
            document_id=req["document_id"],
            document_text=req["document_text"],
            document_type=req["document_type"],
            unmapped_fields=[UnmappedField(**f) for f in req["unmapped_fields"]],
            correlation_id="canvas6",
        )
    )
    print(json.dumps(asdict(result)))


asyncio.run(main())
"""

_NARRATIVE_SNIPPET = r"""
import asyncio
import json
from dataclasses import asdict

from options import DocumentNarrativeOptions
from providers.azure_openai_narrative import AzureOpenAiNarrativeAdapter
from contracts import NarrativeRequest, NarrativeType, SubmissionContext, LossHistoryContext


async def main() -> None:
    synth = json.loads(__import__("os").environ["CANVAS6_SYNTH_JSON"])
    payload = synth["narrative"]
    options = DocumentNarrativeOptions(
        provider="AzureOpenAi",
        azure_openai_endpoint=__import__("os").environ.get("LAB_WIREMOCK") or "http://localhost:8080",
        deployment_name="gpt-4o-narrative",
        prompt_version="v1",
        temperature=0.0,
    )
    adapter = AzureOpenAiNarrativeAdapter(options, azure_ad_token_provider=lambda: "test-token")

    ctx = SubmissionContext(**payload["submissionContext"])
    loss = LossHistoryContext(**payload["lossHistory"])

    async def gen(nt: NarrativeType):
        res = await adapter.generate_narrative_async(
            NarrativeRequest(
                submission_id=payload["submissionId"],
                narrative_type=nt,
                submission_context=ctx,
                loss_history=loss,
                underwriter_name="Underwriter One",
                additional_notes=None,
                correlation_id="canvas6",
            )
        )
        return asdict(res)

    out = {
        "risk_summary": await gen(NarrativeType.RISK_SUMMARY),
        "loss_run": await gen(NarrativeType.LOSS_RUN_NARRATIVE),
        "referral_memo": await gen(NarrativeType.REFERRAL_MEMO),
        "file_note": await gen(NarrativeType.UNDERWRITER_FILE_NOTE),
    }
    print(json.dumps(out))


asyncio.run(main())
"""

_AGENTIC_SNIPPET = r"""
import asyncio
import json
from datetime import datetime, timezone

from options import AgenticActionsOptions
from tools.draft_referral import DraftReferralHandler
from tools.execute_draft_action import build_default_executor, execute_draft_action
from draft_store import DraftExpiredException, DraftStore
from event_bus import InMemoryEventPublisher
from sqlalchemy import text


async def main() -> None:
    synth = json.loads(__import__("os").environ["CANVAS6_SYNTH_JSON"])
    db_url = __import__("os").environ["CANVAS6_DRAFT_DB_URL"]

    options = AgenticActionsOptions(draft_ttl_minutes=10)
    store = DraftStore(db_url)
    publisher = InMemoryEventPublisher()
    executor = build_default_executor(store, publisher, options)

    payload = synth["agentic"]["draft_referral"]
    handler = DraftReferralHandler(options)

    draft = await handler.create_draft(
        submission_id=payload["submission_id"],
        referral_reason=payload["referral_reason"],
        priority=payload["priority"],
        assigned_to_queue=payload["assigned_to_queue"],
    )
    await store.save_async(draft)
    await store.mark_confirmed_async(draft.draft_id, confirmed_by="uw-1")

    r1 = await execute_draft_action(draft.draft_id, store, executor)
    r2 = await execute_draft_action(draft.draft_id, store, executor)

    same = (r1.success and r2.success and (r1.result_data == r2.result_data))

    expired_raised = False
    payload2 = dict(payload)
    payload2["submission_id"] = payload["submission_id"] + "-expired"
    draft2 = await handler.create_draft(
        submission_id=payload2["submission_id"],
        referral_reason=payload2["referral_reason"],
        priority=payload2["priority"],
        assigned_to_queue=payload2["assigned_to_queue"],
    )
    await store.save_async(draft2)
    await store.mark_confirmed_async(draft2.draft_id, confirmed_by="uw-1")

    async with store._engine.begin() as conn:
        await conn.execute(
            text("UPDATE agent_draft_actions SET expires_at = :expires_at WHERE draft_id = :draft_id"),
            {"draft_id": draft2.draft_id, "expires_at": datetime(2000, 1, 1, tzinfo=timezone.utc).isoformat()},
        )

    try:
        await execute_draft_action(draft2.draft_id, store, executor)
    except DraftExpiredException:
        expired_raised = True

    out = {
        "first_success": bool(r1.success),
        "second_success": bool(r2.success),
        "idempotent_same_result": bool(same),
        "published_events": len(publisher.events),
        "expired_raised": bool(expired_raised),
        "published_events_after_expired": len(publisher.events),
    }
    print(json.dumps(out))


asyncio.run(main())
"""


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

CANVAS_6_ASSERTIONS = [
    "synthesize_canvas6_inputs",
    "wiremock_stub_openai_for_ai_components",
    "triage_intent_and_entities",
    "triage_routing_suggestion_matches_k12",
    "prefill_batching_20_fields_to_2_llm_calls",
    "referral_narrative_sections_ge_4",
    "draft_execute_publishes_one_event_and_is_idempotent",
    "draft_expired_exception_no_event",
]


def create_scenario() -> ScenarioRunner:
    return Canvas6AiIntelligenceScenario()

from __future__ import annotations

import json
from dataclasses import asdict
from typing import Any, Awaitable, Callable, Optional

from draft_store import DraftExpiredException, DraftNotConfirmedException, DraftNotFoundException, DraftStore
from event_bus import IEventPublisher
from options import AgenticActionsOptions
from tools.draft_checklist_update import DraftChecklistUpdateHandler
from tools.draft_field_update import DraftFieldUpdateHandler
from tools.draft_info_request import DraftInfoRequestHandler
from tools.draft_referral import DraftReferralHandler
from tools.execute_draft_action import build_default_executor, execute_draft_action

try:
    from ksquare.agent_orchestrator.models import ToolResult
except Exception:

    class ToolResult:  # type: ignore[no-redef]
        def __init__(self, success: bool, content: str, raw_data: Optional[dict] = None, error: Optional[str] = None) -> None:
            self.success = success
            self.content = content
            self.raw_data = raw_data
            self.error = error


async def register_agentic_actions(
    router: Any,
    draft_store: DraftStore,
    event_publisher: IEventPublisher,
    options: AgenticActionsOptions,
) -> None:
    handlers = {
        "draft_referral": DraftReferralHandler(options),
        "draft_field_update": DraftFieldUpdateHandler(options),
        "draft_info_request": DraftInfoRequestHandler(options),
        "draft_checklist_update": DraftChecklistUpdateHandler(options),
    }
    executor = build_default_executor(draft_store, event_publisher, options)

    async def _disabled(*_, **__) -> ToolResult:
        payload = {"error": "Write actions are disabled in read-only mode"}
        return ToolResult(success=False, content=json.dumps(payload), raw_data=payload, error=payload["error"])

    async def _draft(tool_name: str, **kwargs) -> ToolResult:
        if not options.enabled:
            return await _disabled()

        if tool_name == "draft_referral" and not options.enable_referral_drafting:
            return await _disabled()
        if tool_name == "draft_field_update" and not options.enable_field_updates:
            return await _disabled()
        if tool_name == "draft_info_request" and not options.enable_info_requests:
            return await _disabled()
        if tool_name == "draft_checklist_update" and not options.enable_checklist_updates:
            return await _disabled()

        handler = handlers[tool_name]
        draft = await handler.create_draft(**kwargs)
        await draft_store.save_async(draft)
        preview = {
            "draft_id": draft.draft_id,
            "preview_title": draft.preview_title,
            "preview_detail": draft.preview_detail,
            "requires_confirmation": draft.requires_confirmation,
            "action_type": draft.action_type.value,
        }
        return ToolResult(success=True, content=json.dumps(preview), raw_data=preview)

    async def _execute(draft_id: str, **_) -> ToolResult:
        if not options.enabled:
            return await _disabled()

        try:
            result = await execute_draft_action(draft_id, draft_store, executor)
            payload = asdict(result)
            return ToolResult(success=True, content=json.dumps(payload), raw_data=payload)
        except DraftExpiredException as ex:
            payload = {"error": str(ex)}
            return ToolResult(success=False, content=json.dumps(payload), raw_data=payload, error=str(ex))
        except (DraftNotConfirmedException, DraftNotFoundException) as ex:
            payload = {"error": str(ex)}
            return ToolResult(success=False, content=json.dumps(payload), raw_data=payload, error=str(ex))
        except Exception as ex:
            payload = {"error": str(ex)}
            return ToolResult(success=False, content=json.dumps(payload), raw_data=payload, error=str(ex))

    tools: dict[str, Callable[..., Awaitable[ToolResult]]] = getattr(router, "_tools", None) or {}
    tools["draft_referral"] = lambda submission_id, referral_reason, priority="Normal", assigned_to_queue="SeniorUW", **_: _draft(
        "draft_referral",
        submission_id=submission_id,
        referral_reason=referral_reason,
        priority=priority,
        assigned_to_queue=assigned_to_queue,
    )
    tools["draft_field_update"] = lambda submission_id, field_updates, **_: _draft(
        "draft_field_update",
        submission_id=submission_id,
        field_updates=field_updates,
    )
    tools["draft_info_request"] = lambda submission_id, broker_email, requested_items, due_date=None, custom_message=None, **_: _draft(
        "draft_info_request",
        submission_id=submission_id,
        broker_email=broker_email,
        requested_items=requested_items,
        due_date=due_date,
        custom_message=custom_message,
    )
    tools["draft_checklist_update"] = lambda submission_id, checklist_updates, **_: _draft(
        "draft_checklist_update",
        submission_id=submission_id,
        checklist_updates=checklist_updates,
    )
    tools["execute_draft_action"] = lambda draft_id, **_: _execute(draft_id=draft_id)

    setattr(router, "_tools", tools)

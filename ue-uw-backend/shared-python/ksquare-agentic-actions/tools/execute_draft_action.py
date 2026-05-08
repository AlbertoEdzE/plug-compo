from __future__ import annotations

from contracts import ActionExecutionResult, DraftActionType
from draft_store import DraftStore
from event_bus import IEventPublisher
from executor import ActionExecutor
from options import AgenticActionsOptions
from tools.draft_checklist_update import DraftChecklistUpdateHandler
from tools.draft_field_update import DraftFieldUpdateHandler
from tools.draft_info_request import DraftInfoRequestHandler
from tools.draft_referral import DraftReferralHandler


def build_default_executor(store: DraftStore, publisher: IEventPublisher, options: AgenticActionsOptions) -> ActionExecutor:
    handlers = {
        DraftActionType.CREATE_REFERRAL: DraftReferralHandler(options),
        DraftActionType.UPDATE_FIELDS: DraftFieldUpdateHandler(options),
        DraftActionType.SEND_INFO_REQUEST: DraftInfoRequestHandler(options),
        DraftActionType.UPDATE_CHECKLIST: DraftChecklistUpdateHandler(options),
    }
    return ActionExecutor(draft_store=store, event_publisher=publisher, handlers=handlers)


async def execute_draft_action(draft_id: str, store: DraftStore, executor: ActionExecutor) -> ActionExecutionResult:
    draft = await store.ensure_confirmed_for_execute_async(draft_id)
    return await executor.execute_async(draft)

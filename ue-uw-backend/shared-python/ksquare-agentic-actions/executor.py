from __future__ import annotations

from contracts import ActionExecutionResult, DraftAction, DraftActionStatus, DraftActionType, IActionExecutor, IDraftActionHandler
from draft_store import DraftNotConfirmedException, DraftStore
from event_bus import IEventPublisher


class ActionExecutor(IActionExecutor):
    def __init__(
        self,
        draft_store: DraftStore,
        event_publisher: IEventPublisher,
        handlers: dict[DraftActionType, IDraftActionHandler],
    ) -> None:
        self._store = draft_store
        self._publisher = event_publisher
        self._handlers = handlers

    async def execute_async(self, draft: DraftAction) -> ActionExecutionResult:
        if draft.status != DraftActionStatus.CONFIRMED:
            raise DraftNotConfirmedException("Draft not found or not confirmed")

        existing = await self._store.get_execution_result_async(draft.draft_id)
        if existing is not None and existing.success:
            return existing

        handler = self._handlers.get(draft.action_type)
        if handler is None:
            result = ActionExecutionResult(
                draft_id=draft.draft_id,
                action_type=draft.action_type,
                success=False,
                error_message=f"No handler registered for action_type={draft.action_type.value}",
            )
            await self._store.save_execution_result_async(result)
            return result

        result = await handler.execute(draft)
        await self._store.save_execution_result_async(result)
        if result.success:
            await self._publisher.publish_async(
                event_type="agentic_action.executed",
                data={
                    "draft_id": draft.draft_id,
                    "action_type": draft.action_type.value,
                    "submission_id": draft.submission_id,
                    "result_data": result.result_data or {},
                },
            )
        return result

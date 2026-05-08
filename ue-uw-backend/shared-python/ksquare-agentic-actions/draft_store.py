from __future__ import annotations

import json
from dataclasses import asdict
from datetime import datetime, timedelta, timezone

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, create_async_engine
from sqlalchemy.orm import sessionmaker

from contracts import ActionExecutionResult, DraftAction, DraftActionStatus, DraftActionType, IDraftStore


class DraftExpiredException(Exception):
    pass


class DraftNotConfirmedException(Exception):
    pass


class DraftNotFoundException(Exception):
    pass


def _utcnow() -> datetime:
    return datetime.now(timezone.utc)


def _parse_dt(value: str) -> datetime:
    dt = datetime.fromisoformat(value)
    return dt if dt.tzinfo is not None else dt.replace(tzinfo=timezone.utc)


def _iso(dt: datetime) -> str:
    return dt.astimezone(timezone.utc).isoformat()


CREATE_TABLE_SQL = """
CREATE TABLE IF NOT EXISTS agent_draft_actions (
    draft_id        TEXT NOT NULL PRIMARY KEY,
    action_type     TEXT NOT NULL,
    submission_id   TEXT NOT NULL,
    status          TEXT NOT NULL DEFAULT 'Pending',
    payload_json    TEXT NOT NULL,
    preview_title   TEXT NOT NULL,
    preview_detail  TEXT NOT NULL,
    created_by      TEXT NULL,
    created_at      TEXT NOT NULL,
    expires_at      TEXT NOT NULL,
    executed_at     TEXT NULL,
    execution_result_json TEXT NULL
);
"""


class DraftStore(IDraftStore):
    def __init__(self, database_url: str) -> None:
        self._engine: AsyncEngine = create_async_engine(database_url, future=True)
        self._session_factory = sessionmaker(self._engine, class_=AsyncSession, expire_on_commit=False)

    async def initialize_async(self) -> None:
        async with self._engine.begin() as conn:
            await conn.execute(text(CREATE_TABLE_SQL))

    async def save_async(self, draft: DraftAction) -> None:
        await self.initialize_async()
        async with self._session_factory() as session:
            await session.execute(
                text(
                    """
                    INSERT INTO agent_draft_actions (
                        draft_id, action_type, submission_id, status,
                        payload_json, preview_title, preview_detail,
                        created_at, expires_at, execution_result_json
                    )
                    VALUES (
                        :draft_id, :action_type, :submission_id, :status,
                        :payload_json, :preview_title, :preview_detail,
                        :created_at, :expires_at, NULL
                    )
                    ON CONFLICT(draft_id) DO NOTHING
                    """
                ),
                {
                    "draft_id": draft.draft_id,
                    "action_type": draft.action_type.value,
                    "submission_id": draft.submission_id,
                    "status": draft.status.value,
                    "payload_json": json.dumps(draft.payload),
                    "preview_title": draft.preview_title,
                    "preview_detail": draft.preview_detail,
                    "created_at": draft.created_at,
                    "expires_at": draft.expires_at,
                },
            )
            await session.commit()

    async def get_async(self, draft_id: str) -> DraftAction | None:
        await self.initialize_async()
        async with self._session_factory() as session:
            row = (
                await session.execute(
                    text(
                        """
                        SELECT draft_id, action_type, submission_id, status,
                               payload_json, preview_title, preview_detail,
                               created_at, expires_at
                        FROM agent_draft_actions
                        WHERE draft_id = :draft_id
                        """
                    ),
                    {"draft_id": draft_id},
                )
            ).mappings().first()
            if row is None:
                return None

            status = DraftActionStatus(str(row["status"]))
            expires_at = str(row["expires_at"])
            if status in (DraftActionStatus.PENDING, DraftActionStatus.CONFIRMED) and _parse_dt(expires_at) < _utcnow():
                status = DraftActionStatus.EXPIRED

            return DraftAction(
                draft_id=str(row["draft_id"]),
                action_type=DraftActionType(str(row["action_type"])),
                submission_id=str(row["submission_id"]),
                status=status,
                preview_title=str(row["preview_title"]),
                preview_detail=str(row["preview_detail"]),
                payload=json.loads(str(row["payload_json"]) or "{}"),
                requires_confirmation=True,
                created_at=str(row["created_at"]),
                expires_at=expires_at,
            )

    async def mark_confirmed_async(self, draft_id: str, confirmed_by: str) -> None:
        await self.initialize_async()
        draft = await self.get_async(draft_id)
        if draft is None:
            raise DraftNotFoundException("Draft not found.")
        if draft.status == DraftActionStatus.EXPIRED:
            raise DraftExpiredException("Action expired — please ask the assistant again.")
        if draft.status != DraftActionStatus.PENDING:
            return

        async with self._session_factory() as session:
            await session.execute(
                text(
                    """
                    UPDATE agent_draft_actions
                    SET status = :status, created_by = :created_by
                    WHERE draft_id = :draft_id
                    """
                ),
                {"draft_id": draft_id, "status": DraftActionStatus.CONFIRMED.value, "created_by": confirmed_by},
            )
            await session.commit()

    async def save_execution_result_async(self, result: ActionExecutionResult) -> None:
        await self.initialize_async()
        async with self._session_factory() as session:
            await session.execute(
                text(
                    """
                    UPDATE agent_draft_actions
                    SET status = :status,
                        executed_at = :executed_at,
                        execution_result_json = :result_json
                    WHERE draft_id = :draft_id
                    """
                ),
                {
                    "draft_id": result.draft_id,
                    "status": DraftActionStatus.EXECUTED.value if result.success else DraftActionStatus.CONFIRMED.value,
                    "executed_at": _iso(_utcnow()),
                    "result_json": json.dumps(asdict(result)),
                },
            )
            await session.commit()

    async def get_execution_result_async(self, draft_id: str) -> ActionExecutionResult | None:
        await self.initialize_async()
        async with self._session_factory() as session:
            row = (
                await session.execute(
                    text("SELECT execution_result_json FROM agent_draft_actions WHERE draft_id = :draft_id"),
                    {"draft_id": draft_id},
                )
            ).mappings().first()
            if row is None:
                return None
            raw = row.get("execution_result_json", None)
            if raw is None:
                return None
            try:
                data = json.loads(str(raw))
                return ActionExecutionResult(
                    draft_id=str(data.get("draft_id", draft_id)),
                    action_type=DraftActionType(str(data.get("action_type"))),
                    success=bool(data.get("success", False)),
                    result_data=data.get("result_data", None),
                    error_message=data.get("error_message", None),
                )
            except Exception:
                return None

    async def ensure_confirmed_for_execute_async(self, draft_id: str) -> DraftAction:
        draft = await self.get_async(draft_id)
        if draft is None:
            raise DraftNotFoundException("Draft not found.")
        if draft.status == DraftActionStatus.EXPIRED:
            raise DraftExpiredException("Action expired — please ask the assistant again.")
        if draft.status != DraftActionStatus.CONFIRMED:
            raise DraftNotConfirmedException("Draft not found or not confirmed")
        return draft

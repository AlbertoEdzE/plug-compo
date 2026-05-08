from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass
from enum import Enum
from typing import Any, Optional


class DraftActionType(str, Enum):
    CREATE_REFERRAL = "CreateReferral"
    UPDATE_FIELDS = "UpdateFields"
    SEND_INFO_REQUEST = "SendInfoRequest"
    UPDATE_CHECKLIST = "UpdateChecklist"


class DraftActionStatus(str, Enum):
    PENDING = "Pending"
    CONFIRMED = "Confirmed"
    EXECUTED = "Executed"
    CANCELLED = "Cancelled"
    EXPIRED = "Expired"


@dataclass
class DraftAction:
    draft_id: str
    action_type: DraftActionType
    submission_id: str
    status: DraftActionStatus
    preview_title: str
    preview_detail: str
    payload: dict[str, Any]
    requires_confirmation: bool = True
    created_at: str = ""
    expires_at: str = ""


@dataclass
class ActionExecutionResult:
    draft_id: str
    action_type: DraftActionType
    success: bool
    result_data: Optional[dict] = None
    error_message: Optional[str] = None


class IDraftActionHandler(ABC):
    @abstractmethod
    async def create_draft(self, **kwargs) -> DraftAction:
        raise NotImplementedError

    @abstractmethod
    async def execute(self, draft: DraftAction) -> ActionExecutionResult:
        raise NotImplementedError


class IDraftStore(ABC):
    @abstractmethod
    async def save_async(self, draft: DraftAction) -> None:
        raise NotImplementedError

    @abstractmethod
    async def get_async(self, draft_id: str) -> DraftAction | None:
        raise NotImplementedError

    @abstractmethod
    async def mark_confirmed_async(self, draft_id: str, confirmed_by: str) -> None:
        raise NotImplementedError


class IActionExecutor(ABC):
    @abstractmethod
    async def execute_async(self, draft: DraftAction) -> ActionExecutionResult:
        raise NotImplementedError

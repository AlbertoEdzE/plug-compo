# Component 25 — Agentic Action Toolkit

**Library**: `KSquare.AgenticActions`  
**Layer**: AI / Agentic UI  
**Default Provider**: Azure OpenAI (via Component 13 AgentOrchestrator)  
**Alternate Providers**: Mock  
**Language**: Python 3.11 (extends Component 13 AG UI Function)  
**Depends On**: Component 13 (AgentOrchestrator — for tool registration), Component 02 (EventBus)

---

## Why This Is a Pluggable Component

Component 13 (`KSquare.AgentOrchestrator`) defines the AG UI agent loop with **read-only tools**.
The agent can answer questions and retrieve data, but it cannot take actions.

For a truly **agentic UW experience**, the AI assistant must also be able to:
1. **Draft and stage write actions** — create a referral memo draft, pre-populate a checklist item,
   stage a field update — without immediately committing them
2. **Execute confirmed actions** — after the underwriter approves a draft action, commit it to the
   domain API
3. **Orchestrate multi-step workflows** — e.g., "Flag for referral AND draft the referral memo AND
   notify senior UW" as a single agent turn

`KSquare.AgenticActions` defines the **write-side tool set** that plugs into the Agent Orchestrator's
tool registry. It is separate from Component 13 because:
- Write tools have a different risk profile — they need confirmation UX before execution
- Write tools call domain APIs, not read-only data services
- Write tools can be enabled/disabled per customer (some customers may want read-only agent)
- The tool set evolves independently of the agent loop infrastructure

---

## Interface Contract

```python
from abc import ABC, abstractmethod

class IDraftActionHandler(ABC):
    @abstractmethod
    async def create_draft(self, **kwargs) -> "DraftAction":
        ...

    @abstractmethod
    async def execute(self, draft: "DraftAction") -> "ActionExecutionResult":
        ...

class IDraftStore(ABC):
    @abstractmethod
    async def save_async(self, draft: "DraftAction") -> None:
        ...

    @abstractmethod
    async def get_async(self, draft_id: str) -> "DraftAction | None":
        ...

    @abstractmethod
    async def mark_confirmed_async(self, draft_id: str, confirmed_by: str) -> None:
        ...

class IActionExecutor(ABC):
    @abstractmethod
    async def execute_async(self, draft: "DraftAction") -> "ActionExecutionResult":
        ...
```

---

## AG UI Protocol — Two-Phase Action Pattern

All write tools follow a **Draft → Confirm → Execute** pattern to prevent accidental mutations:

```
User: "Refer this submission to senior UW with the reason that TIV exceeds authority"
                    ↓
Agent: calls draft_referral tool
       → returns DraftAction { id, type: "CreateReferral", preview: "...", requires_confirmation: true }
                    ↓
Frontend renders: [Preview card] "Create Referral — TIV exceeds binding authority limit"
                  [Confirm] [Cancel] buttons
                    ↓
User clicks Confirm
                    ↓
Frontend calls execute_action(draft_id)
                    ↓
Agent: calls execute_draft_action tool → commits to domain API → returns success
```

---

## Tool Definitions (Write-Side)

```python
AGENTIC_TOOL_DEFINITIONS = [

    {
        "type": "function",
        "function": {
            "name": "draft_referral",
            "description": (
                "Stage a referral creation for the current submission. "
                "Returns a draft action for underwriter confirmation — does NOT create the referral yet. "
                "Use when the underwriter asks to refer the submission to a senior underwriter."
            ),
            "parameters": {
                "type": "object",
                "properties": {
                    "submission_id": {"type": "string"},
                    "referral_reason": {
                        "type": "string",
                        "description": "Plain-English reason for referral (will appear in the referral record)"
                    },
                    "priority": {
                        "type": "string",
                        "enum": ["Normal", "High", "Urgent"],
                        "description": "Referral priority level"
                    },
                    "assigned_to_queue": {
                        "type": "string",
                        "description": "Target underwriter queue (e.g., 'SeniorUW-K12', 'Reinsurance')"
                    }
                },
                "required": ["submission_id", "referral_reason"]
            }
        }
    },

    {
        "type": "function",
        "function": {
            "name": "draft_field_update",
            "description": (
                "Stage an update to one or more submission form fields. "
                "Returns a draft for confirmation — does NOT save yet. "
                "Use when the underwriter asks to update a field value based on additional information."
            ),
            "parameters": {
                "type": "object",
                "properties": {
                    "submission_id": {"type": "string"},
                    "field_updates": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "field_name": {"type": "string"},
                                "new_value": {"type": "string"},
                                "reason": {"type": "string"}
                            },
                            "required": ["field_name", "new_value"]
                        }
                    }
                },
                "required": ["submission_id", "field_updates"]
            }
        }
    },

    {
        "type": "function",
        "function": {
            "name": "draft_info_request",
            "description": (
                "Stage an information request to the broker. "
                "Returns a draft email for confirmation — does NOT send yet. "
                "Use when the underwriter asks to request missing documents or clarifications from the broker."
            ),
            "parameters": {
                "type": "object",
                "properties": {
                    "submission_id": {"type": "string"},
                    "broker_email": {"type": "string"},
                    "requested_items": {
                        "type": "array",
                        "items": {"type": "string"},
                        "description": "List of items being requested (e.g., ['Updated loss run', '5-year financials'])"
                    },
                    "due_date": {
                        "type": "string",
                        "description": "Optional due date for the request (ISO 8601 date)"
                    },
                    "custom_message": {
                        "type": "string",
                        "description": "Optional additional text to include in the request email"
                    }
                },
                "required": ["submission_id", "broker_email", "requested_items"]
            }
        }
    },

    {
        "type": "function",
        "function": {
            "name": "draft_checklist_update",
            "description": (
                "Stage an update to one or more underwriting checklist items. "
                "Returns a draft for confirmation. "
                "Use when the underwriter asks to mark a checklist item as complete or add a note."
            ),
            "parameters": {
                "type": "object",
                "properties": {
                    "submission_id": {"type": "string"},
                    "checklist_updates": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "item_id": {"type": "string"},
                                "status": {"type": "string", "enum": ["Complete", "NotApplicable", "NeedsReview"]},
                                "note": {"type": "string"}
                            },
                            "required": ["item_id", "status"]
                        }
                    }
                },
                "required": ["submission_id", "checklist_updates"]
            }
        }
    },

    {
        "type": "function",
        "function": {
            "name": "execute_draft_action",
            "description": (
                "Execute a previously staged draft action after the underwriter has confirmed it. "
                "Should only be called after the user has explicitly confirmed the draft. "
                "Returns the result of the committed action."
            ),
            "parameters": {
                "type": "object",
                "properties": {
                    "draft_id": {
                        "type": "string",
                        "description": "The draft action ID returned by a previous draft_* tool call"
                    }
                },
                "required": ["draft_id"]
            }
        }
    }
]
```

---

## Models

```python
from dataclasses import dataclass, field
from typing import Optional, Any
from enum import Enum

class DraftActionType(str, Enum):
    CREATE_REFERRAL    = "CreateReferral"
    UPDATE_FIELDS      = "UpdateFields"
    SEND_INFO_REQUEST  = "SendInfoRequest"
    UPDATE_CHECKLIST   = "UpdateChecklist"

class DraftActionStatus(str, Enum):
    PENDING    = "Pending"     # created; awaiting confirmation
    CONFIRMED  = "Confirmed"   # user confirmed; execute_draft_action was called
    EXECUTED   = "Executed"    # domain API call succeeded
    CANCELLED  = "Cancelled"   # user clicked Cancel
    EXPIRED    = "Expired"     # not confirmed within TTL (default: 10 minutes)

@dataclass
class DraftAction:
    draft_id: str
    action_type: DraftActionType
    submission_id: str
    status: DraftActionStatus
    preview_title: str         # short title for the confirmation card (≤ 60 chars)
    preview_detail: str        # 1-2 sentences describing what will happen
    payload: dict[str, Any]    # action-specific data to execute
    requires_confirmation: bool = True
    created_at: str = ""
    expires_at: str = ""       # 10 minutes from created_at

@dataclass
class ActionExecutionResult:
    draft_id: str
    action_type: DraftActionType
    success: bool
    result_data: Optional[dict] = None  # e.g., {"referral_id": "REF-001"}
    error_message: Optional[str] = None
```

---

## Draft Action Handlers

```python
class DraftReferralHandler:
    def __init__(self, referral_api_client: ReferralApiClient, narrative_adapter):
        self._referral_api = referral_api_client
        self._narrative = narrative_adapter

    async def create_draft(
        self, submission_id: str, referral_reason: str,
        priority: str = "Normal", assigned_to_queue: str = "SeniorUW"
    ) -> DraftAction:
        # Generate referral memo narrative to include in preview
        # (optional — if narrative adapter is configured)
        preview_detail = (
            f"Create referral to {assigned_to_queue} queue with {priority} priority. "
            f"Reason: {referral_reason[:100]}"
        )
        return DraftAction(
            draft_id=str(uuid.uuid4()),
            action_type=DraftActionType.CREATE_REFERRAL,
            submission_id=submission_id,
            status=DraftActionStatus.PENDING,
            preview_title=f"Create Referral — {priority} Priority",
            preview_detail=preview_detail,
            payload={
                "submission_id": submission_id,
                "reason": referral_reason,
                "priority": priority,
                "queue": assigned_to_queue
            },
            requires_confirmation=True,
            created_at=datetime.utcnow().isoformat(),
            expires_at=(datetime.utcnow() + timedelta(minutes=10)).isoformat()
        )

    async def execute(self, draft: DraftAction) -> ActionExecutionResult:
        p = draft.payload
        result = await self._referral_api.create_referral_async(
            submission_id=p["submission_id"],
            reason=p["reason"],
            priority=p["priority"],
            queue=p["queue"]
        )
        return ActionExecutionResult(
            draft_id=draft.draft_id,
            action_type=DraftActionType.CREATE_REFERRAL,
            success=True,
            result_data={"referral_id": result.referral_id}
        )


class DraftInfoRequestHandler:
    def __init__(self, email_adapter):
        self._email = email_adapter

    async def create_draft(
        self, submission_id: str, broker_email: str,
        requested_items: list[str], due_date: str = None,
        custom_message: str = None
    ) -> DraftAction:
        items_preview = "; ".join(requested_items[:3])
        if len(requested_items) > 3:
            items_preview += f" (+{len(requested_items) - 3} more)"
        return DraftAction(
            draft_id=str(uuid.uuid4()),
            action_type=DraftActionType.SEND_INFO_REQUEST,
            submission_id=submission_id,
            status=DraftActionStatus.PENDING,
            preview_title="Send Information Request to Broker",
            preview_detail=f"Email to {broker_email} requesting: {items_preview}",
            payload={
                "submission_id": submission_id,
                "broker_email": broker_email,
                "requested_items": requested_items,
                "due_date": due_date,
                "custom_message": custom_message
            },
            requires_confirmation=True,
            created_at=datetime.utcnow().isoformat(),
            expires_at=(datetime.utcnow() + timedelta(minutes=10)).isoformat()
        )

    async def execute(self, draft: DraftAction) -> ActionExecutionResult:
        p = draft.payload
        await self._email.send_async(
            to=[p["broker_email"]],
            template_id="info-request",
            data={
                "requested_items": p["requested_items"],
                "due_date": p.get("due_date", ""),
                "custom_message": p.get("custom_message", "")
            }
        )
        return ActionExecutionResult(
            draft_id=draft.draft_id,
            action_type=DraftActionType.SEND_INFO_REQUEST,
            success=True
        )
```

---

## Tool Router Integration (extends Component 13)

```python
# In AgentOrchestrator's ToolRouter, add write-side tools:

class AgentToolRouter:

    # Existing read tools (Component 13)
    READ_TOOLS = [
        "get_submission_summary", "get_loss_history", "get_risk_indicators",
        "get_coverage_summary", "get_document_excerpt", "get_checklist_status"
    ]

    # Write tools (this component — Component 25)
    WRITE_TOOLS = [
        "draft_referral", "draft_field_update",
        "draft_info_request", "draft_checklist_update",
        "execute_draft_action"
    ]

    async def route(self, tool_name: str, tool_args: dict, context: AgentContext) -> str:
        if tool_name in self.READ_TOOLS:
            return await self._route_read(tool_name, tool_args, context)
        elif tool_name in self.WRITE_TOOLS:
            # Write tools always go through draft_store — no direct execution
            return await self._route_write(tool_name, tool_args, context)
        else:
            return json.dumps({"error": f"Unknown tool: {tool_name}"})

    async def _route_write(self, tool_name: str, tool_args: dict, context: AgentContext) -> str:
        if tool_name == "execute_draft_action":
            # Execution path — retrieve and run the staged draft
            draft_id = tool_args["draft_id"]
            draft = await self._draft_store.get_async(draft_id)
            if draft is None or draft.status != DraftActionStatus.CONFIRMED:
                return json.dumps({"error": "Draft not found or not confirmed"})
            result = await self._action_executor.execute_async(draft)
            return json.dumps(asdict(result))
        else:
            # Draft creation path — build draft, store, return preview
            handler = self._resolve_handler(tool_name)
            draft = await handler.create_draft(**tool_args)
            await self._draft_store.save_async(draft)
            return json.dumps({
                "draft_id": draft.draft_id,
                "preview_title": draft.preview_title,
                "preview_detail": draft.preview_detail,
                "requires_confirmation": draft.requires_confirmation,
                "action_type": draft.action_type.value
            })
```

---

## AG UI SSE Events for Action Flow

The frontend receives these event types during an agentic action turn:

```
data: {"type": "message_start", "message": {"id": "msg_xxx"}}

data: {"type": "content_block_delta", "delta": {"type": "text_delta", 
       "text": "I'll stage a referral for you. Let me prepare the details..."}}

data: {"type": "tool_use", "id": "tool_use_yyy", "name": "draft_referral",
       "input": {"submission_id": "SUB-001", "referral_reason": "TIV exceeds $25M authority limit"}}

data: {"type": "tool_result", "tool_use_id": "tool_use_yyy",
       "content": {"draft_id": "draft-zzz", "preview_title": "Create Referral — Normal Priority",
                   "preview_detail": "Create referral to SeniorUW queue...", 
                   "requires_confirmation": true}}

data: {"type": "content_block_delta", "delta": {"type": "text_delta",
       "text": "I've prepared a referral draft. Please review and confirm above."}}

data: {"type": "message_stop"}
```

The frontend renders the `tool_result` with `requires_confirmation: true` as an **action card**
with Confirm/Cancel buttons. On Confirm, the frontend calls the `/execute` endpoint with `draft_id`.

---

## SQL Schema (Draft Store)

```sql
CREATE TABLE agent_draft_actions (
    draft_id        NVARCHAR(64) NOT NULL PRIMARY KEY,
    action_type     NVARCHAR(50) NOT NULL,
    submission_id   NVARCHAR(64) NOT NULL,
    status          NVARCHAR(30) NOT NULL DEFAULT 'Pending',
    payload_json    NVARCHAR(MAX) NOT NULL,
    preview_title   NVARCHAR(100) NOT NULL,
    preview_detail  NVARCHAR(500) NOT NULL,
    created_by      NVARCHAR(200) NULL,
    created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    expires_at      DATETIMEOFFSET NOT NULL,
    executed_at     DATETIMEOFFSET NULL,
    INDEX IX_draft_submission (submission_id, status),
    INDEX IX_draft_expiry     (status, expires_at)     -- for cleanup job
);
```

---

## Configuration

```python
@dataclass
class AgenticActionsOptions:
    enabled: bool = True           # set False to disable all write tools (read-only mode)
    draft_ttl_minutes: int = 10    # drafts expire if not confirmed
    # Individual tool toggles (for graduated rollout)
    enable_referral_drafting: bool = True
    enable_field_updates: bool = True
    enable_info_requests: bool = True
    enable_checklist_updates: bool = True
```

---

## Failure States

| Scenario | Behaviour |
|---|---|
| Draft expires before confirmation | Return error to frontend: "Action expired — please ask the assistant again" |
| Domain API fails during execute | Return ActionExecutionResult { success=False, error_message }; agent reports failure to user |
| User calls execute on non-CONFIRMED draft | Return error — execute requires explicit confirmation |
| Write tools disabled in config | Tool calls return `{"error": "Write actions are disabled in read-only mode"}` |
| Concurrent execute of same draft | Idempotency check on draft status; second execute returns same result |

---

## Claude Code Build Prompt

```
Build a Python 3.11 module called ksquare-agentic-actions at path:
shared-python/ksquare-agentic-actions/

This module extends the KSquare.AgentOrchestrator (Component 13) with write-side tools
that follow a Draft → Confirm → Execute pattern. Tools never mutate domain state directly —
they first create a DraftAction returned to the frontend as a confirmation card; execution
only happens when the user explicitly confirms.

Module structure:
  shared-python/ksquare-agentic-actions/
  ├── tool_definitions.py        ← AGENTIC_TOOL_DEFINITIONS list (write-side tools only)
  ├── contracts.py               ← DraftAction, DraftActionType, DraftActionStatus,
  │                                 ActionExecutionResult dataclasses
  ├── options.py                 ← AgenticActionsOptions dataclass
  ├── handlers/
  │   ├── draft_referral.py      ← DraftReferralHandler
  │   ├── draft_field_update.py  ← DraftFieldUpdateHandler
  │   ├── draft_info_request.py  ← DraftInfoRequestHandler
  │   └── draft_checklist.py     ← DraftChecklistUpdateHandler
  ├── store/
  │   └── draft_store.py         ← DraftStore; SQL via SQLAlchemy or plain asyncpg
  ├── executor.py                ← ActionExecutor; routes execute call to correct handler
  ├── router_extension.py        ← AgentToolRouterExtension; plugs into Component 13 ToolRouter
  ├── cleanup.py                 ← MarkExpiredDraftsTask; sets status=Expired for past expires_at
  └── tests/

DraftReferralHandler.create_draft:
  - Validate submission_id is provided
  - Build DraftAction with preview_title, preview_detail from inputs
  - draft_id = str(uuid.uuid4())
  - expires_at = utcnow + draft_ttl_minutes

ActionExecutor.execute_async:
  - SELECT draft FROM DB
  - If not found or status != Confirmed: raise DraftNotConfirmedException
  - Route to handler based on action_type
  - UPDATE draft status = Executed, executed_at = utcnow
  - Return ActionExecutionResult

AgentToolRouterExtension:
  - Register all AGENTIC_TOOL_DEFINITIONS with the parent ToolRouter
  - For non-execute tool calls: call correct handler.create_draft, save to DB, return preview JSON
  - For execute_draft_action: call ActionExecutor.execute_async

Tests:
  - draft_referral returns DraftAction with status=Pending and requires_confirmation=True
  - execute_async on Pending draft raises DraftNotConfirmedException
  - execute_async on Confirmed draft calls domain handler and returns success result
  - Expired draft returns error on execute attempt
  - tool_definitions includes all 5 write tools
  Use pytest + pytest-asyncio + Moq-equivalent (unittest.mock).

Requirements:
  sqlalchemy>=2.0
  azure-functions>=1.19.0
  pytest
  pytest-asyncio
```

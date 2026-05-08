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
                        "description": "Plain-English reason for referral (will appear in the referral record)",
                    },
                    "priority": {
                        "type": "string",
                        "enum": ["Normal", "High", "Urgent"],
                        "description": "Referral priority level",
                    },
                    "assigned_to_queue": {
                        "type": "string",
                        "description": "Target underwriter queue (e.g., 'SeniorUW-K12', 'Reinsurance')",
                    },
                },
                "required": ["submission_id", "referral_reason"],
            },
        },
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
                                "reason": {"type": "string"},
                            },
                            "required": ["field_name", "new_value"],
                        },
                    },
                },
                "required": ["submission_id", "field_updates"],
            },
        },
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
                        "description": "List of items being requested (e.g., ['Updated loss run', '5-year financials'])",
                    },
                    "due_date": {"type": "string", "description": "Optional due date for the request (ISO 8601 date)"},
                    "custom_message": {
                        "type": "string",
                        "description": "Optional additional text to include in the request email",
                    },
                },
                "required": ["submission_id", "broker_email", "requested_items"],
            },
        },
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
                                "note": {"type": "string"},
                            },
                            "required": ["item_id", "status"],
                        },
                    },
                },
                "required": ["submission_id", "checklist_updates"],
            },
        },
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
                        "description": "The draft action ID returned by a previous draft_* tool call",
                    }
                },
                "required": ["draft_id"],
            },
        },
    },
]

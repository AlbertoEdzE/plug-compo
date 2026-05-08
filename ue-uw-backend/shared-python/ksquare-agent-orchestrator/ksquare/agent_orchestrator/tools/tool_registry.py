TOOL_DEFINITIONS = [
    {
        "type": "function",
        "function": {
            "name": "get_submission_summary",
            "description": "Returns key submission header details including institution name, status, broker, effective date, and underwriter assignment. Use this when the user asks 'what is this submission?' or about submission status.",
            "parameters": {
                "type": "object",
                "properties": {
                    "submission_id": {"type": "string", "description": "The submission ID (e.g. SUB-7829)"}
                },
                "required": ["submission_id"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "get_loss_history",
            "description": "Returns the structured loss run history for the submission, including year-by-year claims count, incurred amounts, and loss ratios. Use when asked about prior losses, claims history, or loss ratio.",
            "parameters": {
                "type": "object",
                "properties": {
                    "submission_id": {"type": "string"},
                    "years": {"type": "integer", "description": "Number of years to return (default: 5)", "default": 5},
                },
                "required": ["submission_id"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "get_risk_indicators",
            "description": "Returns computed risk indicator scores: Campus Safety Rating, Claims Severity, Policy Complexity, Litigation Exposure, and Appetite Fit. Use when asked about risk, appetite, or suitability.",
            "parameters": {
                "type": "object",
                "properties": {"submission_id": {"type": "string"}},
                "required": ["submission_id"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "get_coverage_summary",
            "description": "Returns the requested coverage lines, limits, retentions, and estimated premiums for the submission. Use when asked about coverage, limits, or premium.",
            "parameters": {
                "type": "object",
                "properties": {"submission_id": {"type": "string"}},
                "required": ["submission_id"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "get_document_excerpt",
            "description": "Retrieves the most relevant excerpt from a specific attached document using semantic search. Use when asked to explain something from an attached document such as a loss run or application form.",
            "parameters": {
                "type": "object",
                "properties": {
                    "submission_id": {"type": "string"},
                    "query": {"type": "string", "description": "The specific question or topic to search for within the document"},
                    "document_type": {
                        "type": "string",
                        "enum": ["LossRun", "ACORD125", "Financial", "Supporting"],
                        "description": "The type of document to search",
                    },
                },
                "required": ["submission_id", "query"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "get_checklist_status",
            "description": "Returns the current document checklist status — which supporting documents have been received and which are still missing. Use when asked about missing documents or checklist.",
            "parameters": {
                "type": "object",
                "properties": {"submission_id": {"type": "string"}},
                "required": ["submission_id"],
            },
        },
    },
]


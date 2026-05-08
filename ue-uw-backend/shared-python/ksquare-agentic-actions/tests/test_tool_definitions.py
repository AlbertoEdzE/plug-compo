from tool_definitions import AGENTIC_TOOL_DEFINITIONS


def test_tool_definitions_contains_all_write_tools() -> None:
    names = [d["function"]["name"] for d in AGENTIC_TOOL_DEFINITIONS]
    assert set(names) == {
        "draft_referral",
        "draft_field_update",
        "draft_info_request",
        "draft_checklist_update",
        "execute_draft_action",
    }

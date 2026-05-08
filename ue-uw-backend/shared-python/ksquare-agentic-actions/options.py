from dataclasses import dataclass


@dataclass
class AgenticActionsOptions:
    enabled: bool = True
    draft_ttl_minutes: int = 10
    enable_referral_drafting: bool = True
    enable_field_updates: bool = True
    enable_info_requests: bool = True
    enable_checklist_updates: bool = True

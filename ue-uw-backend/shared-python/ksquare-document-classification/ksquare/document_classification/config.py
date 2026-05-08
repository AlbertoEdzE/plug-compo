from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class ClassificationConfig:
    provider: str = "azure_then_heuristic"
    azure_endpoint: str = ""
    azure_classifier_model_id: str = "ksquare-doc-classifier-v1"
    use_managed_identity: bool = True
    azure_api_key: str | None = None
    confidence_threshold_auto: float = 0.85
    confidence_threshold_review: float = 0.70
    heuristic_fallback_enabled: bool = True

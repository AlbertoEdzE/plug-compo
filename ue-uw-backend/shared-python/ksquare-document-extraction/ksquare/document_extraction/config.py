from __future__ import annotations

from dataclasses import dataclass, field


@dataclass(frozen=True)
class ExtractionConfig:
    endpoint: str
    use_managed_identity: bool = True
    api_key: str | None = None

    model_map: dict[str, str] = field(
        default_factory=lambda: {
            "ACORD125": "prebuilt-document",
            "LossRun": "prebuilt-layout",
            "Financial": "prebuilt-layout",
            "ApplicationForm": "prebuilt-document",
            "default": "prebuilt-document",
        }
    )

    low_confidence_threshold: float = 0.75
    auto_accept_threshold: float = 0.90

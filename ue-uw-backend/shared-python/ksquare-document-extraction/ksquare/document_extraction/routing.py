from __future__ import annotations

from .config import ExtractionConfig


def resolve_model_id(config: ExtractionConfig, model_hint: str | None) -> str:
    if not model_hint:
        return config.model_map.get("default", "prebuilt-document")

    return config.model_map.get(model_hint, config.model_map.get("default", "prebuilt-document"))

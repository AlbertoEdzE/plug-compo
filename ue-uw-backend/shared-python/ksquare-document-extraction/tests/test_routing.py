from ksquare.document_extraction.config import ExtractionConfig
from ksquare.document_extraction.routing import resolve_model_id


def test_resolve_model_id_uses_default_when_none():
    cfg = ExtractionConfig(endpoint="https://example.com")
    assert resolve_model_id(cfg, None) == "prebuilt-document"


def test_resolve_model_id_uses_mapping_when_present():
    cfg = ExtractionConfig(endpoint="https://example.com")
    assert resolve_model_id(cfg, "LossRun") == "prebuilt-layout"


def test_resolve_model_id_falls_back_to_default():
    cfg = ExtractionConfig(endpoint="https://example.com")
    assert resolve_model_id(cfg, "UnknownType") == "prebuilt-document"

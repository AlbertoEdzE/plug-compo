from pathlib import Path

import yaml


def test_alert_rules_yaml_is_valid_and_has_required_fields():
    root = Path(__file__).resolve().parents[1]
    p = root / "alert-rules.yaml"
    data = yaml.safe_load(p.read_text(encoding="utf-8"))
    assert "alerts" in data
    assert isinstance(data["alerts"], list)
    assert len(data["alerts"]) >= 3

    for alert in data["alerts"]:
        assert "name" in alert and alert["name"]
        assert "severity" in alert
        assert isinstance(alert["severity"], int)
        assert "description" in alert and alert["description"]


from __future__ import annotations

import hashlib


class PromptVersionManager:
    VERSIONS = {
        "v1": "v1",
        "v2": "v2",
    }

    AB_SPLIT = {"v1": 0.90, "v2": 0.10}

    def select_version(self, session_id: str) -> str:
        bucket = int(hashlib.md5(session_id.encode("utf-8")).hexdigest(), 16) % 100
        cumulative = 0.0
        for version, fraction in self.AB_SPLIT.items():
            cumulative += fraction * 100.0
            if bucket < cumulative:
                return version
        return "v1"


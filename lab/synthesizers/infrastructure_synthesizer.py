from __future__ import annotations

import json

from .base_synthesizer import BaseSynthesizer


class InfrastructureSynthesizer(BaseSynthesizer):
    def submission_id(self) -> str:
        return f"SUB-{self.faker.random_int(min=1000, max=9999)}"

    def correlation_id(self) -> str:
        return self.faker.uuid4().replace("-", "")

    def pii_payload_json(self, submission_id: str) -> str:
        payload = {
            "email": "user@example.com",
            "phone": "(555) 123-4567",
            "submissionId": submission_id,
        }
        return json.dumps(payload)

    def event_payload(self, submission_id: str) -> dict:
        return {"submissionId": submission_id, "message": self.faker.sentence(nb_words=6)}

    def idempotency_key(self, submission_id: str, correlation_id: str) -> str:
        return f"canvas1:{submission_id}:{correlation_id}"

    def blob_path(self, submission_id: str) -> str:
        return f"inputs/{submission_id}.bin"

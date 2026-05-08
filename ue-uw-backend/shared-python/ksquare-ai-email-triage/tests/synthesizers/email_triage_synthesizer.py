from __future__ import annotations

from faker import Faker

from contracts import EmailTriageRequest


class EmailTriageSynthesizer:
    def __init__(self, seed: int) -> None:
        self._faker = Faker()
        self._faker.seed_instance(seed)

    def request(self, body_text: str | None = None) -> EmailTriageRequest:
        return EmailTriageRequest(
            email_id=self._faker.uuid4(),
            subject=self._faker.sentence(nb_words=6),
            body_text=body_text if body_text is not None else self._faker.paragraph(nb_sentences=5),
            sender_email=self._faker.email(),
            sender_name=self._faker.name(),
            received_at=self._faker.iso8601(),
            attachment_names=[],
            correlation_id=self._faker.uuid4(),
        )


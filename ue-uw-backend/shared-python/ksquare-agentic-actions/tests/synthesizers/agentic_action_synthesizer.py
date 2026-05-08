from __future__ import annotations

from faker import Faker


class AgenticActionSynthesizer:
    def __init__(self, seed: int) -> None:
        self._faker = Faker()
        self._faker.seed_instance(seed)

    def submission_id(self) -> str:
        return f"SUB-{self._faker.random_int(min=1000, max=9999)}"

    def referral_reason(self) -> str:
        return self._faker.sentence(nb_words=10)

    def broker_email(self) -> str:
        return self._faker.email()

    def requested_items(self, count: int = 3) -> list[str]:
        return [self._faker.sentence(nb_words=4).strip(".") for _ in range(count)]

    def field_updates(self, count: int = 2) -> list[dict]:
        updates = []
        for _ in range(count):
            updates.append(
                {
                    "field_name": self._faker.word(),
                    "new_value": self._faker.word(),
                    "reason": self._faker.sentence(nb_words=6),
                }
            )
        return updates

    def checklist_updates(self, count: int = 2) -> list[dict]:
        statuses = ["Complete", "NotApplicable", "NeedsReview"]
        updates = []
        for _ in range(count):
            updates.append(
                {
                    "item_id": self._faker.uuid4(),
                    "status": self._faker.random_element(elements=statuses),
                    "note": self._faker.sentence(nb_words=6),
                }
            )
        return updates

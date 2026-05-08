from __future__ import annotations

from faker import Faker

from contracts import PrefillRequest, UnmappedField


class PrefillSynthesizer:
    def __init__(self, seed: int) -> None:
        self._faker = Faker()
        self._faker.seed_instance(seed)

    def unmapped_fields(self, count: int) -> list[UnmappedField]:
        expected_types = ["string", "integer", "decimal", "date", "boolean"]
        fields: list[UnmappedField] = []
        for i in range(count):
            canonical = f"{self._faker.word()}_{i}".lower()
            fields.append(
                UnmappedField(
                    canonical_field=canonical,
                    display_label=self._faker.sentence(nb_words=3).strip("."),
                    expected_type=self._faker.random_element(elements=expected_types),
                    description=self._faker.sentence(nb_words=8),
                )
            )
        return fields

    def request(
        self,
        unmapped_fields: list[UnmappedField] | None = None,
        document_text: str | None = None,
        document_type: str | None = None,
    ) -> PrefillRequest:
        return PrefillRequest(
            document_id=self._faker.uuid4(),
            document_text=document_text if document_text is not None else self._faker.text(max_nb_chars=2000),
            document_type=document_type if document_type is not None else "ApplicationForm",
            unmapped_fields=unmapped_fields if unmapped_fields is not None else self.unmapped_fields(5),
            correlation_id=self._faker.uuid4(),
        )

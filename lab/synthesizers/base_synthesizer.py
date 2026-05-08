from __future__ import annotations

from faker import Faker


class BaseSynthesizer:
    def __init__(self, seed: int) -> None:
        self.faker = Faker()
        self.faker.seed_instance(seed)

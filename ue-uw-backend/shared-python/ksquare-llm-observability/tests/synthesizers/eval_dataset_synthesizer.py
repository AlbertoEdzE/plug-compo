from __future__ import annotations

import json
from datetime import datetime, timedelta
from typing import Any

from faker import Faker

from ksquare.llm_observability.models import EvaluationDataset, EvaluationRow


class EvalDatasetSynthesizer:
    def __init__(self, seed: int = 1337) -> None:
        self._faker = Faker()
        Faker.seed(seed)

    def dataset(self, *, size: int = 10, name: str = "synth") -> EvaluationDataset:
        rows: list[EvaluationRow] = []
        for i in range(size):
            question = f"What is the loss ratio for {2025 - (i % 3)}?"
            answer = f"From the loss run: {2025 - (i % 3)} loss ratio was 0.20."
            contexts = [f"Loss ratio 0.20 for {2025 - (i % 3)}", self._faker.sentence()]
            rows.append(EvaluationRow(question=question, answer=answer, contexts=contexts, ground_truth="loss ratio 0.20"))
        return EvaluationDataset(name=name, rows=rows)

    def conversation_turn_json(self) -> str:
        messages = [
            {"role": "user", "content": "What is the loss ratio?"},
            {"role": "tool", "content": "{\"year\": 2025, \"loss_ratio\": 0.20}"},
            {"role": "assistant", "content": "From the loss run: 2025 loss ratio was 0.20."},
        ]
        return json.dumps(messages)

    def created_at(self, days_ago: int = 0) -> str:
        return (datetime.utcnow() - timedelta(days=days_ago)).isoformat()


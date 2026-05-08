from __future__ import annotations

import re

from ..contracts import IEvaluationScorer
from ..models import EvaluationScores


class OnlineEvaluationScorer(IEvaluationScorer):
    async def score_async(self, question: str, answer: str, context: str, retrieved_docs: list[str]) -> EvaluationScores:
        scores = EvaluationScores()
        scores.groundedness = self._score_groundedness(answer, context)
        scores.answer_relevance = self._score_answer_relevance(question, answer)
        if retrieved_docs:
            scores.context_relevance = self._score_context_relevance(question, retrieved_docs)
        return scores

    @staticmethod
    def _score_groundedness(answer: str, context: str) -> float:
        context_lower = (context or "").lower()
        answer_lower = (answer or "").lower()

        answer_numbers = set(re.findall(r"\b\d+(?:\.\d+)?\b", answer_lower))
        context_numbers = set(re.findall(r"\b\d+(?:\.\d+)?\b", context_lower))

        if answer_numbers and not answer_numbers.issubset(context_numbers):
            return 0.2

        answer_terms = {w for w in re.findall(r"[a-z]{4,}", answer_lower)}
        context_terms = {w for w in re.findall(r"[a-z]{4,}", context_lower)}
        if not answer_terms:
            return 0.5

        overlap = len(answer_terms & context_terms) / max(len(answer_terms), 1)
        return 0.6 + (0.4 * min(overlap, 1.0))

    @staticmethod
    def _score_answer_relevance(question: str, answer: str) -> float:
        stop = {"the", "a", "is", "what", "how", "why", "when", "and", "or", "to", "of"}
        question_words = {w for w in (question or "").lower().split() if w not in stop}
        if not question_words:
            return 0.5
        answer_lower = (answer or "").lower()
        matched = sum(1 for w in question_words if w in answer_lower)
        return min(matched / max(len(question_words), 1), 1.0)

    @staticmethod
    def _score_context_relevance(question: str, docs: list[str]) -> float:
        stop = {"the", "a", "is", "what", "how", "why", "when", "and", "or", "to", "of"}
        question_words = {w for w in (question or "").lower().split() if w not in stop}
        if not question_words:
            return 0.0
        relevant = sum(1 for doc in docs if any(w in (doc or "").lower() for w in question_words))
        return relevant / len(docs) if docs else 0.0

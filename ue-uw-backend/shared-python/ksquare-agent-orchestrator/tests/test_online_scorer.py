import pytest

from ksquare.agent_orchestrator.evaluation.online_scorer import OnlineEvaluationScorer


@pytest.mark.asyncio
async def test_online_scorer_groundedness_supported_is_high():
    scorer = OnlineEvaluationScorer()
    context = "Status: InReview\nLoss ratio 0.20 for 2025"
    answer = "From the loss run: 2025 loss ratio was 0.20."
    scores = await scorer.score_async("what is the loss ratio?", answer, context, retrieved_docs=[])
    assert scores.groundedness is not None
    assert scores.groundedness > 0.8


@pytest.mark.asyncio
async def test_online_scorer_groundedness_contradiction_is_low():
    scorer = OnlineEvaluationScorer()
    context = "Status: InReview\nLoss ratio 0.20 for 2025"
    answer = "From the loss run: 2025 loss ratio was 0.95."
    scores = await scorer.score_async("what is the loss ratio?", answer, context, retrieved_docs=[])
    assert scores.groundedness is not None
    assert scores.groundedness < 0.5


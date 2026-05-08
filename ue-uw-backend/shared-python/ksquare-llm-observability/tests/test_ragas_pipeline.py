import pytest

from ksquare.llm_observability.config import LlmObservabilityConfig
from ksquare.llm_observability.db.obs_db_context import ObsDbContext
from ksquare.llm_observability.evaluation.ragas_pipeline import RagasEvaluationPipeline
from tests.synthesizers.eval_dataset_synthesizer import EvalDatasetSynthesizer


@pytest.mark.asyncio
async def test_ragas_pipeline_produces_metrics_and_persists_run(tmp_path):
    db = ObsDbContext(str(tmp_path / "obs.sqlite3"))
    cfg = LlmObservabilityConfig(connection_string=str(tmp_path / "obs.sqlite3"))
    pipe = RagasEvaluationPipeline(db, cfg)

    dataset = EvalDatasetSynthesizer().dataset(size=10)
    run = await pipe.run_offline_evaluation_async(dataset, run_name="nightly")

    assert run.dataset_size == 10
    assert 0.0 <= run.groundedness <= 1.0
    assert 0.0 <= run.faithfulness <= 1.0
    assert 0.0 <= run.answer_relevance <= 1.0
    assert 0.0 <= run.context_precision <= 1.0
    assert 0.0 <= run.context_recall <= 1.0

    latest = await db.get_latest_evaluation_run_async()
    assert latest is not None
    assert latest.run_id == run.run_id


@pytest.mark.asyncio
async def test_regression_detector_flags_drop_over_threshold(tmp_path):
    path = str(tmp_path / "obs.sqlite3")
    db = ObsDbContext(path)
    cfg = LlmObservabilityConfig(connection_string=path, regression_threshold=0.05)
    pipe = RagasEvaluationPipeline(db, cfg)

    synth = EvalDatasetSynthesizer()
    good = synth.dataset(size=10, name="good")
    await pipe.run_offline_evaluation_async(good, run_name="baseline")

    bad_rows = []
    for r in good.rows:
        bad_rows.append(type(r)(question=r.question, answer="The loss ratio was 0.95.", contexts=["Loss ratio 0.20 for 2025"], ground_truth=r.ground_truth))
    bad = type(good)(name="bad", rows=bad_rows)

    run2 = await pipe.run_offline_evaluation_async(bad, run_name="candidate")
    assert run2.vs_baseline is not None
    assert run2.has_regression is True


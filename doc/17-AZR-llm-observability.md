# Component 17 — LLM Observability & Evaluation Platform

**Library**: `KSquare.LlmObservability`  
**Layer**: Platform Infrastructure — AI Observability  
**Language**: Python 3.11 (evaluation runners) + C# .NET 8 (metrics exporter, dashboards hook)  
**Depends On**: Component 13 (AgentOrchestrator — produces traces and audit records)

---

## Why This Is a Pluggable Component

The AG UI assistant generates LLM calls, tool executions, and evaluation scores in production.
Without dedicated observability infrastructure, the AI Engineer has no way to:

- Know if the model is hallucinating (groundedness degrading over time)
- Know what the daily/monthly Azure OpenAI cost is
- Detect prompt regressions after system prompt updates
- Build a golden test dataset for offline evaluation
- Run RAGAS evaluation against a test set before deploying prompt changes
- Alert when P95 latency exceeds SLA
- Know what percentage of users find responses helpful (feedback rate)
- Satisfy audit requirements by proving the assistant did not say harmful things

This component provides a dedicated evaluation and observability runtime that is separate from
the orchestrator (which just emits traces). It consumes those traces and augments them with
offline evaluation, dashboarding, cost analysis, and alerting.

---

## Architecture Overview

```
KSquare.AgentOrchestrator (Component 13)
    │ OpenTelemetry spans (gen_ai.*)
    │ Conversation audit turns (SQL)
    │ Evaluation scores per turn (SQL)
    │
    ▼
KSquare.LlmObservability
    │
    ├─► Azure Monitor / Application Insights
    │       - LLM call spans (latency, tokens, cost, model)
    │       - Tool call spans (name, duration, success)
    │       - Custom metrics: groundedness, cost_per_day, error_rate
    │       - Alerts: P95 latency > 8s, daily cost > $200, groundedness < 0.70
    │
    ├─► RAGAS Offline Evaluation Pipeline (nightly batch)
    │       - Load evaluation dataset from conversation audit table
    │       - Compute: groundedness, faithfulness, context_precision, context_recall, answer_relevance
    │       - Compare against previous run — detect regression
    │       - Write results to evaluation_runs table
    │       - Send alert if any metric drops > 0.05 from baseline
    │
    ├─► LangSmith (optional — requires LANGSMITH_API_KEY)
    │       - Export all traces to LangSmith for visual inspection
    │       - Tag with submission_id, user_role, prompt_version
    │       - Enable replay for debugging specific bad responses
    │
    ├─► Cost Tracker
    │       - Aggregate estimated_cost_usd per day/user/submission
    │       - Budget alerts: daily, weekly, monthly
    │       - Cost breakdown: prompt tokens vs completion tokens
    │
    └─► Quality Dashboard (REST API for internal monitoring UI)
            - GET /api/llm-obs/summary?from=7d
            - GET /api/llm-obs/evals/latest
            - GET /api/llm-obs/costs/daily
            - GET /api/llm-obs/feedback/summary
```

---

## Interface Contracts

```python
# ksquare/llm_observability/contracts.py

from abc import ABC, abstractmethod
from typing import Optional

class IEvaluationPipeline(ABC):
    @abstractmethod
    async def run_offline_evaluation_async(
        self,
        dataset: "EvaluationDataset",
        run_name: Optional[str] = None
    ) -> "EvaluationRunResult":
        """
        Run RAGAS evaluation metrics against a labelled dataset.
        Called nightly by the background scheduler.
        """
        ...

class ICostTracker(ABC):
    @abstractmethod
    async def get_daily_cost_async(self, date: "datetime.date") -> "CostSummary": ...
    @abstractmethod
    async def get_period_cost_async(self, from_date: "datetime.date", to_date: "datetime.date") -> "CostSummary": ...

class IObservabilityExporter(ABC):
    @abstractmethod
    def export_to_langsmith(self, trace: "LlmTrace") -> None: ...
    @abstractmethod
    def export_to_app_insights(self, metrics: "LlmMetricsBatch") -> None: ...
```

---

## Models

```python
# ksquare/llm_observability/models.py

from dataclasses import dataclass, field
from typing import Optional
from datetime import datetime, date

@dataclass
class EvaluationDataset:
    name: str
    rows: list["EvaluationRow"]
    created_at: datetime = field(default_factory=datetime.utcnow)

@dataclass
class EvaluationRow:
    question: str                      # the user's question
    answer: str                        # the assistant's response
    contexts: list[str]                # retrieved document chunks (RAG context)
    ground_truth: Optional[str] = None # reference answer (from human labelling)
    session_id: Optional[str] = None
    submission_id: Optional[str] = None
    prompt_version: Optional[str] = None

@dataclass
class EvaluationRunResult:
    run_id: str
    run_name: str
    dataset_size: int
    # RAGAS metrics (averaged across all rows)
    groundedness: float              # 0-1: answer supported by context
    faithfulness: float              # 0-1: no hallucinated claims
    answer_relevance: float          # 0-1: answer addresses question
    context_precision: float         # 0-1: retrieved contexts relevant to question
    context_recall: float            # 0-1: all relevant info retrieved (needs ground_truth)
    # Regression detection
    vs_baseline: Optional["MetricDelta"] = None
    has_regression: bool = False
    created_at: datetime = field(default_factory=datetime.utcnow)

@dataclass
class MetricDelta:
    groundedness_delta: float
    faithfulness_delta: float
    answer_relevance_delta: float
    context_precision_delta: float

@dataclass
class CostSummary:
    period_start: date
    period_end: date
    total_usd: float
    prompt_tokens: int
    completion_tokens: int
    total_tokens: int
    requests: int
    cost_by_model: dict                # {"gpt-4.1": 12.50, "gpt-4o-mini": 0.30}
    cost_by_user: dict                 # top 10 users
    average_cost_per_request: float

@dataclass
class LlmMetricsBatch:
    period_start: datetime
    period_end: datetime
    # Latency
    p50_latency_ms: float
    p95_latency_ms: float
    p99_latency_ms: float
    # Quality
    avg_groundedness: float
    avg_answer_relevance: float
    avg_context_relevance: float
    # Feedback
    positive_feedback_rate: float
    negative_feedback_rate: float
    total_feedback_count: int
    # Safety
    safety_block_rate: float
    out_of_scope_rate: float
    # Volume + cost
    total_requests: int
    total_cost_usd: float
    error_rate: float
```

---

## RAGAS Offline Evaluation Pipeline

```python
# ksquare/llm_observability/evaluation/ragas_pipeline.py
# Uses the RAGAS library (https://docs.ragas.io)

from ragas import evaluate
from ragas.metrics import (
    faithfulness,
    answer_relevancy,
    context_precision,
    context_recall,
    context_utilization
)
from datasets import Dataset

class RagasEvaluationPipeline(IEvaluationPipeline):

    def __init__(self, llm_client, embedding_client, db, config):
        self._llm = llm_client
        self._embed = embedding_client
        self._db = db
        self._config = config

    async def run_offline_evaluation_async(
        self,
        dataset: EvaluationDataset,
        run_name: Optional[str] = None
    ) -> EvaluationRunResult:

        # Convert to RAGAS Dataset format
        ragas_data = {
            "question": [r.question for r in dataset.rows],
            "answer": [r.answer for r in dataset.rows],
            "contexts": [r.contexts for r in dataset.rows],
            "ground_truth": [r.ground_truth or "" for r in dataset.rows],
        }
        hf_dataset = Dataset.from_dict(ragas_data)

        # Run RAGAS evaluation
        # RAGAS internally makes LLM calls using the provided llm_client (gpt-4o-mini to minimize cost)
        result = evaluate(
            dataset=hf_dataset,
            metrics=[
                faithfulness,
                answer_relevancy,
                context_precision,
                context_recall,
                context_utilization,
            ],
            llm=self._llm,
            embeddings=self._embed,
            raise_exceptions=False
        )

        run_result = EvaluationRunResult(
            run_id=str(uuid.uuid4()),
            run_name=run_name or f"eval_{datetime.utcnow().strftime('%Y%m%d_%H%M%S')}",
            dataset_size=len(dataset.rows),
            groundedness=result["faithfulness"],       # RAGAS calls this "faithfulness"
            faithfulness=result["faithfulness"],
            answer_relevance=result["answer_relevancy"],
            context_precision=result["context_precision"],
            context_recall=result["context_recall"]
        )

        # Compare against baseline (last stored run)
        baseline = await self._get_baseline_async()
        if baseline:
            run_result.vs_baseline = MetricDelta(
                groundedness_delta=run_result.groundedness - baseline.groundedness,
                faithfulness_delta=run_result.faithfulness - baseline.faithfulness,
                answer_relevance_delta=run_result.answer_relevance - baseline.answer_relevance,
                context_precision_delta=run_result.context_precision - baseline.context_precision
            )
            # Regression: any metric drops more than 5 points
            run_result.has_regression = any([
                run_result.vs_baseline.groundedness_delta < -0.05,
                run_result.vs_baseline.faithfulness_delta < -0.05,
                run_result.vs_baseline.answer_relevance_delta < -0.05,
            ])

        # Persist run result
        await self._save_run_async(run_result)

        # Alert on regression
        if run_result.has_regression:
            await self._alert_on_regression(run_result)

        return run_result
```

---

## Building Evaluation Datasets

```python
# ksquare/llm_observability/dataset/dataset_builder.py

class EvaluationDatasetBuilder:
    """
    Builds evaluation datasets from two sources:
    1. Positive-feedback turns from conversation audit (automatic)
    2. Manually labelled golden set (uploaded via CLI)
    """

    async def build_from_feedback_async(
        self,
        min_date: datetime,
        min_feedback_count: int = 50
    ) -> EvaluationDataset:
        """
        Extract turns with positive feedback as a proxy for correct responses.
        These become the evaluation dataset for RAGAS.
        """
        rows_db = await self._db.fetch_all("""
            SELECT
                t.turn_id,
                t.content_redacted,
                t.session_id,
                t.submission_id,
                t.feedback_rating,
                t.eval_groundedness,
                t.eval_answer_relevance
            FROM agent_conversation_turns t
            WHERE t.feedback_rating = 'positive'
              AND t.role = 'assistant'
              AND t.created_at >= @min_date
            ORDER BY t.created_at DESC
            LIMIT @limit
        """, min_date=min_date, limit=min_feedback_count * 3)

        rows = []
        for db_row in rows_db:
            content = json.loads(db_row["content_redacted"])
            # Extract question (last user turn) and answer (assistant turn)
            user_turn = next((m for m in reversed(content) if m["role"] == "user"), None)
            asst_turn = next((m for m in reversed(content) if m["role"] == "assistant"), None)
            tool_contexts = [m["content"] for m in content if m["role"] == "tool"]

            if user_turn and asst_turn:
                rows.append(EvaluationRow(
                    question=user_turn["content"],
                    answer=asst_turn["content"],
                    contexts=tool_contexts,
                    session_id=db_row["session_id"],
                    submission_id=db_row["submission_id"]
                ))

        return EvaluationDataset(
            name=f"feedback_dataset_{datetime.utcnow().strftime('%Y%m%d')}",
            rows=rows[:min_feedback_count]
        )

    async def load_golden_set_async(self, blob_path: str) -> EvaluationDataset:
        """
        Load a manually-curated golden set from blob storage.
        Format: JSONL with {question, answer, contexts, ground_truth} per line.
        """
        content = await self._blob_storage.download_async("eval-datasets", blob_path)
        rows = [EvaluationRow(**json.loads(line)) for line in content.decode().splitlines()]
        return EvaluationDataset(name=blob_path, rows=rows)
```

---

## OpenTelemetry → Azure Monitor Export

```python
# ksquare/llm_observability/exporters/azure_monitor_exporter.py
# Configure OpenTelemetry SDK to export LLM spans to Azure Application Insights

from azure.monitor.opentelemetry import configure_azure_monitor
from opentelemetry import trace, metrics
from opentelemetry.sdk.metrics import MeterProvider

def configure_llm_observability(
    app_insights_connection_string: str,
    service_name: str = "ksquare-agent-orchestrator"
):
    # Configure Azure Monitor exporter — captures all OTel spans + metrics
    configure_azure_monitor(
        connection_string=app_insights_connection_string,
        service_name=service_name
    )

    # Custom meter for LLM-specific metrics
    meter = metrics.get_meter("ksquare.llm_observability", "1.0.0")

    # Instruments — these are auto-updated by the orchestrator
    return LlmMetricsInstruments(
        token_counter    = meter.create_counter("gen_ai.tokens.total", unit="tokens", description="Total LLM tokens used"),
        cost_counter     = meter.create_counter("gen_ai.cost.usd", unit="USD", description="Estimated LLM cost"),
        latency_hist     = meter.create_histogram("gen_ai.latency_ms", unit="ms", description="LLM call latency"),
        groundedness_hist = meter.create_histogram("gen_ai.eval.groundedness", description="Groundedness score 0-1"),
        tool_success     = meter.create_counter("gen_ai.tool.success", description="Successful tool calls"),
        tool_failure     = meter.create_counter("gen_ai.tool.failure", description="Failed tool calls"),
        safety_block     = meter.create_counter("gen_ai.safety.blocked", description="Safety-blocked requests"),
    )
```

---

## LangSmith Integration (Optional)

```python
# ksquare/llm_observability/exporters/langsmith_exporter.py

from langsmith import Client
import os

class LangSmithExporter:
    """
    Export LLM traces to LangSmith for visual inspection and debugging.
    Only active when LANGSMITH_API_KEY is set in environment.
    """

    def __init__(self, api_key: str, project_name: str = "ue-uw-ag-ui"):
        self._client = Client(api_key=api_key)
        self._project = project_name

    def export_trace(
        self,
        trace_id: str,
        submission_id: str,
        messages: list[dict],
        response: str,
        metadata: dict
    ):
        """Export a single conversation trace to LangSmith."""
        self._client.create_run(
            project_name=self._project,
            name="ag_ui_chat",
            run_type="chain",
            inputs={"messages": messages, "submission_id": submission_id},
            outputs={"response": response},
            extra={
                "metadata": {
                    "submission_id": submission_id,
                    "user_role": metadata.get("user_role"),
                    "prompt_version": metadata.get("prompt_version"),
                    "trace_id": trace_id,
                    **metadata
                }
            }
        )
```

---

## Alert Rules

```yaml
# azure-monitor-alert-rules.yml
# Deploy to Azure Monitor via Bicep/Terraform

alerts:
  - name: LLM_HighLatency_P95
    description: "AG UI P95 latency exceeded 8 seconds"
    metric: gen_ai.latency_ms
    aggregation: percentile_95
    threshold: 8000
    window: 5m
    severity: 2  # Warning

  - name: LLM_DailyCostExceeded
    description: "Daily LLM cost exceeded $200"
    metric: gen_ai.cost.usd
    aggregation: sum
    threshold: 200
    window: 1d
    severity: 1  # Critical

  - name: LLM_GroundednessRegression
    description: "Average groundedness score dropped below 0.70"
    metric: gen_ai.eval.groundedness
    aggregation: avg
    threshold: 0.70
    window: 1h
    direction: below
    severity: 2

  - name: LLM_HighSafetyBlockRate
    description: "Safety block rate exceeded 10%"
    metric: gen_ai.safety.blocked
    comparison_metric: gen_ai.requests.total
    threshold_ratio: 0.10
    window: 1h
    severity: 2

  - name: LLM_EvaluationRegression
    description: "RAGAS evaluation detected regression (any metric dropped > 0.05)"
    source: evaluation_runs table
    trigger: has_regression = true
    severity: 1
    notify: ai-engineer@company.com, uw-platform@company.com
```

---

## Quality Dashboard API

```python
# REST API for internal observability dashboard (served from the monitoring Azure Function)

GET /api/llm-obs/summary?from=7d
Response: {
  "period": "7d",
  "totalRequests": 1842,
  "avgGroundedness": 0.87,
  "avgAnswerRelevance": 0.83,
  "positiveFeedbackRate": 0.79,
  "p50LatencyMs": 1240,
  "p95LatencyMs": 4800,
  "totalCostUsd": 48.20,
  "safetyBlockRate": 0.008,
  "errorRate": 0.003
}

GET /api/llm-obs/evals/latest
Response: {
  "runId": "eval_20260501_020000",
  "runName": "nightly_eval_2026-05-01",
  "datasetSize": 150,
  "groundedness": 0.88,
  "faithfulness": 0.91,
  "answerRelevance": 0.85,
  "contextPrecision": 0.82,
  "contextRecall": 0.79,
  "hasRegression": false,
  "vsBaseline": {
    "groundednessDelta": +0.02,
    "faithfulnessDelta": +0.01
  }
}

GET /api/llm-obs/costs/daily?days=30
Response: {
  "dailyCosts": [
    { "date": "2026-05-01", "totalUsd": 6.82, "requests": 248, "tokens": 420000 },
    ...
  ],
  "totalUsd": 48.20,
  "avgDailyCost": 1.61
}

GET /api/llm-obs/feedback/summary?from=30d
Response: {
  "totalFeedback": 312,
  "positive": 249,
  "negative": 63,
  "positiveRate": 0.799,
  "topNegativeTopics": ["loss ratio calculation", "document excerpt not found"]
}
```

---

## SQL Schema

```sql
CREATE TABLE evaluation_runs (
    run_id          NVARCHAR(64) NOT NULL PRIMARY KEY,
    run_name        NVARCHAR(200) NOT NULL,
    dataset_size    INT NOT NULL,
    groundedness    FLOAT NOT NULL,
    faithfulness    FLOAT NOT NULL,
    answer_relevance FLOAT NOT NULL,
    context_precision FLOAT NOT NULL,
    context_recall  FLOAT NOT NULL,
    has_regression  BIT NOT NULL DEFAULT 0,
    vs_baseline_json NVARCHAR(MAX) NULL,    -- JSON MetricDelta
    created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    INDEX IX_eval_created (created_at DESC)
);

CREATE TABLE llm_cost_daily (
    cost_date       DATE NOT NULL PRIMARY KEY,
    total_usd       FLOAT NOT NULL,
    prompt_tokens   INT NOT NULL,
    completion_tokens INT NOT NULL,
    request_count   INT NOT NULL,
    model_breakdown_json NVARCHAR(MAX) NULL,
    updated_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
```

---

## Configuration

```python
@dataclass
class LlmObservabilityConfig:
    # Azure Monitor
    app_insights_connection_string: str = ""
    enable_azure_monitor: bool = True

    # LangSmith (optional)
    langsmith_api_key: Optional[str] = None
    langsmith_project: str = "ue-uw-ag-ui"
    enable_langsmith: bool = False       # enable when LANGSMITH_API_KEY is set

    # RAGAS evaluation
    enable_offline_evaluation: bool = True
    evaluation_schedule_cron: str = "0 2 * * *"   # 2 AM UTC nightly
    evaluation_dataset_min_size: int = 50
    ragas_judge_model: str = "gpt-4o-mini"         # low-cost model for RAGAS LLM calls
    regression_threshold: float = 0.05             # alert if metric drops > 5 points

    # Cost alerts
    daily_cost_alert_usd: float = 200.0
    monthly_cost_budget_usd: float = 2000.0

    # DB
    connection_string: str = ""
```

---

## Claude Code Build Prompt

```
Build a Python package called ksquare-llm-observability at path: shared/ksquare-llm-observability/

This package provides the evaluation and observability layer for the AG UI assistant.
It runs RAGAS offline evaluation nightly, exports traces to Azure Monitor + optional LangSmith,
computes daily cost summaries, and provides a REST API for an internal quality dashboard.

Package structure:
  shared/ksquare-llm-observability/
  ├── pyproject.toml
  ├── ksquare/
  │   └── llm_observability/
  │       ├── __init__.py
  │       ├── contracts.py              ← IEvaluationPipeline, ICostTracker, IObservabilityExporter
  │       ├── models.py                 ← all dataclasses
  │       ├── config.py                 ← LlmObservabilityConfig
  │       ├── evaluation/
  │       │   ├── ragas_pipeline.py     ← RagasEvaluationPipeline (full RAGAS run)
  │       │   └── regression_detector.py ← compare run vs baseline, flag regression
  │       ├── dataset/
  │       │   └── dataset_builder.py    ← build from feedback turns or golden set JSONL
  │       ├── exporters/
  │       │   ├── azure_monitor_exporter.py  ← configure_azure_monitor + LlmMetricsInstruments
  │       │   └── langsmith_exporter.py      ← LangSmith trace export (optional)
  │       ├── cost/
  │       │   └── cost_tracker.py       ← aggregate estimated_cost_usd from agent_conversation_turns
  │       ├── api/
  │       │   └── dashboard_api.py      ← FastAPI/Azure Function endpoints for dashboard
  │       └── db/
  │           └── obs_db_context.py     ← evaluation_runs + llm_cost_daily tables
  └── tests/
      ├── test_ragas_pipeline.py
      ├── test_dataset_builder.py
      ├── test_cost_tracker.py
      └── fixtures/
          └── sample_eval_dataset.jsonl    ← 10-row golden set for tests

RagasEvaluationPipeline:
  - Use ragas.evaluate() with metrics: faithfulness, answer_relevancy, context_precision, context_recall
  - Pass gpt-4o-mini as the judge LLM (low cost)
  - Store EvaluationRunResult to evaluation_runs table
  - Compare vs last run: compute MetricDelta; set has_regression=True if any delta < -0.05
  - On regression: log critical + send notification via KSquare.Notifications if configured

DatasetBuilder.build_from_feedback_async:
  - Query agent_conversation_turns WHERE feedback_rating = 'positive'
  - Parse content_redacted JSON to extract question (last user message) + answer (assistant message) + tool contexts
  - Return EvaluationDataset

CostTracker:
  - Daily aggregation job: GROUP BY DATE(created_at) SUM(estimated_cost_usd), SUM(prompt_tokens), etc.
  - UPSERT into llm_cost_daily
  - get_daily_cost_async and get_period_cost_async read from llm_cost_daily
  - Alert if daily total > config.daily_cost_alert_usd

DashboardAPI (FastAPI app for Azure Function):
  - GET /summary → LlmMetricsBatch for last N days
  - GET /evals/latest → latest EvaluationRunResult
  - GET /costs/daily → list of llm_cost_daily rows
  - GET /feedback/summary → positive/negative counts + rate

pyproject.toml dependencies:
  ragas>=0.1.0
  openai>=1.30
  azure-monitor-opentelemetry>=1.0
  langsmith>=0.1.0
  fastapi>=0.110
  datasets>=2.0
  pydantic>=2.0
  aiosqlite>=0.19

Tests:
  - RagasPipeline computes groundedness and faithfulness for fixture dataset
  - RegressionDetector flags has_regression when groundedness drops 0.10
  - DatasetBuilder extracts question + answer from conversation turn JSON correctly
  - CostTracker aggregates cost correctly from multiple turns
  - DashboardAPI /summary returns correct positive_feedback_rate
  Use pytest + pytest-asyncio + httpx (for FastAPI test client).
```

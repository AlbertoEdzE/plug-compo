from __future__ import annotations

import time
from contextlib import contextmanager


class LlmTracer:
    def __init__(self) -> None:
        self._enabled = False
        try:
            from opentelemetry import trace
            from opentelemetry.trace import SpanKind

            self._trace = trace
            self._SpanKind = SpanKind
            self._tracer = trace.get_tracer("ksquare.agent_orchestrator", "1.0.0")
            self._enabled = True
        except Exception:
            self._trace = None
            self._SpanKind = None
            self._tracer = None

    @contextmanager
    def llm_span(self, model: str, operation: str = "chat"):
        if not self._enabled:
            yield None
            return

        with self._tracer.start_as_current_span(
            name=f"gen_ai.{operation}",
            kind=self._SpanKind.CLIENT,
        ) as span:
            span.set_attribute("gen_ai.system", "az.ai.openai")
            span.set_attribute("gen_ai.operation.name", operation)
            span.set_attribute("gen_ai.request.model", model)
            start_time = time.monotonic_ns()
            try:
                yield span
            except Exception as e:
                span.record_exception(e)
                span.set_status(self._trace.StatusCode.ERROR, str(e))
                raise
            finally:
                elapsed_ms = (time.monotonic_ns() - start_time) // 1_000_000
                span.set_attribute("gen_ai.latency_ms", elapsed_ms)

    def record_usage(self, span, prompt_tokens: int, completion_tokens: int, model: str) -> float:
        cost = self._estimate_cost(model, prompt_tokens, completion_tokens)
        if not self._enabled or span is None:
            return cost

        span.set_attribute("gen_ai.usage.input_tokens", prompt_tokens)
        span.set_attribute("gen_ai.usage.output_tokens", completion_tokens)
        span.set_attribute("gen_ai.usage.total_tokens", prompt_tokens + completion_tokens)
        span.set_attribute("gen_ai.usage.cost_usd", cost)
        return cost

    @staticmethod
    def _estimate_cost(model: str, prompt_tokens: int, completion_tokens: int) -> float:
        pricing = {
            "gpt-4.1": {"input": 2.00 / 1_000_000, "output": 8.00 / 1_000_000},
            "gpt-4o": {"input": 5.00 / 1_000_000, "output": 15.00 / 1_000_000},
            "gpt-4o-mini": {"input": 0.15 / 1_000_000, "output": 0.60 / 1_000_000},
        }
        rates = pricing.get(model, pricing["gpt-4.1"])
        return (prompt_tokens * rates["input"]) + (completion_tokens * rates["output"])


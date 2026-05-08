from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class LlmMetricsInstruments:
    token_counter: object
    cost_counter: object
    latency_hist: object
    groundedness_hist: object
    tool_success: object
    tool_failure: object
    safety_block: object


def configure_llm_observability(app_insights_connection_string: str, service_name: str = "ksquare-agent-orchestrator") -> LlmMetricsInstruments:
    try:
        from azure.monitor.opentelemetry import configure_azure_monitor
        from opentelemetry import metrics

        configure_azure_monitor(connection_string=app_insights_connection_string, service_name=service_name)
        meter = metrics.get_meter("ksquare.llm_observability", "1.0.0")

        return LlmMetricsInstruments(
            token_counter=meter.create_counter("gen_ai.tokens.total", unit="tokens"),
            cost_counter=meter.create_counter("gen_ai.cost.usd", unit="USD"),
            latency_hist=meter.create_histogram("gen_ai.latency_ms", unit="ms"),
            groundedness_hist=meter.create_histogram("gen_ai.eval.groundedness"),
            tool_success=meter.create_counter("gen_ai.tool.success"),
            tool_failure=meter.create_counter("gen_ai.tool.failure"),
            safety_block=meter.create_counter("gen_ai.safety.blocked"),
        )
    except Exception:
        no_op = object()
        return LlmMetricsInstruments(
            token_counter=no_op,
            cost_counter=no_op,
            latency_hist=no_op,
            groundedness_hist=no_op,
            tool_success=no_op,
            tool_failure=no_op,
            safety_block=no_op,
        )


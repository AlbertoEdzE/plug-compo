from __future__ import annotations


class LangSmithExporter:
    def __init__(self, api_key: str, project_name: str = "ue-uw-ag-ui") -> None:
        self._enabled = False
        self._project = project_name
        try:
            from langsmith import Client

            self._client = Client(api_key=api_key)
            self._enabled = True
        except Exception:
            self._client = None

    def export_trace(
        self,
        trace_id: str,
        submission_id: str,
        messages: list[dict],
        response: str,
        metadata: dict,
    ) -> None:
        if not self._enabled:
            return

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
                    **metadata,
                }
            },
        )


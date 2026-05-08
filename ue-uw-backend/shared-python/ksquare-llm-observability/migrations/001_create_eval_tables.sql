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
    vs_baseline_json NVARCHAR(MAX) NULL,
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


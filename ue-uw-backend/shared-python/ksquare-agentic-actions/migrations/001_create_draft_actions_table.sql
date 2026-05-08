CREATE TABLE agent_draft_actions (
    draft_id        NVARCHAR(64) NOT NULL PRIMARY KEY,
    action_type     NVARCHAR(50) NOT NULL,
    submission_id   NVARCHAR(64) NOT NULL,
    status          NVARCHAR(30) NOT NULL DEFAULT 'Pending',
    payload_json    NVARCHAR(MAX) NOT NULL,
    preview_title   NVARCHAR(100) NOT NULL,
    preview_detail  NVARCHAR(500) NOT NULL,
    created_by      NVARCHAR(200) NULL,
    created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    expires_at      DATETIMEOFFSET NOT NULL,
    executed_at     DATETIMEOFFSET NULL,
    execution_result_json NVARCHAR(MAX) NULL,
    INDEX IX_draft_submission (submission_id, status),
    INDEX IX_draft_expiry     (status, expires_at)
);

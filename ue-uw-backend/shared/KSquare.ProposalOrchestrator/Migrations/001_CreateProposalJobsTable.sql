CREATE TABLE proposal_generation_jobs (
    job_id              NVARCHAR(64) NOT NULL PRIMARY KEY,
    quote_id            NVARCHAR(64) NOT NULL,
    submission_id       NVARCHAR(64) NOT NULL,
    proposal_type       NVARCHAR(50) NOT NULL,
    provider            NVARCHAR(50) NOT NULL,
    provider_job_id     NVARCHAR(200) NULL,
    status              NVARCHAR(30) NOT NULL DEFAULT 'Pending',
    retry_count         INT NOT NULL DEFAULT 0,
    artifact_blob_path  NVARCHAR(1000) NULL,
    artifact_sas_url    NVARCHAR(2000) NULL,
    error_message       NVARCHAR(MAX) NULL,
    created_at          DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    completed_at        DATETIMEOFFSET NULL
);

CREATE INDEX IX_proposal_quote ON proposal_generation_jobs (quote_id);
CREATE INDEX IX_proposal_status ON proposal_generation_jobs (status, created_at);


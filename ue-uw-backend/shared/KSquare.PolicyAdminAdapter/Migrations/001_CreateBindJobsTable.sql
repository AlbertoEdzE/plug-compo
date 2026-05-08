IF OBJECT_ID('dbo.bind_jobs', 'U') IS NULL
BEGIN
    CREATE TABLE bind_jobs (
        bind_job_id             NVARCHAR(64) NOT NULL PRIMARY KEY,
        quote_id                NVARCHAR(64) NOT NULL,
        submission_id           NVARCHAR(64) NOT NULL,
        provider                NVARCHAR(50) NOT NULL,
        provider_transaction_id NVARCHAR(200) NULL,
        status                  NVARCHAR(30) NOT NULL DEFAULT 'Pending',
        policy_number           NVARCHAR(100) NULL,
        retry_count             INT NOT NULL DEFAULT 0,
        error_code              NVARCHAR(100) NULL,
        error_message           NVARCHAR(MAX) NULL,
        payload_json            NVARCHAR(MAX) NULL,
        created_at              DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        completed_at            DATETIMEOFFSET NULL
    );

    CREATE INDEX IX_bind_quote ON bind_jobs (quote_id);
    CREATE INDEX IX_bind_status ON bind_jobs (status, created_at);
END;


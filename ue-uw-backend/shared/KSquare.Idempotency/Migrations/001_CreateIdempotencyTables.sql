CREATE TABLE idempotency_keys (
    [key]           NVARCHAR(500) NOT NULL PRIMARY KEY,
    status_code     INT NOT NULL,
    response_body   NVARCHAR(MAX) NOT NULL,
    content_type    NVARCHAR(200) NOT NULL,
    processed_at    DATETIMEOFFSET NOT NULL,
    expires_at      DATETIMEOFFSET NOT NULL,
    INDEX IX_idempotency_expires (expires_at)
);

CREATE TABLE idempotency_consumer_keys (
    message_id      NVARCHAR(500) NOT NULL PRIMARY KEY,
    processed_at    DATETIMEOFFSET NOT NULL,
    expires_at      DATETIMEOFFSET NOT NULL,
    INDEX IX_consumer_expires (expires_at)
);

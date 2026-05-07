CREATE TABLE outbox_messages (
    id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    topic NVARCHAR(256) NOT NULL,
    event_type NVARCHAR(256) NOT NULL,
    payload NVARCHAR(MAX) NOT NULL,
    correlation_id NVARCHAR(128) NOT NULL,
    message_id NVARCHAR(128) NULL,
    properties NVARCHAR(MAX) NULL,
    status INT NOT NULL,
    retry_count INT NOT NULL,
    last_error NVARCHAR(MAX) NULL,
    created_at DATETIMEOFFSET NOT NULL,
    processed_at DATETIMEOFFSET NULL
);

CREATE INDEX IX_outbox_messages_status ON outbox_messages(status);
CREATE INDEX IX_outbox_messages_created_at ON outbox_messages(created_at);

CREATE TABLE audit_trail (
    entry_id        UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    resource_type   NVARCHAR(100) NOT NULL,
    resource_id     NVARCHAR(500) NOT NULL,
    action          NVARCHAR(200) NOT NULL,
    actor_user_id   NVARCHAR(500) NOT NULL,
    actor_name      NVARCHAR(500) NOT NULL,
    actor_role      NVARCHAR(200) NULL,
    actor_type      NVARCHAR(50) NOT NULL DEFAULT 'User',
    before_json     NVARCHAR(MAX) NULL,
    after_json      NVARCHAR(MAX) NULL,
    correlation_id  NVARCHAR(200) NULL,
    service_name    NVARCHAR(200) NULL,
    tags_json       NVARCHAR(MAX) NULL,
    occurred_at     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);

CREATE INDEX IX_audit_resource ON audit_trail (resource_type, resource_id, occurred_at DESC);
CREATE INDEX IX_audit_actor ON audit_trail (actor_user_id, occurred_at DESC);
CREATE INDEX IX_audit_occurred ON audit_trail (occurred_at DESC);

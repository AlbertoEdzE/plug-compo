CREATE TABLE in_app_notifications (
    notification_id   UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    user_id           NVARCHAR(500) NOT NULL,
    event_type        NVARCHAR(200) NOT NULL,
    title             NVARCHAR(500) NOT NULL,
    body              NVARCHAR(MAX) NOT NULL,
    action_url        NVARCHAR(1000) NULL,
    resource_type     NVARCHAR(100) NOT NULL,
    resource_id       NVARCHAR(500) NOT NULL,
    is_read           BIT NOT NULL DEFAULT 0,
    created_at        DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    read_at           DATETIMEOFFSET NULL
);

CREATE INDEX IX_notif_user_unread ON in_app_notifications (user_id, is_read, created_at DESC);
CREATE INDEX IX_notif_created ON in_app_notifications (created_at DESC);

CREATE TABLE notification_dedup (
    dedup_key       NVARCHAR(500) NOT NULL PRIMARY KEY,
    created_at      DATETIMEOFFSET NOT NULL,
    expires_at      DATETIMEOFFSET NOT NULL
);

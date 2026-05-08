IF OBJECT_ID('dbo.state_records', 'U') IS NULL
BEGIN
    CREATE TABLE state_records (
        entity_type     NVARCHAR(100) NOT NULL,
        entity_id       NVARCHAR(64)  NOT NULL,
        current_state   NVARCHAR(100) NOT NULL,
        version         INT NOT NULL DEFAULT 0,
        created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        updated_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        updated_by      NVARCHAR(200) NULL,
        CONSTRAINT PK_state_records PRIMARY KEY (entity_type, entity_id)
    );

    CREATE INDEX IX_state_records_type_state ON state_records (entity_type, current_state);
END;


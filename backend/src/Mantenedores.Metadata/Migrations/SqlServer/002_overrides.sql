-- Ajustes por tabla (P-011): un documento JSON por tabla con sus columnas (D-049).
CREATE TABLE mnt_table_override (
    connection_id nvarchar(50)   NOT NULL,
    schema_name   nvarchar(128)  NOT NULL,
    table_name    nvarchar(128)  NOT NULL,
    settings      nvarchar(max)  NOT NULL,
    updated_at    datetimeoffset NOT NULL,
    updated_by    nvarchar(200)  NULL,
    CONSTRAINT pk_mnt_table_override PRIMARY KEY (connection_id, schema_name, table_name)
);

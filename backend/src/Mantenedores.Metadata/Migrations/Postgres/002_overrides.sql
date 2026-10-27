-- Ajustes por tabla (P-011): un documento JSON por tabla con sus columnas (D-049).
CREATE TABLE mnt_table_override (
    connection_id varchar(50)  NOT NULL,
    schema_name   varchar(128) NOT NULL,
    table_name    varchar(128) NOT NULL,
    settings      text         NOT NULL,
    updated_at    timestamptz  NOT NULL,
    updated_by    varchar(200) NULL,
    PRIMARY KEY (connection_id, schema_name, table_name)
);

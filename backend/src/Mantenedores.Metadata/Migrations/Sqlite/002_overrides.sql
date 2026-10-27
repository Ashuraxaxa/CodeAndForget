-- Ajustes por tabla (P-011): un documento JSON por tabla con sus columnas (D-049).
CREATE TABLE mnt_table_override (
    connection_id VARCHAR(50)  NOT NULL,
    schema_name   VARCHAR(128) NOT NULL,
    table_name    VARCHAR(128) NOT NULL,
    settings      TEXT         NOT NULL,
    updated_at    VARCHAR(40)  NOT NULL,
    updated_by    VARCHAR(200) NULL,
    PRIMARY KEY (connection_id, schema_name, table_name)
);

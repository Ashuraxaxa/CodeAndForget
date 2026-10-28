-- Auditoría de cambios (P-044, D-065): una fila por alta, edición, baja, fila importada o cambio de vínculos N:M.
CREATE TABLE mnt_audit (
    id            INTEGER      NOT NULL PRIMARY KEY AUTOINCREMENT,
    at            VARCHAR(40)  NOT NULL,
    username      VARCHAR(200) NULL,
    connection_id VARCHAR(50)  NOT NULL,
    schema_name   VARCHAR(128) NOT NULL,
    table_name    VARCHAR(128) NOT NULL,
    operation     VARCHAR(10)  NOT NULL,
    row_key       VARCHAR(900) NULL,
    old_values    TEXT         NULL,
    new_values    TEXT         NULL
);
CREATE INDEX ix_mnt_audit_at ON mnt_audit (at);
CREATE INDEX ix_mnt_audit_row ON mnt_audit (connection_id, schema_name, table_name, row_key);

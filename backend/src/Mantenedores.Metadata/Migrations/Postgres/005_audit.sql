-- Auditoría de cambios (P-044, D-065): una fila por alta, edición, baja, fila importada o cambio de vínculos N:M.
CREATE TABLE mnt_audit (
    id            bigserial    NOT NULL PRIMARY KEY,
    at            timestamptz  NOT NULL,
    username      varchar(200) NULL,
    connection_id varchar(50)  NOT NULL,
    schema_name   varchar(128) NOT NULL,
    table_name    varchar(128) NOT NULL,
    operation     varchar(10)  NOT NULL,
    row_key       varchar(900) NULL,
    old_values    text         NULL,
    new_values    text         NULL
);
CREATE INDEX ix_mnt_audit_at ON mnt_audit (at);
CREATE INDEX ix_mnt_audit_row ON mnt_audit (connection_id, schema_name, table_name, row_key);

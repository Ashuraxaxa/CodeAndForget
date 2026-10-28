-- Auditoría de cambios (P-044, D-065): una fila por alta, edición, baja, fila importada o cambio de vínculos N:M.
CREATE TABLE mnt_audit (
    id            bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    at            datetimeoffset NOT NULL,
    username      nvarchar(200)  NULL,
    connection_id nvarchar(50)   NOT NULL,
    schema_name   nvarchar(128)  NOT NULL,
    table_name    nvarchar(128)  NOT NULL,
    operation     nvarchar(10)   NOT NULL,
    row_key       nvarchar(400)  NULL,
    old_values    nvarchar(max)  NULL,
    new_values    nvarchar(max)  NULL
);
CREATE INDEX ix_mnt_audit_at ON mnt_audit (at);
CREATE INDEX ix_mnt_audit_row ON mnt_audit (connection_id, schema_name, table_name, row_key);

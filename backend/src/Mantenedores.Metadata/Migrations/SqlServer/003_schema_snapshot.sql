-- Foto de referencia del esquema de cada conexión, para avisar cambios (P-013).
CREATE TABLE mnt_schema_snapshot (
    connection_id nvarchar(50)   NOT NULL PRIMARY KEY,
    snapshot      nvarchar(max)  NOT NULL,
    taken_at      datetimeoffset NOT NULL,
    taken_by      nvarchar(200)  NULL
);

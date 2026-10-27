-- Foto de referencia del esquema de cada conexión, para avisar cambios (P-013).
CREATE TABLE mnt_schema_snapshot (
    connection_id VARCHAR(50)  NOT NULL PRIMARY KEY,
    snapshot      TEXT         NOT NULL,
    taken_at      VARCHAR(40)  NOT NULL,
    taken_by      VARCHAR(200) NULL
);

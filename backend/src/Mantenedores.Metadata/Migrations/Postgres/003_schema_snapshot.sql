-- Foto de referencia del esquema de cada conexión, para avisar cambios (P-013).
CREATE TABLE mnt_schema_snapshot (
    connection_id varchar(50)  NOT NULL PRIMARY KEY,
    snapshot      text         NOT NULL,
    taken_at      timestamptz  NOT NULL,
    taken_by      varchar(200) NULL
);

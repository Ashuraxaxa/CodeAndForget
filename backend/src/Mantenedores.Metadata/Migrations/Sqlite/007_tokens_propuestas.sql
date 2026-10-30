-- IA por MCP (P-113, D-100): muestra de datos por conexión, tokens de acceso y propuestas de ajustes.
-- Fechas como texto ISO 8601 en UTC (se ordena igual que la fecha) y banderas como 0/1, igual en los tres motores.
ALTER TABLE mnt_connection ADD allow_samples INTEGER NOT NULL DEFAULT 0;

CREATE TABLE mnt_access_token (
    id           VARCHAR(40)  NOT NULL PRIMARY KEY,
    name         VARCHAR(100) NOT NULL,
    owner        VARCHAR(200) NOT NULL,
    roles        TEXT      NOT NULL,
    prefix       VARCHAR(20)  NOT NULL,
    token_hash   VARCHAR(64)  NOT NULL UNIQUE,
    created_at   VARCHAR(40)  NOT NULL,
    expires_at   VARCHAR(40)  NULL,
    last_used_at VARCHAR(40)  NULL
);

CREATE TABLE mnt_override_proposal (
    id            VARCHAR(40)  NOT NULL PRIMARY KEY,
    connection_id VARCHAR(50)  NOT NULL,
    schema_name   VARCHAR(128) NOT NULL,
    table_name    VARCHAR(128) NOT NULL,
    settings      TEXT      NOT NULL,
    reason        TEXT      NULL,
    created_by    VARCHAR(200) NULL,
    created_at    VARCHAR(40)  NOT NULL,
    status        VARCHAR(20)  NOT NULL,
    resolved_by   VARCHAR(200) NULL,
    resolved_at   VARCHAR(40)  NULL
);

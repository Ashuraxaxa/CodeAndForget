-- IA por MCP (P-113, D-100): muestra de datos por conexión, tokens de acceso y propuestas de ajustes.
-- Fechas como texto ISO 8601 en UTC (se ordena igual que la fecha) y banderas como 0/1, igual en los tres motores.
ALTER TABLE mnt_connection ADD allow_samples INTEGER NOT NULL DEFAULT 0;

CREATE TABLE mnt_access_token (
    id           varchar(40)  NOT NULL PRIMARY KEY,
    name         varchar(100) NOT NULL,
    owner        varchar(200) NOT NULL,
    roles        text      NOT NULL,
    prefix       varchar(20)  NOT NULL,
    token_hash   varchar(64)  NOT NULL UNIQUE,
    created_at   varchar(40)  NOT NULL,
    expires_at   varchar(40)  NULL,
    last_used_at varchar(40)  NULL
);

CREATE TABLE mnt_override_proposal (
    id            varchar(40)  NOT NULL PRIMARY KEY,
    connection_id varchar(50)  NOT NULL,
    schema_name   varchar(128) NOT NULL,
    table_name    varchar(128) NOT NULL,
    settings      text      NOT NULL,
    reason        text      NULL,
    created_by    varchar(200) NULL,
    created_at    varchar(40)  NOT NULL,
    status        varchar(20)  NOT NULL,
    resolved_by   varchar(200) NULL,
    resolved_at   varchar(40)  NULL
);

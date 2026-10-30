-- IA por MCP (P-113, D-100): muestra de datos por conexión, tokens de acceso y propuestas de ajustes.
-- Fechas como texto ISO 8601 en UTC (se ordena igual que la fecha) y banderas como 0/1, igual en los tres motores.
ALTER TABLE mnt_connection ADD allow_samples int NOT NULL DEFAULT 0;

CREATE TABLE mnt_access_token (
    id           nvarchar(40)  NOT NULL PRIMARY KEY,
    name         nvarchar(100) NOT NULL,
    owner        nvarchar(200) NOT NULL,
    roles        nvarchar(max)      NOT NULL,
    prefix       nvarchar(20)  NOT NULL,
    token_hash   nvarchar(64)  NOT NULL UNIQUE,
    created_at   nvarchar(40)  NOT NULL,
    expires_at   nvarchar(40)  NULL,
    last_used_at nvarchar(40)  NULL
);

CREATE TABLE mnt_override_proposal (
    id            nvarchar(40)  NOT NULL PRIMARY KEY,
    connection_id nvarchar(50)  NOT NULL,
    schema_name   nvarchar(128) NOT NULL,
    table_name    nvarchar(128) NOT NULL,
    settings      nvarchar(max)      NOT NULL,
    reason        nvarchar(max)      NULL,
    created_by    nvarchar(200) NULL,
    created_at    nvarchar(40)  NOT NULL,
    status        nvarchar(20)  NOT NULL,
    resolved_by   nvarchar(200) NULL,
    resolved_at   nvarchar(40)  NULL
);

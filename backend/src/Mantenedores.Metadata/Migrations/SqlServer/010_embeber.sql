-- Aplicaciones que embeben Celdo con tokens firmados (P-135, D-113). El documento JSON lleva los sitios, el perfil y
-- la clave cifrada. Fechas como texto ISO 8601 en UTC, igual en los tres motores.
CREATE TABLE mnt_embed_app (
    id          nvarchar(40)  NOT NULL PRIMARY KEY,
    name        nvarchar(100) NOT NULL,
    definition  nvarchar(max) NOT NULL,
    created_at  nvarchar(40)  NOT NULL,
    updated_at  nvarchar(40)  NOT NULL
);

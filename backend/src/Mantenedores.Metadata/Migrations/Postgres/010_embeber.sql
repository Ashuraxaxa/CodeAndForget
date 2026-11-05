-- Aplicaciones que embeben Celdo con tokens firmados (P-135, D-113). El documento JSON lleva los sitios, el perfil y
-- la clave cifrada. Fechas como texto ISO 8601 en UTC, igual en los tres motores.
CREATE TABLE mnt_embed_app (
    id          varchar(40)  NOT NULL PRIMARY KEY,
    name        varchar(100) NOT NULL,
    definition  text NOT NULL,
    created_at  varchar(40)  NOT NULL,
    updated_at  varchar(40)  NOT NULL
);

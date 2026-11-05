-- Dashboards (P-128, D-111): la definición (tarjetas y con qué perfiles se comparte) va como documento JSON.
-- Fechas como texto ISO 8601 en UTC, igual en los tres motores.
CREATE TABLE mnt_dashboard (
    id          VARCHAR(40)  NOT NULL PRIMARY KEY,
    name        VARCHAR(100) NOT NULL,
    owner       VARCHAR(200) NOT NULL,
    definition  TEXT NOT NULL,
    created_at  VARCHAR(40)  NOT NULL,
    updated_at  VARCHAR(40)  NOT NULL
);

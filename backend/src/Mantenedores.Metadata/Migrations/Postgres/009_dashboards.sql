-- Dashboards (P-128, D-111): la definición (tarjetas y con qué perfiles se comparte) va como documento JSON.
-- Fechas como texto ISO 8601 en UTC, igual en los tres motores.
CREATE TABLE mnt_dashboard (
    id          varchar(40)  NOT NULL PRIMARY KEY,
    name        varchar(100) NOT NULL,
    owner       varchar(200) NOT NULL,
    definition  text NOT NULL,
    created_at  varchar(40)  NOT NULL,
    updated_at  varchar(40)  NOT NULL
);

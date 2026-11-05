-- Dashboards (P-128, D-111): la definición (tarjetas y con qué perfiles se comparte) va como documento JSON.
-- Fechas como texto ISO 8601 en UTC, igual en los tres motores.
CREATE TABLE mnt_dashboard (
    id          nvarchar(40)  NOT NULL PRIMARY KEY,
    name        nvarchar(100) NOT NULL,
    owner       nvarchar(200) NOT NULL,
    definition  nvarchar(max) NOT NULL,
    created_at  nvarchar(40)  NOT NULL,
    updated_at  nvarchar(40)  NOT NULL
);

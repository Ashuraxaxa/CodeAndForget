-- Licencias y planes (P-116, D-106): ajustes del servicio clave → valor. Guarda la licencia pegada en la pantalla
-- Plan, la prueba Pro y las tablas elegidas del plan Gratis. Fechas como texto ISO 8601 en UTC.
CREATE TABLE mnt_setting (
    name       VARCHAR(100) NOT NULL PRIMARY KEY,
    value      TEXT NOT NULL,
    updated_at VARCHAR(40) NOT NULL
);

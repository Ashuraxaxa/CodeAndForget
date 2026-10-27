-- Conexiones administradas (la cadena va cifrada con Data Protection) y llaves de cifrado compartidas.
-- Compatible con SQL Server 2016 (D-035). Sin separadores GO: cada script corre en una sola transacción.
CREATE TABLE mnt_connection (
    id                          nvarchar(50)   NOT NULL PRIMARY KEY,
    name                        nvarchar(200)  NOT NULL,
    engine                      nvarchar(30)   NOT NULL,
    protected_connection_string nvarchar(max)  NOT NULL,
    schemas                     nvarchar(max)  NOT NULL,
    created_at                  datetimeoffset NOT NULL,
    updated_at                  datetimeoffset NOT NULL
);

CREATE TABLE mnt_dataprotection_key (
    friendly_name nvarchar(200) NOT NULL PRIMARY KEY,
    xml           nvarchar(max) NOT NULL
);

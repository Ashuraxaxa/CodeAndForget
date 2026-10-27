-- Conexiones administradas (la cadena va cifrada con Data Protection) y llaves de cifrado compartidas.
CREATE TABLE mnt_connection (
    id                          varchar(50)  NOT NULL PRIMARY KEY,
    name                        varchar(200) NOT NULL,
    engine                      varchar(30)  NOT NULL,
    protected_connection_string text         NOT NULL,
    schemas                     text         NOT NULL,
    created_at                  timestamptz  NOT NULL,
    updated_at                  timestamptz  NOT NULL
);

CREATE TABLE mnt_dataprotection_key (
    friendly_name varchar(200) NOT NULL PRIMARY KEY,
    xml           text         NOT NULL
);

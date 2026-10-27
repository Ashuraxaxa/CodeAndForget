-- Conexiones administradas (la cadena va cifrada con Data Protection) y llaves de cifrado compartidas.
CREATE TABLE mnt_connection (
    id                          VARCHAR(50)  NOT NULL PRIMARY KEY,
    name                        VARCHAR(200) NOT NULL,
    engine                      VARCHAR(30)  NOT NULL,
    protected_connection_string TEXT         NOT NULL,
    schemas                     TEXT         NOT NULL,
    created_at                  VARCHAR(40)  NOT NULL,
    updated_at                  VARCHAR(40)  NOT NULL
);

CREATE TABLE mnt_dataprotection_key (
    friendly_name VARCHAR(200) NOT NULL PRIMARY KEY,
    xml           TEXT         NOT NULL
);

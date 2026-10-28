-- Avisos entre instancias (P-082, D-077): un contador por ámbito ("overrides:ventas", "schema:ventas", "security").
CREATE TABLE mnt_change_stamp (
    scope      varchar(200) NOT NULL PRIMARY KEY,
    version    bigint       NOT NULL,
    changed_at timestamptz  NOT NULL
);

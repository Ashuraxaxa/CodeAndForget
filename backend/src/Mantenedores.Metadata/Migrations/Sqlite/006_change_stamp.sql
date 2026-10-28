-- Avisos entre instancias (P-082, D-077): un contador por ámbito ("overrides:ventas", "schema:ventas", "security").
CREATE TABLE mnt_change_stamp (
    scope      VARCHAR(200) NOT NULL PRIMARY KEY,
    version    INTEGER      NOT NULL,
    changed_at VARCHAR(40)  NOT NULL
);

-- Seguridad (P-040, P-042): perfiles como documento JSON y usuarios propios con contraseña en hash (D-062).
CREATE TABLE mnt_profile (
    id         VARCHAR(50)  NOT NULL PRIMARY KEY,
    name       VARCHAR(200) NOT NULL,
    settings   TEXT         NOT NULL,
    updated_at VARCHAR(40)  NOT NULL,
    updated_by VARCHAR(200) NULL
);

CREATE TABLE mnt_user (
    username      VARCHAR(100) NOT NULL PRIMARY KEY,
    display_name  VARCHAR(200) NULL,
    password_hash TEXT         NOT NULL,
    active        INTEGER      NOT NULL,
    updated_at    VARCHAR(40)  NOT NULL
);

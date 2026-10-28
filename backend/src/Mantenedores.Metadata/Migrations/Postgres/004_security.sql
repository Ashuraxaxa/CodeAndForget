-- Seguridad (P-040, P-042): perfiles como documento JSON y usuarios propios con contraseña en hash (D-062).
CREATE TABLE mnt_profile (
    id         varchar(50)  NOT NULL PRIMARY KEY,
    name       varchar(200) NOT NULL,
    settings   text         NOT NULL,
    updated_at timestamptz  NOT NULL,
    updated_by varchar(200) NULL
);

CREATE TABLE mnt_user (
    username      varchar(100) NOT NULL PRIMARY KEY,
    display_name  varchar(200) NULL,
    password_hash text         NOT NULL,
    active        boolean      NOT NULL,
    updated_at    timestamptz  NOT NULL
);

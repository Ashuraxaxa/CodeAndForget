-- Seguridad (P-040, P-042): perfiles como documento JSON y usuarios propios con contraseña en hash (D-062).
CREATE TABLE mnt_profile (
    id         nvarchar(50)   NOT NULL PRIMARY KEY,
    name       nvarchar(200)  NOT NULL,
    settings   nvarchar(max)  NOT NULL,
    updated_at datetimeoffset NOT NULL,
    updated_by nvarchar(200)  NULL
);

CREATE TABLE mnt_user (
    username      nvarchar(100)  NOT NULL PRIMARY KEY,
    display_name  nvarchar(200)  NULL,
    password_hash nvarchar(max)  NOT NULL,
    active        bit            NOT NULL,
    updated_at    datetimeoffset NOT NULL
);

-- Base de ejemplo para probar los mantenedores dinámicos (SQL Server).
-- Misma estructura que la de Postgres, incluido el trigger (caso típico que rompe un OUTPUT directo).
-- Compatible con SQL Server 2016+ (versión mínima, D-035): no usar CREATE OR ALTER, STRING_AGG, TRIM ni CONCAT_WS.
IF DB_ID('Demo') IS NULL CREATE DATABASE Demo;
GO
USE Demo;
GO
CREATE SCHEMA ventas;
GO
CREATE TABLE ventas.Categoria (
    Id      int IDENTITY PRIMARY KEY,
    Nombre  nvarchar(80) NOT NULL UNIQUE,
    Activo  bit NOT NULL DEFAULT 1
);

CREATE TABLE ventas.Producto (
    Id          int IDENTITY PRIMARY KEY,
    Codigo      varchar(20) NOT NULL UNIQUE,
    Nombre      nvarchar(120) NOT NULL,
    CategoriaId int NOT NULL REFERENCES ventas.Categoria(Id),
    Precio      decimal(12,2) NOT NULL CHECK (Precio >= 0),
    Atributos   nvarchar(max) NULL,
    CreadoEn    datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    PrecioConIva AS (Precio * 1.19),
    RowVer      rowversion
);

CREATE TABLE ventas.Etiqueta (
    Id     int IDENTITY PRIMARY KEY,
    Nombre nvarchar(40) NOT NULL
);

CREATE TABLE ventas.ProductoEtiqueta (
    ProductoId int NOT NULL REFERENCES ventas.Producto(Id) ON DELETE CASCADE,
    EtiquetaId int NOT NULL REFERENCES ventas.Etiqueta(Id),
    PRIMARY KEY (ProductoId, EtiquetaId)
);

CREATE TABLE ventas.Cliente (
    Id          uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
    Rut         varchar(12) NOT NULL UNIQUE,
    RazonSocial nvarchar(150) NOT NULL,
    Email       varchar(120) NULL,
    FechaAlta   date NOT NULL DEFAULT CAST(GETDATE() AS date)
);

CREATE TABLE ventas.Pedido (
    Id          bigint IDENTITY PRIMARY KEY,
    ClienteId   uniqueidentifier NOT NULL REFERENCES ventas.Cliente(Id),
    Fecha       datetime2 NOT NULL DEFAULT SYSDATETIME(),
    Estado      varchar(15) NOT NULL DEFAULT 'borrador'
                CHECK (Estado IN ('borrador', 'confirmado', 'despachado', 'anulado')),
    Observacion nvarchar(max) NULL,
    -- 38 dígitos: no cabe en decimal de .NET, se lee como texto (P-023).
    MontoTotal  decimal(38,4) NULL
);

CREATE TABLE ventas.PedidoDetalle (
    PedidoId   bigint NOT NULL REFERENCES ventas.Pedido(Id) ON DELETE CASCADE,
    Linea      smallint NOT NULL,
    ProductoId int NOT NULL REFERENCES ventas.Producto(Id),
    Cantidad   int NOT NULL CHECK (Cantidad > 0),
    Precio     decimal(12,2) NOT NULL,
    PRIMARY KEY (PedidoId, Linea)
);

CREATE TABLE ventas.Empleado (
    Id          int IDENTITY PRIMARY KEY,
    Nombre      nvarchar(100) NOT NULL,
    JefeId      int NULL REFERENCES ventas.Empleado(Id),
    HoraEntrada time NULL
);

CREATE TABLE ventas.LogEvento (
    OcurridoEn datetime2 NOT NULL DEFAULT SYSDATETIME(),
    Detalle    nvarchar(max) NULL
);
GO
CREATE TRIGGER ventas.trg_Producto_Log ON ventas.Producto AFTER INSERT, UPDATE AS
    INSERT INTO ventas.LogEvento (Detalle) SELECT CONCAT('Producto ', Codigo, ' guardado') FROM inserted;
GO
CREATE VIEW ventas.vProductos AS
SELECT p.Id, p.Codigo, p.Nombre, c.Nombre AS Categoria, p.Precio
FROM ventas.Producto p JOIN ventas.Categoria c ON c.Id = p.CategoriaId;
GO
INSERT INTO ventas.Categoria (Nombre) VALUES (N'Bebidas'), (N'Snacks'), (N'Lácteos');
INSERT INTO ventas.Producto (Codigo, Nombre, CategoriaId, Precio, Atributos) VALUES
    ('BEB-001', N'Agua mineral 500ml', 1, 690, N'{"retornable": false}'),
    ('BEB-002', N'Jugo de naranja 1L', 1, 1890, NULL),
    ('SNK-001', N'Papas fritas 150g', 2, 1490, N'{"sin_gluten": true}'),
    ('LAC-001', N'Leche entera 1L', 3, 1090, NULL);
INSERT INTO ventas.Etiqueta (Nombre) VALUES (N'Oferta'), (N'Nuevo');
INSERT INTO ventas.ProductoEtiqueta VALUES (1, 1), (3, 2);
INSERT INTO ventas.Cliente (Rut, RazonSocial, Email) VALUES
    ('76.123.456-7', N'Comercial Los Andes SpA', 'compras@losandes.cl'),
    ('77.987.654-3', N'Minimarket El Sol', NULL);
INSERT INTO ventas.Pedido (ClienteId, Estado, MontoTotal)
    SELECT Id, 'confirmado', 1234567890123456789012345678901234.5678 FROM ventas.Cliente WHERE Rut = '76.123.456-7';
INSERT INTO ventas.PedidoDetalle VALUES (1, 1, 1, 10, 690), (1, 2, 3, 5, 1490);
INSERT INTO ventas.Empleado (Nombre) VALUES (N'Ana Gerente');
INSERT INTO ventas.Empleado (Nombre, JefeId, HoraEntrada) VALUES (N'Luis Vendedor', 1, '09:00');
GO

-- Tipos de P-018: archivo (varbinary), geografía (se lee como WKT) y jerarquía (se lee como ruta).
ALTER TABLE ventas.Categoria ADD Icono varbinary(max) NULL;
ALTER TABLE ventas.Cliente ADD Ubicacion geography NULL;
ALTER TABLE ventas.Empleado ADD Nodo hierarchyid NULL;
GO
UPDATE ventas.Cliente SET Ubicacion = geography::Point(-33.45, -70.66, 4326) WHERE Rut = '76.123.456-7';
UPDATE ventas.Empleado SET Nodo = hierarchyid::GetRoot() WHERE Id = 1;
UPDATE ventas.Empleado SET Nodo = hierarchyid::Parse('/1/') WHERE Id = 2;
GO

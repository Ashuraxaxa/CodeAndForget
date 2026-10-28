-- Base de ejemplo para probar los mantenedores dinámicos (MySQL 8.0+ y MariaDB 10.6+).
-- Misma estructura que las de PostgreSQL y SQL Server. En MySQL el "esquema" es la base de datos (D-032).
-- Compatible con las versiones mínimas (D-035): sin funciones posteriores a MySQL 8.0 / MariaDB 10.6.
-- Los triggers son de una sola sentencia para no necesitar DELIMITER (comando del cliente mysql).
CREATE DATABASE IF NOT EXISTS ventas CHARACTER SET utf8mb4;
USE ventas;

CREATE TABLE categoria (
    id     INT AUTO_INCREMENT PRIMARY KEY,
    nombre VARCHAR(80) NOT NULL,
    activo BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_categoria_nombre UNIQUE (nombre)
);

CREATE TABLE producto (
    id           INT AUTO_INCREMENT PRIMARY KEY,
    codigo       VARCHAR(20) NOT NULL,
    nombre       VARCHAR(120) NOT NULL,
    categoria_id INT NOT NULL,
    precio       DECIMAL(12,2) NOT NULL,
    atributos    JSON NULL,
    creado_en    TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_producto_codigo UNIQUE (codigo),
    CONSTRAINT fk_producto_categoria FOREIGN KEY (categoria_id) REFERENCES categoria(id),
    CONSTRAINT ck_producto_precio CHECK (precio >= 0)
);

CREATE TABLE etiqueta (
    id     INT AUTO_INCREMENT PRIMARY KEY,
    nombre VARCHAR(40) NOT NULL
);

-- N:M: tabla puente con dos FK que forman la PK.
CREATE TABLE producto_etiqueta (
    producto_id INT NOT NULL,
    etiqueta_id INT NOT NULL,
    PRIMARY KEY (producto_id, etiqueta_id),
    CONSTRAINT fk_pe_producto FOREIGN KEY (producto_id) REFERENCES producto(id) ON DELETE CASCADE,
    CONSTRAINT fk_pe_etiqueta FOREIGN KEY (etiqueta_id) REFERENCES etiqueta(id)
);

-- UUID en CHAR(36) con default por expresión: MySQL no puede devolverlo, lo genera la app (D-040).
CREATE TABLE cliente (
    id           CHAR(36) NOT NULL DEFAULT (UUID()) PRIMARY KEY,
    rut          VARCHAR(12) NOT NULL,
    razon_social VARCHAR(150) NOT NULL,
    email        VARCHAR(120) NULL,
    fecha_alta   DATE NOT NULL DEFAULT (CURRENT_DATE),
    CONSTRAINT uq_cliente_rut UNIQUE (rut)
);

CREATE TABLE pedido (
    id          BIGINT AUTO_INCREMENT PRIMARY KEY,
    cliente_id  CHAR(36) NOT NULL,
    fecha       DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    estado      ENUM('borrador', 'confirmado', 'despachado', 'anulado') NOT NULL DEFAULT 'borrador',
    observacion TEXT NULL,
    -- 38 dígitos: no cabe en decimal de .NET, se lee como texto (P-023).
    monto_total DECIMAL(38,4) NULL,
    CONSTRAINT fk_pedido_cliente FOREIGN KEY (cliente_id) REFERENCES cliente(id)
);

-- PK compuesta.
CREATE TABLE pedido_detalle (
    pedido_id   BIGINT NOT NULL,
    linea       SMALLINT NOT NULL,
    producto_id INT NOT NULL,
    cantidad    INT NOT NULL,
    precio      DECIMAL(12,2) NOT NULL,
    PRIMARY KEY (pedido_id, linea),
    CONSTRAINT fk_detalle_pedido FOREIGN KEY (pedido_id) REFERENCES pedido(id) ON DELETE CASCADE,
    CONSTRAINT fk_detalle_producto FOREIGN KEY (producto_id) REFERENCES producto(id),
    CONSTRAINT ck_detalle_cantidad CHECK (cantidad > 0)
);

-- Autorreferencia (árbol).
CREATE TABLE empleado (
    id           INT AUTO_INCREMENT PRIMARY KEY,
    nombre       VARCHAR(100) NOT NULL,
    jefe_id      INT NULL,
    hora_entrada TIME NULL,
    CONSTRAINT fk_empleado_jefe FOREIGN KEY (jefe_id) REFERENCES empleado(id)
);

-- Sin PK: queda de solo lectura.
CREATE TABLE log_evento (
    ocurrido_en TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    detalle     TEXT NULL
);

-- Triggers equivalentes al de SQL Server y Postgres (MySQL exige uno por evento).
CREATE TRIGGER trg_producto_log_ins AFTER INSERT ON producto FOR EACH ROW
    INSERT INTO log_evento (detalle) VALUES (CONCAT('Producto ', NEW.codigo, ' guardado'));
CREATE TRIGGER trg_producto_log_upd AFTER UPDATE ON producto FOR EACH ROW
    INSERT INTO log_evento (detalle) VALUES (CONCAT('Producto ', NEW.codigo, ' guardado'));

CREATE VIEW v_productos AS
SELECT p.id, p.codigo, p.nombre, c.nombre AS categoria, p.precio
FROM producto p JOIN categoria c ON c.id = p.categoria_id;

INSERT INTO categoria (nombre) VALUES ('Bebidas'), ('Snacks'), ('Lácteos');
INSERT INTO producto (codigo, nombre, categoria_id, precio, atributos) VALUES
    ('BEB-001', 'Agua mineral 500ml', 1, 690, '{"retornable": false}'),
    ('BEB-002', 'Jugo de naranja 1L', 1, 1890, NULL),
    ('SNK-001', 'Papas fritas 150g', 2, 1490, '{"sin_gluten": true}'),
    ('LAC-001', 'Leche entera 1L', 3, 1090, NULL);
INSERT INTO etiqueta (nombre) VALUES ('Oferta'), ('Nuevo');
INSERT INTO producto_etiqueta VALUES (1, 1), (3, 2);
INSERT INTO cliente (rut, razon_social, email) VALUES
    ('76.123.456-7', 'Comercial Los Andes SpA', 'compras@losandes.cl'),
    ('77.987.654-3', 'Minimarket El Sol', NULL);
INSERT INTO pedido (cliente_id, estado, monto_total)
    SELECT id, 'confirmado', 1234567890123456789012345678901234.5678 FROM cliente WHERE rut = '76.123.456-7';
INSERT INTO pedido_detalle VALUES (1, 1, 1, 10, 690), (1, 2, 3, 5, 1490);
INSERT INTO empleado (nombre) VALUES ('Ana Gerente');
INSERT INTO empleado (nombre, jefe_id, hora_entrada) VALUES ('Luis Vendedor', 1, '09:00');

-- Tipos de P-018: archivo (LONGBLOB) y punto geográfico (se lee como WKT).
ALTER TABLE categoria ADD COLUMN icono LONGBLOB NULL;
ALTER TABLE cliente ADD COLUMN ubicacion POINT NULL;
UPDATE cliente SET ubicacion = ST_GeomFromText('POINT(-70.66 -33.45)') WHERE rut = '76.123.456-7';

-- Relación de varias columnas (P-030): una sucursal se identifica por país + código.
CREATE TABLE sucursal (
    pais   CHAR(2)     NOT NULL,
    codigo VARCHAR(10) NOT NULL,
    nombre VARCHAR(80) NOT NULL,
    PRIMARY KEY (pais, codigo)
) ENGINE=InnoDB;
CREATE TABLE bodega (
    id              INT AUTO_INCREMENT PRIMARY KEY,
    nombre          VARCHAR(80) NOT NULL,
    pais            CHAR(2)     NOT NULL,
    sucursal_codigo VARCHAR(10) NOT NULL,
    CONSTRAINT fk_bodega_sucursal FOREIGN KEY (pais, sucursal_codigo) REFERENCES sucursal (pais, codigo)
) ENGINE=InnoDB;
INSERT INTO sucursal VALUES ('CL', 'STGO', 'Santiago Centro'), ('CL', 'VAL', 'Valparaíso'), ('PE', 'LIM', 'Lima');
INSERT INTO bodega (nombre, pais, sucursal_codigo) VALUES ('Bodega norte', 'CL', 'STGO'), ('Bodega puerto', 'CL', 'VAL');

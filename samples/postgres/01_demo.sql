-- Base de ejemplo para probar los mantenedores dinámicos (PostgreSQL).
-- Cubre: FK simples, PK compuesta, N:M, autorreferencia, enum, json, uuid, vista, tabla sin PK y trigger.
-- Compatible con PostgreSQL 12+ (versión mínima, D-035).

-- gen_random_uuid() es nativo recién desde PostgreSQL 13; en la 12 lo aporta pgcrypto.
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE SCHEMA IF NOT EXISTS ventas;

CREATE TYPE ventas.estado_pedido AS ENUM ('borrador', 'confirmado', 'despachado', 'anulado');

CREATE TABLE ventas.categoria (
    id          serial PRIMARY KEY,
    nombre      varchar(80) NOT NULL UNIQUE,
    activo      boolean NOT NULL DEFAULT true
);

CREATE TABLE ventas.producto (
    id           integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    codigo       varchar(20) NOT NULL UNIQUE,
    nombre       varchar(120) NOT NULL,
    categoria_id integer NOT NULL REFERENCES ventas.categoria(id),
    precio       numeric(12,2) NOT NULL CHECK (precio >= 0),
    atributos    jsonb,
    creado_en    timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE ventas.etiqueta (
    id     serial PRIMARY KEY,
    nombre varchar(40) NOT NULL
);

-- N:M: tabla puente con dos FK que forman la PK.
CREATE TABLE ventas.producto_etiqueta (
    producto_id integer NOT NULL REFERENCES ventas.producto(id) ON DELETE CASCADE,
    etiqueta_id integer NOT NULL REFERENCES ventas.etiqueta(id),
    PRIMARY KEY (producto_id, etiqueta_id)
);

CREATE TABLE ventas.cliente (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    rut        varchar(12) NOT NULL UNIQUE,
    razon_social varchar(150) NOT NULL,
    email      varchar(120),
    fecha_alta date NOT NULL DEFAULT current_date
);

CREATE TABLE ventas.pedido (
    id         bigserial PRIMARY KEY,
    cliente_id uuid NOT NULL REFERENCES ventas.cliente(id),
    fecha      timestamp NOT NULL DEFAULT now(),
    estado     ventas.estado_pedido NOT NULL DEFAULT 'borrador',
    observacion text,
    -- 38 dígitos: no cabe en decimal de .NET, se lee como texto (P-023).
    monto_total numeric(38,4)
);

-- PK compuesta.
CREATE TABLE ventas.pedido_detalle (
    pedido_id   bigint NOT NULL REFERENCES ventas.pedido(id) ON DELETE CASCADE,
    linea       smallint NOT NULL,
    producto_id integer NOT NULL REFERENCES ventas.producto(id),
    cantidad    integer NOT NULL CHECK (cantidad > 0),
    precio      numeric(12,2) NOT NULL,
    PRIMARY KEY (pedido_id, linea)
);

-- Autorreferencia (árbol).
CREATE TABLE ventas.empleado (
    id      serial PRIMARY KEY,
    nombre  varchar(100) NOT NULL,
    jefe_id integer REFERENCES ventas.empleado(id),
    hora_entrada time
);

-- Sin PK: queda de solo lectura.
CREATE TABLE ventas.log_evento (
    ocurrido_en timestamptz NOT NULL DEFAULT now(),
    detalle     text
);

-- Trigger equivalente al de SQL Server: deja registro de cada producto guardado.
CREATE FUNCTION ventas.fn_producto_log() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO ventas.log_evento (detalle) VALUES ('Producto ' || NEW.codigo || ' guardado');
    RETURN NEW;
END
$$;

CREATE TRIGGER trg_producto_log AFTER INSERT OR UPDATE ON ventas.producto
    FOR EACH ROW EXECUTE FUNCTION ventas.fn_producto_log();

CREATE VIEW ventas.v_productos AS
SELECT p.id, p.codigo, p.nombre, c.nombre AS categoria, p.precio
FROM ventas.producto p JOIN ventas.categoria c ON c.id = p.categoria_id;

INSERT INTO ventas.categoria (nombre) VALUES ('Bebidas'), ('Snacks'), ('Lácteos');
INSERT INTO ventas.producto (codigo, nombre, categoria_id, precio, atributos) VALUES
    ('BEB-001', 'Agua mineral 500ml', 1, 690, '{"retornable": false}'),
    ('BEB-002', 'Jugo de naranja 1L', 1, 1890, NULL),
    ('SNK-001', 'Papas fritas 150g', 2, 1490, '{"sin_gluten": true}'),
    ('LAC-001', 'Leche entera 1L', 3, 1090, NULL);
INSERT INTO ventas.etiqueta (nombre) VALUES ('Oferta'), ('Nuevo');
INSERT INTO ventas.producto_etiqueta VALUES (1, 1), (3, 2);
INSERT INTO ventas.cliente (rut, razon_social, email) VALUES
    ('76.123.456-7', 'Comercial Los Andes SpA', 'compras@losandes.cl'),
    ('77.987.654-3', 'Minimarket El Sol', NULL);
INSERT INTO ventas.pedido (cliente_id, estado, monto_total)
    SELECT id, 'confirmado', 1234567890123456789012345678901234.5678 FROM ventas.cliente WHERE rut = '76.123.456-7';
INSERT INTO ventas.pedido_detalle VALUES (1, 1, 1, 10, 690), (1, 2, 3, 5, 1490);
INSERT INTO ventas.empleado (nombre) VALUES ('Ana Gerente');
INSERT INTO ventas.empleado (nombre, jefe_id, hora_entrada) VALUES ('Luis Vendedor', 1, '09:00');

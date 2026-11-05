# Power BI y Excel (OData)

**P-132 · D-112.** Celdo publica cada conexión como una **fuente OData v4 de solo lectura**. Power BI y Excel la leen
directamente, sin copiar datos a otro lado ni instalar nada en el servidor.

## Conectar

1. Crea un **token de acceso** en Celdo: tu nombre, arriba a la derecha → Tokens de acceso.
2. En **Power BI Desktop**: Obtener datos → **Fuente OData**. En **Excel**: Datos → Obtener datos → De otras fuentes →
   **Desde fuente OData**.
3. URL: `https://<tu-celdo>/odata/<id-de-la-conexión>/` (la página de tokens la muestra lista para copiar).
4. Autenticación **Básica**: en usuario escribe cualquier nombre y en contraseña el token (`mnt_…`). También se acepta
   `Authorization: Bearer mnt_…`.
5. Elige las tablas y carga. Para actualizar más tarde, Power BI vuelve a leer de Celdo.

En el **servicio** de Power BI (actualización programada en la nube), Celdo tiene que ser accesible desde internet o
desde una puerta de enlace local (*on-premises data gateway*).

## Qué se publica

- **Tablas:** las que el usuario del token puede **exportar** (D-063), y en Gratis solo las 15 elegidas. Cada una es
  un conjunto de entidades `esquema_tabla`.
- **Columnas:** las que ve, sin las ocultas ni los archivos.
- **Textos de las FK:** cada FK trae el texto de la tabla referenciada como columna aparte (`categoria_id__label` =
  "Bebidas"), aunque el usuario no vea esa tabla (D-107).
- **Tipos:** `Edm.Int64`, `Edm.Decimal`, `Edm.Boolean`, `Edm.Date`, `Edm.DateTimeOffset` (una fecha y hora sin zona se
  publica como UTC), `Edm.TimeOfDay`, `Edm.Guid` y `Edm.String` para el resto (JSON, arreglos y espaciales como
  texto). Los números de más de 28 dígitos van como texto.
- **Clave:** la PK; una vista o una tabla sin PK usa su primera columna.
- **Páginas:** hasta 5.000 filas por página, con `@odata.nextLink`. Power BI pide las siguientes solo.
- **Opciones de consulta:** `$top`, `$skip`, `$select` y `$orderby`.
  - `$filter`, `$expand`, `$apply`, `$search` y `$count` responden **501**. Así Power BI filtra en su lado, en vez de
    recibir datos sin filtrar sin darse cuenta.

## Plan y seguridad

- **Plan:** es parte de "exportar e importar en todos los formatos": **Pro** y Empresa. Los tokens de acceso también
  son de Pro.
- **Permisos:** el token tiene exactamente el acceso de quien lo creó, calculado en cada solicitud (D-100). Si se le
  quita el permiso de exportar una tabla, deja de aparecer en el feed.
- **Revocar:** borra el token en Celdo y Power BI deja de tener acceso en la siguiente actualización.
- **Red:** usa HTTPS (lo termina el proxy, ver [despliegue.md](despliegue.md)): la autenticación Básica viaja en cada
  solicitud.

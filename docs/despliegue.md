# Despliegue

El servicio es **una sola app** (API + frontend) que corre igual en un servidor propio o en cualquier
nube (D-022): como contenedor, en IIS, como servicio de Windows o como servicio systemd. Toda la
configuración está en [configuracion.md](configuracion.md).

## 1. Antes de instalar

- [ ] **Versiones de las bases** que se van a administrar, según la tabla de [Requisitos de instalación](../README.md#requisitos-de-instalación): SQL Server 2016+, PostgreSQL 12+, MySQL 8.0+ y MariaDB 10.6+. La prueba de conexión de la app muestra la versión y rechaza las menores.
- [ ] **Usuario de base de datos dedicado** en cada base, con permisos mínimos (ver [sección 9](#9-usuario-de-base-de-datos-con-permisos-mínimos)).
- [ ] **Autenticación:** elegir el modo ([configuracion.md](configuracion.md#autenticación-auth)).
  - `Local`: clave para firmar (`Auth__JwtKey`, aleatoria de 32+ caracteres, p. ej. `openssl rand -base64 48`) y el hash de la contraseña de cada usuario (`dotnet Mantenedores.Api.dll hash-password "clave"`).
  - `Jwt`: registrar en el proveedor (Duende, Keycloak, Entra ID…) un cliente **público con PKCE**, con *redirect URI* `https://<app>/auth/callback`, y configurar `Auth__Jwt__*`.
- [ ] **Dónde vive la metadata** ([configuracion.md](configuracion.md#metadata-del-servicio-metadata)): por defecto SQLite en la carpeta de datos (`Storage__DataDirectory`), que debe ser **persistente y respaldarse** porque guarda las conexiones cifradas y **las llaves para descifrarlas**. Con Postgres o SQL Server (`Metadata__Provider`, `Metadata__ConnectionString`) y `DataProtection__KeyStore=Database`, todo queda en esa base.
- [ ] **HTTPS:** lo termina un proxy (nginx, IIS, Traefik, balanceador). Si es así, activa `Hosting__ForwardedHeaders__Enabled`.
- [ ] **Varias instancias:** solo con `Metadata__Provider=Postgres` o `SqlServer` y `DataProtection__KeyStore=Database`. Con SQLite, una sola instancia.

## 2. Docker

```bash
docker build -t mantenedores .

docker run -d --name mantenedores --restart unless-stopped \
  -p 8080:8080 \
  -v mantenedores-data:/app/data \
  -e Auth__JwtKey='<clave-de-32-o-más-caracteres>' \
  -e Auth__Users__0__Username=admin \
  -e Auth__Users__0__PasswordHash='<hash>' \
  mantenedores
```

Con Docker Compose (sin las bases de demo del `docker-compose.yml` del repo):

```yaml
services:
  mantenedores:
    image: mantenedores
    restart: unless-stopped
    ports: ["8080:8080"]
    env_file: mantenedores.env        # Auth__JwtKey, Auth__Users__0__..., Hosting__...
    volumes: ["mantenedores-data:/app/data"]
volumes:
  mantenedores-data:
```

> La imagen `aspnet:8.0` no trae `curl` ni `wget`, así que el health check se hace desde afuera
> (orquestador, balanceador o monitoreo) contra `/api/health/ready`.

## 3. Kubernetes

```yaml
apiVersion: v1
kind: Secret
metadata: { name: mantenedores }
stringData:
  Auth__JwtKey: "<clave-de-32-o-más-caracteres>"
  Auth__Users__0__Username: "admin"
  Auth__Users__0__PasswordHash: "<hash>"
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata: { name: mantenedores-data }
spec:
  accessModes: [ReadWriteOnce]
  resources: { requests: { storage: 1Gi } }
---
apiVersion: apps/v1
kind: Deployment
metadata: { name: mantenedores }
spec:
  replicas: 1                      # con SQLite, una; para más: Metadata en Postgres/SQL Server + DataProtection__KeyStore=Database
  strategy: { type: Recreate }     # el volumen ReadWriteOnce no se comparte
  selector: { matchLabels: { app: mantenedores } }
  template:
    metadata: { labels: { app: mantenedores } }
    spec:
      containers:
        - name: app
          image: <registro>/mantenedores:<versión>
          ports: [{ containerPort: 8080 }]
          envFrom: [{ secretRef: { name: mantenedores } }]
          env:
            - { name: Hosting__PathBase, value: "/mantenedores" }
            - { name: Hosting__ForwardedHeaders__Enabled, value: "true" }
            - { name: Hosting__ForwardedHeaders__KnownNetworks__0, value: "10.0.0.0/8" }   # red de pods del clúster
          volumeMounts: [{ name: data, mountPath: /app/data }]
          livenessProbe:  { httpGet: { path: /mantenedores/api/health/live,  port: 8080 } }
          readinessProbe: { httpGet: { path: /mantenedores/api/health/ready, port: 8080 } }
      volumes:
        - name: data
          persistentVolumeClaim: { claimName: mantenedores-data }
---
apiVersion: v1
kind: Service
metadata: { name: mantenedores }
spec:
  selector: { app: mantenedores }
  ports: [{ port: 80, targetPort: 8080 }]
```

En el Ingress, la regla `path: /mantenedores` con `pathType: Prefix` debe **reenviar la ruta completa**, sin reescribirla.

## 4. Detrás de nginx (subruta + HTTPS)

```nginx
location /mantenedores/ {
    proxy_pass         http://127.0.0.1:8080;   # sin barra final: conserva /mantenedores
    proxy_set_header   Host              $host;
    proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
    proxy_set_header   X-Forwarded-Proto $scheme;
    proxy_set_header   X-Forwarded-Host  $host;
}
```

Configuración de la app: `Hosting__PathBase=/mantenedores` y `Hosting__ForwardedHeaders__Enabled=true`.
Si nginx está en la misma máquina no hace falta más: por defecto se confía en localhost.

## 5. IIS (Windows Server)

1. Instalar el **ASP.NET Core 8 Hosting Bundle**.
2. Publicar: `dotnet publish backend/src/Mantenedores.Api -c Release -o C:\apps\mantenedores` y copiar `frontend/dist` a `C:\apps\mantenedores\wwwroot`.
3. Crear el sitio, o una **aplicación** dentro de un sitio (p. ej. `/mantenedores`), con un *Application Pool* sin código administrado.
4. Configurar las variables en el `web.config` generado:

   ```xml
   <aspNetCore processPath="dotnet" arguments=".\Mantenedores.Api.dll" hostingModel="inprocess">
     <environmentVariables>
       <environmentVariable name="Auth__JwtKey" value="..." />
       <environmentVariable name="Auth__Users__0__Username" value="admin" />
       <environmentVariable name="Auth__Users__0__PasswordHash" value="..." />
       <environmentVariable name="Storage__DataDirectory" value="D:\datos\mantenedores" />
     </environmentVariables>
   </aspNetCore>
   ```

5. Dar a la identidad del *Application Pool* permiso de escritura sobre la carpeta de datos.

> **Bajo una aplicación de IIS, no configures `Hosting:PathBase`:** IIS ya le informa la subruta a la app.
> Ponerla también la duplicaría.

## 6. Servicio de Windows (sin IIS)

```powershell
dotnet publish backend\src\Mantenedores.Api -c Release -o C:\apps\mantenedores
# copiar frontend\dist a C:\apps\mantenedores\wwwroot
# configuración en C:\apps\mantenedores\appsettings.Production.json (Auth, Storage, Hosting)

sc.exe create Mantenedores binPath= "C:\apps\mantenedores\Mantenedores.Api.exe --urls http://+:8080" start= auto
sc.exe start Mantenedores
```

La app detecta que corre como servicio. Una `Storage:DataDirectory` relativa se resuelve contra la carpeta
de la app, no contra `C:\Windows\System32`.

## 7. Servicio systemd (Linux sin contenedores)

```ini
# /etc/systemd/system/mantenedores.service
[Unit]
Description=Mantenedores dinámicos
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/mantenedores
ExecStart=/usr/bin/dotnet /opt/mantenedores/Mantenedores.Api.dll --urls http://127.0.0.1:8080
EnvironmentFile=/etc/mantenedores/mantenedores.env
User=mantenedores
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload && sudo systemctl enable --now mantenedores
```

`Type=notify` funciona porque la app avisa a systemd cuando está lista.

## 8. Respaldo y actualización

- **Respaldo:** con los valores por defecto, copia la carpeta de datos completa (`mantenedores.db` y `keys/`); sin `keys/` las conexiones no se pueden descifrar. Con metadata en Postgres/SQL Server y llaves en la base, basta con el respaldo de esa base.
- **Desde la Fase 1:** si existe `connections.json`, se importa solo al primer arranque y queda como `connections.json.importado`.
- **Actualización:** reemplaza la imagen o los binarios conservando la carpeta de datos, y verifica `/api/health/ready`.
- **Rotación de la clave JWT:** cambiar `Auth:JwtKey` cierra todas las sesiones; no afecta a las conexiones guardadas.

## 9. Usuario de base de datos con permisos mínimos

El servicio solo necesita leer el catálogo y hacer CRUD sobre los esquemas que se expongan.
**Nunca** uses `sa`, `postgres` ni `root`.

**SQL Server**

```sql
CREATE LOGIN mantenedores WITH PASSWORD = '<clave>';
USE MiBase;
CREATE USER mantenedores FOR LOGIN mantenedores;
GRANT VIEW DEFINITION ON SCHEMA::ventas TO mantenedores;   -- leer la estructura (sys.*)
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::ventas TO mantenedores;
```

**PostgreSQL**

```sql
CREATE ROLE mantenedores LOGIN PASSWORD '<clave>';
GRANT CONNECT ON DATABASE mibase TO mantenedores;
GRANT USAGE ON SCHEMA ventas TO mantenedores;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA ventas TO mantenedores;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA ventas TO mantenedores;   -- columnas serial
-- Para las tablas que se creen después:
ALTER DEFAULT PRIVILEGES IN SCHEMA ventas GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO mantenedores;
ALTER DEFAULT PRIVILEGES IN SCHEMA ventas GRANT USAGE ON SEQUENCES TO mantenedores;
```

`pg_catalog` es legible por cualquier usuario; no requiere permisos extra.

**MySQL / MariaDB** (cada base es un "esquema")

```sql
CREATE USER 'mantenedores'@'%' IDENTIFIED BY '<clave>';
GRANT SELECT, INSERT, UPDATE, DELETE ON ventas.* TO 'mantenedores'@'%';
```

`information_schema` muestra a cada usuario solo los objetos sobre los que tiene algún permiso: con el `GRANT`
anterior ve la estructura de `ventas` y nada más.

Para solo consulta, otorga únicamente `SELECT`: el servicio mostrará los errores de permiso de la base al intentar escribir.

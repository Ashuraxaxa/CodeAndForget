# Imagen única: API .NET que además sirve el frontend compilado.
# Sale para linux/amd64 y linux/arm64 (P-123): los pasos de compilación corren en la plataforma de quien construye
# y .NET publica para la de destino, así no hace falta emular la compilación.
FROM --platform=$BUILDPLATFORM node:22-alpine AS web
WORKDIR /web
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS api
ARG TARGETARCH
# Versión de la etiqueta (1.2.0 o 1.2.0-rc.1); vacía en las compilaciones locales.
ARG VERSION=
WORKDIR /src
COPY backend/ ./
RUN set -e; \
    props=""; \
    if [ -n "$VERSION" ]; then \
      props="-p:VersionPrefix=${VERSION%%-*}"; \
      case "$VERSION" in *-*) props="$props -p:VersionSuffix=${VERSION#*-}";; esac; \
    fi; \
    dotnet publish src/Mantenedores.Api -c Release -a "$TARGETARCH" --self-contained false $props -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG VERSION=
LABEL org.opencontainers.image.title="Celdo" \
      org.opencontainers.image.description="Tu base de datos, lista para trabajar: CRUD para SQL Server, PostgreSQL, MySQL y MariaDB." \
      org.opencontainers.image.url="https://celdo.net" \
      org.opencontainers.image.version="${VERSION}"
WORKDIR /app
COPY --from=api /out ./
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080 \
    Storage__DataDirectory=/app/data
VOLUME /app/data
EXPOSE 8080
# La imagen no trae curl: la propia app consulta /api/health/ready (P-074).
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD ["dotnet", "Mantenedores.Api.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "Mantenedores.Api.dll"]

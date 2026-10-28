# Imagen única: API .NET que además sirve el frontend compilado.
FROM node:22-alpine AS web
WORKDIR /web
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api
WORKDIR /src
COPY backend/ ./
RUN dotnet publish src/Mantenedores.Api -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
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

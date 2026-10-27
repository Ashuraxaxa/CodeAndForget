#!/usr/bin/env bash
# Espera a que SQL Server responda y carga la base de ejemplo solo si aún no existe.
set -e
SQLCMD="/opt/mssql-tools18/bin/sqlcmd -C -S ${MSSQL_HOST:-mssql-demo} -U sa -P ${MSSQL_SA_PASSWORD}"

for i in $(seq 1 60); do
  $SQLCMD -Q "SELECT 1" >/dev/null 2>&1 && break
  sleep 2
done

if $SQLCMD -h -1 -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID('Demo') IS NULL THEN 0 ELSE 1 END" | grep -q 1; then
  echo "La base Demo ya existe; no se vuelve a cargar."
else
  $SQLCMD -b -i /seed/01_demo.sql
  echo "Base Demo cargada."
fi

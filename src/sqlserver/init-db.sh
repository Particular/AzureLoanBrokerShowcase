#!/bin/bash

/opt/mssql/bin/sqlservr &

for i in {1..60}; do
  /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "SELECT 1" && break
  sleep 2
done

/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "IF DB_ID('NServiceBus') IS NULL CREATE DATABASE [NServiceBus];"

wait
echo "Database initialization complete"
# SQL Warehouse + Dapper API — containerized with the ODBC driver preinstalled.
#
# The Simba/Databricks ODBC driver for Linux is x86_64 only, so the image is pinned to
# linux/amd64 (emulated on Apple Silicon — fine for a demo). The runtime stage needs
# unixODBC + the driver too: they are native dependencies loaded at run time.
#
# Build context is the REPO ROOT (the project references src/Shared):
#   docker build --platform linux/amd64 -f src/WarehouseApi/Dockerfile -t warehouse-api .
# Run:
#   docker run --rm --platform linux/amd64 -p 5000:8080 \
#     -e DATABRICKS_HOST -e DATABRICKS_HTTP_PATH -e DATABRICKS_TOKEN \
#     warehouse-api
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Shared/Shared.csproj src/Shared/
COPY src/WarehouseApi/WarehouseApi.csproj src/WarehouseApi/
RUN dotnet restore src/WarehouseApi/WarehouseApi.csproj
COPY src/Shared/ src/Shared/
COPY src/WarehouseApi/ src/WarehouseApi/
RUN dotnet publish src/WarehouseApi/WarehouseApi.csproj -c Release -o /app

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:8.0
ARG SIMBA_ODBC_URL=https://databricks-bi-artifacts.s3.us-east-2.amazonaws.com/simbaspark-drivers/odbc/2.9.1/SimbaSparkODBC-2.9.1.1001-Debian-64bit.zip

# unixODBC (driver manager) + the Simba Spark ODBC driver, plus the ODBCInstLib fix so
# the driver can load SQLGetPrivateProfileString (same gotcha as on macOS).
RUN apt-get update \
    && apt-get install -y --no-install-recommends unixodbc unzip curl ca-certificates \
    && curl -fsSL "$SIMBA_ODBC_URL" -o /tmp/simba.zip \
    && unzip -q /tmp/simba.zip -d /tmp/simba \
    && (dpkg -i /tmp/simba/*.deb || apt-get install -y -f --no-install-recommends) \
    && printf 'DriverManagerEncoding=UTF-16\nODBCInstLib=/usr/lib/x86_64-linux-gnu/libodbcinst.so.2\n' \
        >> /opt/simba/spark/lib/64/simba.sparkodbc.ini \
    && rm -rf /tmp/simba /tmp/simba.zip \
    && apt-get purge -y unzip curl && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*

ENV DATABRICKS_ODBC_DRIVER=/opt/simba/spark/lib/64/libsparkodbc_sb64.so

WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "DatabricksServing.WarehouseApi.dll"]

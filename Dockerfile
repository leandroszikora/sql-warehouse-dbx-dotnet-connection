# Databricks SQL Warehouse .NET demo — containerized with the ODBC driver preinstalled.
#
# The Simba/Databricks ODBC driver for Linux is x86_64 only, so we pin the platform
# to linux/amd64. On Apple Silicon this builds/runs under emulation (fine for a demo).
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:8.0 AS build

ARG SIMBA_ODBC_URL=https://databricks-bi-artifacts.s3.us-east-2.amazonaws.com/simbaspark-drivers/odbc/2.9.1/SimbaSparkODBC-2.9.1.1001-Debian-64bit.zip

# 1) Install unixODBC (driver manager) + the Simba Spark ODBC driver.
RUN apt-get update \
    && apt-get install -y --no-install-recommends unixodbc unzip curl ca-certificates \
    && curl -fsSL "$SIMBA_ODBC_URL" -o /tmp/simba.zip \
    && unzip -q /tmp/simba.zip -d /tmp/simba \
    && (dpkg -i /tmp/simba/*.deb || apt-get install -y -f --no-install-recommends) \
    # Point the driver at unixODBC's installer lib so it can load SQLGetPrivateProfileString.
    && printf 'DriverManagerEncoding=UTF-16\nODBCInstLib=/usr/lib/x86_64-linux-gnu/libodbcinst.so.2\n' \
        >> /opt/simba/spark/lib/64/simba.sparkodbc.ini \
    && rm -rf /tmp/simba /tmp/simba.zip \
    && apt-get purge -y unzip curl && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*

# 2) Build the app.
WORKDIR /src
COPY DatabricksSqlDemo.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app

# 3) The Program picks the Linux Simba path by default; make it explicit anyway.
ENV DATABRICKS_ODBC_DRIVER=/opt/simba/spark/lib/64/libsparkodbc_sb64.so

WORKDIR /app
ENTRYPOINT ["dotnet", "DatabricksSqlDemo.dll"]

# Lakebase + EF Core API — containerized.
#
# Unlike the root Dockerfile (which needs the x86_64-only Simba ODBC driver), this
# image has zero native dependencies: Npgsql is pure managed code, so it builds and
# runs natively on any architecture (including Apple Silicon, no emulation).
#
# Build (from this directory):   docker build -t customers-api-lakebase .
# Run:
#   docker run --rm -p 5210:8080 \
#     -e DATABRICKS_HOST -e DATABRICKS_TOKEN \
#     -e LAKEBASE_ENDPOINT -e LAKEBASE_HOST -e LAKEBASE_USER \
#     customers-api-lakebase
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY DatabricksCustomersLakebaseApi.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# launchSettings.json (port 5210) only applies to `dotnet run`; inside the container
# the app listens on the aspnet image's default, ASPNETCORE_HTTP_PORTS=8080.
EXPOSE 8080
ENTRYPOINT ["dotnet", "DatabricksCustomersLakebaseApi.dll"]

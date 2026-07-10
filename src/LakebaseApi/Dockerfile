# Lakebase + EF Core API — containerized.
#
# Unlike the WarehouseApi image (which needs the x86_64-only Simba ODBC driver), this
# image has zero native dependencies: Npgsql is pure managed code, so it builds and
# runs natively on any architecture (including Apple Silicon, no emulation).
#
# Build context is the REPO ROOT (the project references src/Shared):
#   docker build -f src/LakebaseApi/Dockerfile -t lakebase-api .
# Run:
#   docker run --rm -p 5210:8080 \
#     -e DATABRICKS_HOST -e DATABRICKS_TOKEN \
#     -e LAKEBASE_ENDPOINT -e LAKEBASE_HOST -e LAKEBASE_USER \
#     lakebase-api
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Shared/Shared.csproj src/Shared/
COPY src/LakebaseApi/LakebaseApi.csproj src/LakebaseApi/
RUN dotnet restore src/LakebaseApi/LakebaseApi.csproj
COPY src/Shared/ src/Shared/
COPY src/LakebaseApi/ src/LakebaseApi/
RUN dotnet publish src/LakebaseApi/LakebaseApi.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# launchSettings.json (port 5210) only applies to `dotnet run`; inside the container
# the app listens on the aspnet image's default, ASPNETCORE_HTTP_PORTS=8080.
EXPOSE 8080
ENTRYPOINT ["dotnet", "DatabricksServing.LakebaseApi.dll"]

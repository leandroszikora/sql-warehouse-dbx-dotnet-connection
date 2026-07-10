using DatabricksCustomersLakebaseApi;
using DatabricksCustomersLakebaseApi.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Lakebase is plain Postgres on port 5432, SSL required. The password is intentionally
// absent from the connection string: it is a short-lived OAuth credential supplied by
// LakebaseCredentialProvider whenever Npgsql opens a physical connection (pooled
// connections are reused without re-authenticating, so pool warm-up pays it once).
var connectionString = new NpgsqlConnectionStringBuilder
{
    Host = LakebaseEnvironment.Require("LAKEBASE_HOST"),
    Port = 5432,
    Database = LakebaseEnvironment.Optional("LAKEBASE_DATABASE", "databricks_postgres"),
    Username = LakebaseEnvironment.Require("LAKEBASE_USER"),
    SslMode = SslMode.Require,
}.ConnectionString;

var credentialProvider = new LakebaseCredentialProvider();
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
// Npgsql requires both variants; the sync one only runs if a connection is opened
// synchronously (this app always opens async via EF Core).
dataSourceBuilder.UsePasswordProvider(
    passwordProvider: _ => credentialProvider
        .GetPasswordAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult(),
    passwordProviderAsync: (_, ct) => credentialProvider.GetPasswordAsync(ct));
NpgsqlDataSource dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<CustomersDbContext>(options => options.UseNpgsql(dataSource));
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger UI stays on in all environments: this is a demo app.
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();

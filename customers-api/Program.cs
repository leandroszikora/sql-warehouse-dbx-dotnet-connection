using Dapper;

// Databricks columns are snake_case (first_name, email_address, ...) while the POCOs
// use PascalCase. This tells Dapper to match "first_name" -> FirstName, etc.
DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger UI stays on in all environments: this is a demo app.
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();

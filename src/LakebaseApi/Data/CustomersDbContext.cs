using DatabricksServing.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DatabricksServing.LakebaseApi.Data;

/// <summary>
/// Read-only EF Core context over the Lakebase synced table. The table is created and
/// kept in sync by Databricks (Delta → Postgres), so there are no migrations here —
/// the model just mirrors the existing table.
/// </summary>
public sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options)
    : DbContext(options)
{
    public DbSet<SalesCustomer> Customers => Set<SalesCustomer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Target table overridable via LAKEBASE_TABLE, format "schema.table".
        string qualified = LakebaseEnvironment.Optional("LAKEBASE_TABLE", "public.sales_customers");
        int dot = qualified.IndexOf('.');
        (string schema, string table) = dot > 0
            ? (qualified[..dot], qualified[(dot + 1)..])
            : ("public", qualified);

        var entity = modelBuilder.Entity<SalesCustomer>();
        entity.ToTable(table, schema);
        entity.HasKey(c => c.CustomerId);

        // Synced tables keep the Delta column names; "customerID" is case-sensitive in
        // Postgres, so every column is mapped explicitly (EF quotes the identifiers).
        entity.Property(c => c.CustomerId).HasColumnName("customerID");
        entity.Property(c => c.FirstName).HasColumnName("first_name");
        entity.Property(c => c.LastName).HasColumnName("last_name");
        entity.Property(c => c.EmailAddress).HasColumnName("email_address");
        entity.Property(c => c.PhoneNumber).HasColumnName("phone_number");
        entity.Property(c => c.Address).HasColumnName("address");
        entity.Property(c => c.City).HasColumnName("city");
        entity.Property(c => c.State).HasColumnName("state");
        entity.Property(c => c.Country).HasColumnName("country");
        entity.Property(c => c.Continent).HasColumnName("continent");
        entity.Property(c => c.PostalZipCode).HasColumnName("postal_zip_code");
        entity.Property(c => c.Gender).HasColumnName("gender");
    }
}

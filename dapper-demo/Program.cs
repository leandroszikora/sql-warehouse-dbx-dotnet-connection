using System.Data.Odbc;
using Dapper;
using DatabricksSqlDemo; // DatabricksConnection (linked from the root project)

namespace DatabricksSqlDapperDemo;

internal static class Program
{
    private static int Main()
    {
        // Databricks columns are snake_case (first_name, email_address, ...) while the POCO
        // uses PascalCase. This tells Dapper to match "first_name" -> FirstName, etc.
        // Without it, snake_case columns would not map automatically.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        try
        {
            using OdbcConnection connection = DatabricksConnection.OpenConnection();
            Console.WriteLine("Connected to the Databricks SQL Warehouse (Dapper).\n");

            // --- Test 1: typed query mapped to a POCO ------------------------------------
            // Uses the shared sample table available in every Databricks workspace.
            // Override with DAPPER_TEST_QUERY to point at another table.
            string query = Environment.GetEnvironmentVariable("DAPPER_TEST_QUERY")
                ?? """
                   SELECT customerID, first_name, last_name, email_address, phone_number,
                          address, city, state, country, continent, postal_zip_code, gender
                   FROM samples.bakehouse.sales_customers
                   LIMIT 10
                   """;

            Console.WriteLine("Test 1 — Query<SalesCustomer> (no parameters):");
            var customers = connection.Query<SalesCustomer>(query).ToList();
            Console.WriteLine($"  Rows mapped: {customers.Count}\n");
            foreach (var c in customers)
                Console.WriteLine($"  #{c.CustomerId,-6} {c.FirstName} {c.LastName,-12} " +
                                  $"{c.City}, {c.Country} <{c.EmailAddress}>");

            // --- Test 2: parameterized query (ODBC uses positional '?' placeholders) ------
            // Dapper still takes an anonymous object; with ODBC the '?' is bound positionally.
            Console.WriteLine("\nTest 2 — parameterized query (WHERE country = ?):");
            const string paramQuery = """
                SELECT customerID, first_name, last_name, email_address, phone_number,
                       address, city, state, country, continent, postal_zip_code, gender
                FROM samples.bakehouse.sales_customers
                WHERE country = ?
                LIMIT 5
                """;
            var byCountry = connection.Query<SalesCustomer>(paramQuery, new { country = "USA" }).ToList();
            Console.WriteLine($"  Rows where country = 'USA': {byCountry.Count}");
            foreach (var c in byCountry)
                Console.WriteLine($"  #{c.CustomerId,-6} {c.FirstName} {c.LastName} — {c.City}");

            Console.WriteLine("\nDone: Dapper maps Databricks rows to POCOs end to end.");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (OdbcException ex)
        {
            Console.Error.WriteLine("\nODBC error while connecting/querying Databricks:");
            foreach (OdbcError err in ex.Errors)
                Console.Error.WriteLine($"  [{err.SQLState}] {err.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\nUnexpected error: {ex.Message}");
            return 2;
        }
    }
}

/// <summary>
/// POCO mapped from samples.bakehouse.sales_customers.
/// Property names are PascalCase versions of the snake_case columns
/// (matched via DefaultTypeMap.MatchNamesWithUnderscores = true).
/// </summary>
public sealed class SalesCustomer
{
    public long CustomerId { get; set; }        // customerID
    public string? FirstName { get; set; }       // first_name
    public string? LastName { get; set; }        // last_name
    public string? EmailAddress { get; set; }    // email_address
    public string? PhoneNumber { get; set; }     // phone_number
    public string? Address { get; set; }         // address
    public string? City { get; set; }            // city
    public string? State { get; set; }           // state
    public string? Country { get; set; }         // country
    public string? Continent { get; set; }       // continent
    public long PostalZipCode { get; set; }      // postal_zip_code
    public string? Gender { get; set; }          // gender
}

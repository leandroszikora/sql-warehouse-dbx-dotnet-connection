using System.Data.Odbc;
using System.Diagnostics;
using Dapper;
using DatabricksCustomersApi.Models;
using DatabricksSqlDemo; // DatabricksConnection (linked from the root project)
using Microsoft.AspNetCore.Mvc;

namespace DatabricksCustomersApi.Controllers;

[ApiController]
[Route("customers")]
public sealed class CustomersController : ControllerBase
{
    private const int MaxLimit = 1000;

    private const string ColumnList =
        "customerID, first_name, last_name, email_address, phone_number, " +
        "address, city, state, country, continent, postal_zip_code, gender";

    // Sample table available in every Databricks workspace; override to point elsewhere.
    private static string Table =>
        Environment.GetEnvironmentVariable("CUSTOMERS_TABLE") ?? "samples.bakehouse.sales_customers";

    /// <summary>
    /// Lists customers. Each supplied query parameter becomes an equality filter,
    /// e.g. GET /customers?gender=female&amp;country=USA.
    /// </summary>
    [HttpGet]
    public IActionResult GetCustomers([FromQuery] CustomerFilters filters)
    {
        if (filters.Limit is < 1 or > MaxLimit)
            return BadRequest($"limit must be between 1 and {MaxLimit}.");

        // Fixed whitelist: query parameters map to hard-coded column names, so no
        // request-controlled text ever reaches the SQL — only parameter VALUES do.
        var candidates = new (string Column, string? Value)[]
        {
            ("gender", filters.Gender),
            ("country", filters.Country),
            ("city", filters.City),
            ("state", filters.State),
            ("continent", filters.Continent),
            ("first_name", filters.FirstName),
            ("last_name", filters.LastName),
        };

        // The ODBC driver only supports positional '?' placeholders, so the WHERE
        // clauses and the parameter list MUST be built in the same order.
        var clauses = new List<string>();
        var parameters = new DynamicParameters();
        foreach (var (column, value) in candidates)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            clauses.Add($"{column} = ?");
            parameters.Add(column, value);
        }

        string where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";
        // LIMIT is inlined (not a '?') because the Simba/Spark driver does not reliably
        // support a parameterized LIMIT; the value is a range-checked int, so it is safe.
        string sql = $"SELECT {ColumnList} FROM {Table} {where} LIMIT {filters.Limit}";

        return Execute(connection =>
        {
            var customers = connection.Query<SalesCustomer>(sql, parameters).ToList();
            return Ok(customers);
        });
    }

    /// <summary>Fetches a single customer by customerID, e.g. GET /customers/1234567.</summary>
    [HttpGet("{id:long}")]
    public IActionResult GetCustomerById(long id)
    {
        string sql = $"SELECT {ColumnList} FROM {Table} WHERE customerID = ? LIMIT 1";

        return Execute(connection =>
        {
            var customer = connection.QueryFirstOrDefault<SalesCustomer>(sql, new { id });
            return customer is null
                ? NotFound($"No customer found with customerID = {id}.")
                : Ok(customer);
        });
    }

    // Opens a connection per request (fine for a POC; each request pays the ODBC
    // handshake) and translates the demo's known failure modes into HTTP 500s.
    // Timing is reported via response headers so the JSON body stays untouched:
    //   X-Connection-Open-Ms  ODBC handshake against the warehouse
    //   X-Query-Ms            query execution + result materialization
    //   X-Total-Ms            sum of both (whole warehouse round trip)
    private IActionResult Execute(Func<OdbcConnection, IActionResult> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using OdbcConnection connection = DatabricksConnection.OpenConnection();
            long connectionOpenMs = stopwatch.ElapsedMilliseconds;
            IActionResult result = action(connection);
            stopwatch.Stop();

            Response.Headers["X-Connection-Open-Ms"] = connectionOpenMs.ToString();
            Response.Headers["X-Query-Ms"] = (stopwatch.ElapsedMilliseconds - connectionOpenMs).ToString();
            Response.Headers["X-Total-Ms"] = stopwatch.ElapsedMilliseconds.ToString();
            return result;
        }
        catch (InvalidOperationException ex) // missing env vars or ODBC driver
        {
            return Problem(ex.Message);
        }
        catch (OdbcException ex)
        {
            string details = string.Join("\n",
                ex.Errors.Cast<OdbcError>().Select(e => $"[{e.SQLState}] {e.Message}"));
            return Problem($"ODBC error while querying Databricks:\n{details}");
        }
    }
}

/// <summary>Optional equality filters for GET /customers, bound from the query string.</summary>
public sealed class CustomerFilters
{
    public string? Gender { get; init; }
    public string? Country { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Continent { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public int Limit { get; init; } = 100;
}

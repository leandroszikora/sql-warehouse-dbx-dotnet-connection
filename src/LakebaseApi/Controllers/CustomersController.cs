using System.Diagnostics;
using System.Net.Sockets;
using DatabricksServing.LakebaseApi.Data;
using DatabricksServing.Shared;
using DatabricksServing.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DatabricksServing.LakebaseApi.Controllers;

[ApiController]
[Route("customers")]
public sealed class CustomersController(CustomersDbContext db) : ControllerBase
{
    private const int MaxLimit = 1000;

    /// <summary>
    /// Lists customers. Each supplied query parameter becomes an equality filter,
    /// e.g. GET /customers?gender=female&amp;country=USA.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCustomers([FromQuery] CustomerFilters filters)
    {
        if (filters.Limit is < 1 or > MaxLimit)
            return BadRequest($"limit must be between 1 and {MaxLimit}.");

        // Same whitelist as the ODBC version, but expressed as LINQ: EF Core translates
        // these to parameterized SQL, so no request text ever reaches the query directly.
        IQueryable<SalesCustomer> query = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filters.Gender)) query = query.Where(c => c.Gender == filters.Gender);
        if (!string.IsNullOrWhiteSpace(filters.Country)) query = query.Where(c => c.Country == filters.Country);
        if (!string.IsNullOrWhiteSpace(filters.City)) query = query.Where(c => c.City == filters.City);
        if (!string.IsNullOrWhiteSpace(filters.State)) query = query.Where(c => c.State == filters.State);
        if (!string.IsNullOrWhiteSpace(filters.Continent)) query = query.Where(c => c.Continent == filters.Continent);
        if (!string.IsNullOrWhiteSpace(filters.FirstName)) query = query.Where(c => c.FirstName == filters.FirstName);
        if (!string.IsNullOrWhiteSpace(filters.LastName)) query = query.Where(c => c.LastName == filters.LastName);

        return await Execute(async () =>
        {
            List<SalesCustomer> customers = await query.Take(filters.Limit).ToListAsync();
            return Ok(customers);
        });
    }

    /// <summary>Fetches a single customer by customerID, e.g. GET /customers/1234567.</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetCustomerById(long id)
    {
        return await Execute(async () =>
        {
            SalesCustomer? customer = await db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CustomerId == id);
            return customer is null
                ? NotFound($"No customer found with customerID = {id}.")
                : Ok(customer);
        });
    }

    // Runs the query, translates the known failure modes into HTTP 500s, and reports
    // timing via response headers (mirrors WarehouseApi so both backends are directly
    // comparable). Npgsql pools connections, so unlike the ODBC path there is no
    // per-request handshake to measure separately:
    //   X-Query-Ms  query execution + result materialization
    //   X-Total-Ms  whole round trip incl. credential/connection acquisition if any
    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            IActionResult result = await action();
            stopwatch.Stop();

            Response.Headers["X-Query-Ms"] = stopwatch.ElapsedMilliseconds.ToString();
            Response.Headers["X-Total-Ms"] = stopwatch.ElapsedMilliseconds.ToString();
            return result;
        }
        catch (InvalidOperationException ex) // missing env vars or credentials API failure
        {
            return Problem(ex.Message);
        }
        catch (NpgsqlException ex)
        {
            return Problem($"Postgres error while querying Lakebase:\n{ex.Message}");
        }
        catch (HttpRequestException ex) // credentials API unreachable (bad DATABRICKS_HOST)
        {
            return Problem($"Could not reach the Databricks credentials API:\n{ex.Message}");
        }
        catch (SocketException ex) // Npgsql surfaces DNS/socket failures unwrapped
        {
            return Problem($"Could not reach the Lakebase Postgres host:\n{ex.Message}");
        }
    }
}

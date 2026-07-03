namespace DatabricksCustomersApi.Models;

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

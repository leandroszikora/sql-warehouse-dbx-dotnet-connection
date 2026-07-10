namespace DatabricksCustomersLakebaseApi.Models;

/// <summary>
/// Entity mapped to the Lakebase synced table of samples.bakehouse.sales_customers.
/// Column names are mapped explicitly in <c>CustomersDbContext</c> because the synced
/// table keeps the original snake_case (and one camelCase: customerID) column names.
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

namespace DatabricksServing.Shared;

/// <summary>
/// Optional equality filters for GET /customers, bound from the query string.
/// Shared by both backends so the two APIs expose the exact same contract.
/// </summary>
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

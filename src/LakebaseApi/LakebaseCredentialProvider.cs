using System.Text;
using System.Text.Json;

namespace DatabricksServing.LakebaseApi;

/// <summary>
/// Generates the short-lived OAuth credential that Lakebase Postgres requires as the
/// connection password. The workspace personal access token (PAT) is NOT a valid
/// Postgres password — but it IS valid to call the Databricks REST API that mints one.
/// Lakebase comes in two flavors with different credential APIs:
///  - Projects (Neon-based; the only flavor on Free Edition, hosts look like ep-xxx...):
///    POST /api/2.0/postgres/credentials with the endpoint resource name
///    (set LAKEBASE_ENDPOINT=projects/{p}/branches/{b}/endpoints/{e}).
///  - Provisioned database instances:
///    POST /api/2.0/database/credentials with the instance name (set LAKEBASE_INSTANCE).
/// Tokens expire after ~1 hour (enforced at login only), so the credential is cached
/// and refreshed a few minutes before expiry.
/// </summary>
public sealed class LakebaseCredentialProvider
{
    // Refresh this long before the reported expiration to never hand out a stale token.
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    /// <summary>Returns a valid Postgres password, minting a new one only when needed.</summary>
    public async ValueTask<string> GetPasswordAsync(CancellationToken cancellationToken)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _expiresAt - RefreshMargin)
            return _token;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token is null || DateTimeOffset.UtcNow >= _expiresAt - RefreshMargin)
                (_token, _expiresAt) = await MintCredentialAsync(cancellationToken);
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<(string Token, DateTimeOffset ExpiresAt)> MintCredentialAsync(
        CancellationToken cancellationToken)
    {
        string host = LakebaseEnvironment.Require("DATABRICKS_HOST");
        string pat = LakebaseEnvironment.Require("DATABRICKS_TOKEN");
        string? projectEndpoint = Environment.GetEnvironmentVariable("LAKEBASE_ENDPOINT");
        string? instance = Environment.GetEnvironmentVariable("LAKEBASE_INSTANCE");
        if (string.IsNullOrWhiteSpace(projectEndpoint) && string.IsNullOrWhiteSpace(instance))
            throw new InvalidOperationException(
                "Set LAKEBASE_ENDPOINT (Lakebase project, format " +
                "projects/{p}/branches/{b}/endpoints/{e} — the only flavor on Free Edition) " +
                "or LAKEBASE_INSTANCE (provisioned database instance name).");

        // Same tolerance as DatabricksConnection: users paste full workspace URLs.
        string cleanHost = host
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');

        (string path, object payload) = !string.IsNullOrWhiteSpace(projectEndpoint)
            ? ("/api/2.0/postgres/credentials",
               (object)new { endpoint = projectEndpoint })
            : ("/api/2.0/database/credentials",
               new { request_id = Guid.NewGuid().ToString(), instance_names = new[] { instance } });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{cleanHost}{path}");
        request.Headers.Authorization = new("Bearer", pat);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Databricks credentials API ({path}) returned {(int)response.StatusCode}: {body}");

        using JsonDocument json = JsonDocument.Parse(body);
        string token = json.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("Credentials API returned an empty token.");
        // Instances API says "expiration_time"; projects API says "expire_time".
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddHours(1); // documented lifetime
        foreach (string property in new[] { "expiration_time", "expire_time" })
        {
            if (json.RootElement.TryGetProperty(property, out JsonElement expiration)
                && expiration.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(expiration.GetString(), out DateTimeOffset parsed))
            {
                expiresAt = parsed;
                break;
            }
        }

        return (token, expiresAt);
    }
}

/// <summary>Environment-variable helpers shared by the Lakebase wiring.</summary>
public static class LakebaseEnvironment
{
    public static string Require(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Missing environment variable: {name}\n" +
                "Required: DATABRICKS_HOST, DATABRICKS_TOKEN, LAKEBASE_HOST, LAKEBASE_USER, " +
                "and LAKEBASE_ENDPOINT (project) or LAKEBASE_INSTANCE (provisioned instance). " +
                "Optional: LAKEBASE_DATABASE (default databricks_postgres), " +
                "LAKEBASE_TABLE (default public.sales_customers).");

    public static string Optional(string name, string defaultValue) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : defaultValue;
}

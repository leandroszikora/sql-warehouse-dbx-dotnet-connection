using System.Data.Odbc;
using System.Runtime.InteropServices;

namespace DatabricksServing.WarehouseApi;

/// <summary>
/// Builds and opens an ODBC connection to a Databricks SQL Warehouse using a personal
/// access token.
/// </summary>
public static class DatabricksConnection
{
    private static bool _resolverRegistered;

    /// <summary>
    /// Returns an <b>open</b> <see cref="OdbcConnection"/> to the SQL Warehouse.
    /// Throws <see cref="InvalidOperationException"/> with a clear message if required
    /// environment variables or the ODBC driver are missing.
    /// </summary>
    public static OdbcConnection OpenConnection()
    {
        // On macOS (Apple Silicon), Homebrew installs unixODBC under /opt/homebrew/lib,
        // a path .NET does not search by default. Register a resolver so System.Data.Odbc
        // can find libodbc without needing DYLD_LIBRARY_PATH. Not needed on Windows
        // (ODBC is built into the OS) or Linux (unixODBC lives in standard paths).
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && !_resolverRegistered)
        {
            NativeLibrary.SetDllImportResolver(typeof(OdbcConnection).Assembly, ResolveLibodbc);
            _resolverRegistered = true;
        }

        var connection = new OdbcConnection(BuildConnectionString());
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Builds the ODBC connection string from environment variables. The token is NEVER
    /// written to source or committed to the repo.
    /// </summary>
    public static string BuildConnectionString()
    {
        string? host = Environment.GetEnvironmentVariable("DATABRICKS_HOST");
        string? httpPath = Environment.GetEnvironmentVariable("DATABRICKS_HTTP_PATH");
        string? token = Environment.GetEnvironmentVariable("DATABRICKS_TOKEN");

        // ODBC driver reference. Defaults are per-OS; override with DATABRICKS_ODBC_DRIVER.
        // On Windows this is the registered driver NAME; on macOS/Linux it is a path to the lib.
        string driver = Environment.GetEnvironmentVariable("DATABRICKS_ODBC_DRIVER")
                        ?? DefaultDriver();

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(host)) missing.Add("DATABRICKS_HOST");
        if (string.IsNullOrWhiteSpace(httpPath)) missing.Add("DATABRICKS_HTTP_PATH");
        if (string.IsNullOrWhiteSpace(token)) missing.Add("DATABRICKS_TOKEN");

        if (missing.Count > 0)
            throw new InvalidOperationException(
                "Missing environment variables: " + string.Join(", ", missing) + "\n" +
                "Set them before running, for example:\n" +
                "  export DATABRICKS_HOST=\"adb-xxxx.azuredatabricks.net\"\n" +
                "  export DATABRICKS_HTTP_PATH=\"/sql/1.0/warehouses/abc123def456\"\n" +
                "  export DATABRICKS_TOKEN=\"dapiXXXXXXXX\"");

        // On macOS/Linux the driver is a file path; validate it exists for a clearer error.
        // On Windows it is a registered driver name (not a path), so we skip this check.
        if (Path.IsPathRooted(driver) && !File.Exists(driver))
            throw new InvalidOperationException(
                $"ODBC driver not found at: {driver}\n" +
                "Install the Databricks/Simba ODBC driver or set DATABRICKS_ODBC_DRIVER to the correct path.");

        // The ODBC driver expects a bare hostname: strip scheme and trailing slash in case
        // the variable contains something like "https://xxx.cloud.databricks.com/".
        string cleanHost = host!
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');

        // AuthMech=3 + UID=token + PWD=<PAT> is the standard Databricks token pattern.
        return
            $"Driver={{{driver}}};" +
            $"Host={cleanHost};" +
            "Port=443;" +
            $"HTTPPath={httpPath};" +
            "SSL=1;" +
            "ThriftTransport=2;" +
            "AuthMech=3;" +
            "UID=token;" +
            $"PWD={token};";
    }

    // Default ODBC driver reference per operating system.
    private static string DefaultDriver()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "Databricks ODBC Driver"; // registered driver name on Windows

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "/Library/databricks/databricksodbc/lib/libdatabricksodbc.dylib";

        // Linux (e.g. the Docker image): Simba Spark ODBC driver install path.
        return "/opt/simba/spark/lib/64/libsparkodbc_sb64.so";
    }

    // Resolves libodbc by searching the usual Homebrew locations (Apple Silicon and Intel).
    // Can be overridden with the UNIXODBC_LIB environment variable.
    private static IntPtr ResolveLibodbc(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.Contains("libodbc", StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("UNIXODBC_LIB"),
            "/opt/homebrew/lib/libodbc.2.dylib",   // Homebrew Apple Silicon
            "/usr/local/lib/libodbc.2.dylib",       // Homebrew Intel
        };

        foreach (var path in candidates)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path) && NativeLibrary.TryLoad(path, out IntPtr handle))
                return handle;
        }

        return IntPtr.Zero; // let the runtime fall back to its normal search
    }
}

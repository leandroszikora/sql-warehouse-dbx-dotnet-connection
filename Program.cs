using System.Data.Odbc;
using System.Runtime.InteropServices;

namespace DatabricksSqlDemo;

internal static class Program
{
    private static int Main()
    {
        // 0) On macOS (Apple Silicon), Homebrew installs unixODBC under /opt/homebrew/lib,
        //    a path .NET does not search by default. Register a resolver so System.Data.Odbc
        //    can find libodbc without needing DYLD_LIBRARY_PATH. Not needed on Windows
        //    (ODBC is built into the OS) or Linux (unixODBC lives in standard paths).
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            NativeLibrary.SetDllImportResolver(typeof(OdbcConnection).Assembly, ResolveLibodbc);

        // 1) Read configuration from environment variables.
        //    The token is NEVER written to source or committed to the repo.
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
        {
            Console.Error.WriteLine(
                "Missing environment variables: " + string.Join(", ", missing) + "\n\n" +
                "Set them before running, for example:\n" +
                "  export DATABRICKS_HOST=\"adb-xxxx.azuredatabricks.net\"\n" +
                "  export DATABRICKS_HTTP_PATH=\"/sql/1.0/warehouses/abc123def456\"\n" +
                "  export DATABRICKS_TOKEN=\"dapiXXXXXXXX\"");
            return 1;
        }

        // On macOS/Linux the driver is a file path; validate it exists for a clearer error.
        // On Windows it is a registered driver name (not a path), so we skip this check.
        if (Path.IsPathRooted(driver) && !File.Exists(driver))
        {
            Console.Error.WriteLine(
                $"ODBC driver not found at: {driver}\n" +
                "Install the Databricks/Simba ODBC driver or set DATABRICKS_ODBC_DRIVER to the correct path.");
            return 1;
        }

        // The ODBC driver expects a bare hostname: strip scheme and trailing slash in case
        // the variable contains something like "https://xxx.cloud.databricks.com/".
        string cleanHost = host!
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');

        // 2) Build the ODBC connection string using personal access token authentication.
        //    AuthMech=3 + UID=token + PWD=<PAT> is the standard Databricks pattern.
        string connectionString =
            $"Driver={{{driver}}};" +
            $"Host={cleanHost};" +
            "Port=443;" +
            $"HTTPPath={httpPath};" +
            "SSL=1;" +
            "ThriftTransport=2;" +
            "AuthMech=3;" +
            "UID=token;" +
            $"PWD={token};";

        // 3) Open the connection, run a test query and print the result.
        try
        {
            using var connection = new OdbcConnection(connectionString);
            Console.WriteLine("Connecting to the Databricks SQL Warehouse...");
            connection.Open();
            Console.WriteLine("Connection established.\n");

            const string sql = "SELECT current_user() AS user_name, now() AS server_time";
            using var command = new OdbcCommand(sql, connection);
            using OdbcDataReader reader = command.ExecuteReader();

            // Column headers.
            var columns = new string[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
                columns[i] = reader.GetName(i);
            Console.WriteLine(string.Join(" | ", columns));
            Console.WriteLine(new string('-', 40));

            // Rows.
            while (reader.Read())
            {
                var values = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                    values[i] = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i).ToString() ?? "";
                Console.WriteLine(string.Join(" | ", values));
            }

            Console.WriteLine("\nDone: the connection works end to end.");
            return 0;
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

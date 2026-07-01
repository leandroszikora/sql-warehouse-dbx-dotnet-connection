using System.Data.Odbc;

namespace DatabricksSqlDemo;

internal static class Program
{
    private static int Main()
    {
        try
        {
            // Open the connection (config + driver handling lives in DatabricksConnection).
            using OdbcConnection connection = DatabricksConnection.OpenConnection();
            Console.WriteLine("Connected to the Databricks SQL Warehouse.\n");

            // Run a test query and print the result with a raw OdbcDataReader.
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

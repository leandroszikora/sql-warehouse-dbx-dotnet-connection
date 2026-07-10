using System.Collections.Concurrent;
using System.Data;
using System.Data.Odbc;
using DatabricksSqlDemo;

namespace DatabricksCustomersApi;

/// <summary>
/// Minimal app-level pool of open ODBC connections to the SQL Warehouse.
/// System.Data.Odbc has no built-in ADO.NET pooling — that is a driver-manager
/// feature configured per machine (odbcinst.ini / Windows ODBC admin) — so the POC
/// pools in-process to stay portable across macOS, Windows and Docker.
/// The ~600 ms per-request handshake is paid only when the bag is empty.
/// </summary>
public sealed class OdbcConnectionPool : IDisposable
{
    // Idle connections kept alive. Small on purpose: one per expected concurrent
    // request is plenty for a POC; excess connections are disposed on Return.
    private const int MaxIdle = 8;

    private readonly ConcurrentBag<OdbcConnection> _idle = new();

    /// <summary>
    /// Returns an open connection, reusing an idle one when available.
    /// <paramref name="reused"/> tells the caller whether the handshake was skipped —
    /// a reused connection may still be dead server-side (warehouse idle timeout),
    /// which surfaces as an OdbcException on first use; callers should retry once
    /// with a fresh connection in that case.
    /// </summary>
    public OdbcConnection Rent(out bool reused)
    {
        while (_idle.TryTake(out OdbcConnection? connection))
        {
            if (connection.State == ConnectionState.Open)
            {
                reused = true;
                return connection;
            }
            connection.Dispose();
        }

        reused = false;
        return DatabricksConnection.OpenConnection();
    }

    /// <summary>Puts a healthy connection back; disposes it if closed or bag is full.</summary>
    public void Return(OdbcConnection connection)
    {
        if (connection.State == ConnectionState.Open && _idle.Count < MaxIdle)
            _idle.Add(connection);
        else
            connection.Dispose();
    }

    public void Dispose()
    {
        while (_idle.TryTake(out OdbcConnection? connection))
            connection.Dispose();
    }
}

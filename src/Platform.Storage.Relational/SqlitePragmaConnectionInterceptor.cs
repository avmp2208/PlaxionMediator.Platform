using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Platform.Storage.Relational;

/// <summary>
/// By default SQLite uses the "rollback journal" mode, in which a writer
/// holds an exclusive lock on the *entire database file* for the duration of
/// its transaction/commit, and readers are blocked until that lock is
/// released. With the aggregation job writing rollups roughly once per
/// minute (and ingestion writing raw samples continuously), this means every
/// dashboard query (e.g. "Last 7 days") can stall waiting for the current
/// write to finish - and can outright fail with "database is locked" if it
/// waits longer than SQLite's default (very short) lock-wait window.
///
/// This interceptor switches every new connection to Write-Ahead Logging
/// (WAL) mode, where writers append to a separate WAL file and readers keep
/// reading a consistent snapshot without ever being blocked by a writer, and
/// sets a busy timeout so that if two writers do briefly contend, ADO.NET
/// retries for a while instead of throwing immediately.
///
/// journal_mode=WAL is persisted in the database file itself (it only truly
/// needs to be set once), but re-issuing it on every connection open is
/// idempotent and cheap, so it is done unconditionally here to also cover
/// databases created before this change.
/// </summary>
internal sealed class SqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyPragmasAsync(connection, cancellationToken);
    }

    private static void ApplyPragmas(DbConnection connection)
    {
        if (connection is not SqliteConnection)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
    }

    private static async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection is not SqliteConnection)
        {
            return;
        }

        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        await using var _ = command;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

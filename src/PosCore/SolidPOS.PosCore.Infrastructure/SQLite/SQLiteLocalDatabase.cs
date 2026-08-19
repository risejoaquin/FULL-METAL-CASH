using Microsoft.Data.Sqlite;

namespace SolidPOS.PosCore.Infrastructure.SQLite;

public sealed class SQLiteLocalDatabase
{
    public SQLiteLocalDatabase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("Database path is required.", nameof(databasePath));
        DatabasePath = databasePath;
    }

    public string DatabasePath { get; }
    public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
        return connection;
    }
}

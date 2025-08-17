using Microsoft.Data.Sqlite;

namespace unofficial_pdrive_cli;

public sealed class PersistenceManager
{
    private readonly string _connectionString;

    public PersistenceManager(string dbFilePath)
    {
        _connectionString = new SqliteConnectionStringBuilder()
        {
            DataSource = dbFilePath,
            Pooling = true,
        }.ToString();
    }

    public SqliteConnection GetSqlConnection()
    {
        return new SqliteConnection(_connectionString);
    }
}

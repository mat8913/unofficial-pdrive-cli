using Microsoft.Win32.SafeHandles;
using System.Diagnostics.CodeAnalysis;

namespace unofficial_pdrive_cli;

public sealed class LocalHashCache
{
    private readonly PersistenceManager _persistenceManager;

    public LocalHashCache(PersistenceManager persistenceManager)
    {
        _persistenceManager = persistenceManager;

        using var sql = _persistenceManager.GetSqlConnection();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS "LocalHashCache" (
                "Path"              TEXT NOT NULL,
                "ModificationTime"  INTEGER NOT NULL,
                "Hash"              TEXT NOT NULL,
                "AccessTime"        INTEGER NOT NULL,
                PRIMARY KEY("Path")
            );
        """;
        sql.Open();
        cmd.ExecuteNonQuery();
    }

    public void Add(string path, DateTimeOffset mtime, string hash)
    {
        using var sql = _persistenceManager.GetSqlConnection();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            INSERT INTO LocalHashCache(Path, ModificationTime, Hash, AccessTime)
            VALUES ($x0, $x1, $x2, strftime('%s', 'now'))
            ON CONFLICT DO UPDATE SET ModificationTime=excluded.ModificationTime, Hash=excluded.Hash, AccessTime=excluded.AccessTime
        """;
        cmd.Parameters.AddWithValue("x0", path);
        cmd.Parameters.AddWithValue("x1", mtime.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("x2", hash);
        sql.Open();
        cmd.ExecuteNonQuery();
    }

    public bool TryGet(string path, out DateTimeOffset mtime, [MaybeNullWhen(false)] out string hash)
    {
        using var sql = _persistenceManager.GetSqlConnection();

        using var cmdGet = sql.CreateCommand();
        cmdGet.CommandText = """
            SELECT Hash, ModificationTime FROM LocalHashCache
            WHERE Path = $x0
        """;
        cmdGet.Parameters.AddWithValue("x0", path);

        using var cmdUpdate = sql.CreateCommand();
        cmdUpdate.CommandText = """
            UPDATE LocalHashCache
            SET AccessTime = strftime('%s', 'now')
            WHERE Path = $x0
        """;
        cmdUpdate.Parameters.AddWithValue("x0", path);

        sql.Open();
        using var transaction = sql.BeginTransaction();
        cmdGet.Transaction = transaction;
        cmdUpdate.Transaction = transaction;

        using (var reader = cmdGet.ExecuteReader())
        {
            if (!reader.Read())
            {
                hash = null;
                mtime = default;
                return false;
            }

            hash = (string)reader[0];
            mtime = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1));

            if (reader.Read())
            {
                throw new InvalidOperationException("Expecting only one result");
            }
        }

        cmdUpdate.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    public bool TryGetOrUpdate(string path, [MaybeNullWhen(false)] out string hash)
    {
        path = Path.GetFullPath(path);

        SafeFileHandle handle;
        try
        {
            handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read);
        }
        catch (IOException)
        {
            // TODO: remove from cache
            hash = null;
            return false;
        }

        using (handle)
        {
            DateTimeOffset mtime = File.GetLastWriteTimeUtc(handle);
            if (TryGet(path, out var cachedMtime, out var cachedHash) && cachedMtime.ToUnixTimeSeconds() == mtime.ToUnixTimeSeconds())
            {
                hash = cachedHash;
                return true;
            }

            using var hashAlgo = SriHasher.CreateHashAlgo();
            using var stream = new FileStream(handle, FileAccess.Read);
            hash = SriHasher.FormatHash(hashAlgo.ComputeHash(stream));
            Add(path, mtime, hash);

            return true;
        }
    }

    public string GetOrUpdate(FileStream stream)
    {
        var path = stream.Name;
        DateTimeOffset mtime = File.GetLastWriteTimeUtc(stream.SafeFileHandle);
        if (TryGet(path, out var cachedMtime, out var cachedHash) && cachedMtime.ToUnixTimeSeconds() == mtime.ToUnixTimeSeconds())
        {
            return cachedHash;
        }

        using var hashAlgo = SriHasher.CreateHashAlgo();
        var hash = SriHasher.FormatHash(hashAlgo.ComputeHash(stream));
        Add(path, mtime, hash);

        return hash;
    }
}

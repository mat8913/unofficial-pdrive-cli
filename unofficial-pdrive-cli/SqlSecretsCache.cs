using Proton.Sdk;
using Proton.Sdk.Cryptography;
using System.Diagnostics.CodeAnalysis;

namespace unofficial_pdrive_cli;

public sealed class SqlSecretsCache : ISecretsCache
{
    private readonly PersistenceManager _persistenceManager;

    public SqlSecretsCache(PersistenceManager persistenceManager)
    {
        _persistenceManager = persistenceManager;

        using var sql = _persistenceManager.GetSqlConnection();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS "SecretsCache_Secrets" (
                "Context_HasValue"  INTEGER NOT NULL,
                "Context_Name"      TEXT NOT NULL,
                "Context_Id"        TEXT NOT NULL,
                "ValueHolderName"   TEXT NOT NULL,
                "ValueHolderId"     TEXT NOT NULL,
                "ValueName"         TEXT NOT NULL,
                "SecretBytes"       BLOB NOT NULL,
                "Flags"             INTEGER NOT NULL,
                PRIMARY KEY(
                    "Context_HasValue",
                    "Context_Name",
                    "Context_Id",
                    "ValueHolderName",
                    "ValueHolderId",
                    "ValueName"
                )
            );

            CREATE TABLE IF NOT EXISTS "SecretsCache_Groups" (
                "Context_HasValue"         INTEGER NOT NULL,
                "Context_Name"             TEXT NOT NULL,
                "Context_Id"               TEXT NOT NULL,
                "ValueHolderName"          TEXT NOT NULL,
                "ValueHolderId"            TEXT NOT NULL,
                "ValueName"                TEXT NOT NULL,
                "Secret_Context_HasValue"  INTEGER NOT NULL,
                "Secret_Context_Name"      TEXT NOT NULL,
                "Secret_Context_Id"        TEXT NOT NULL,
                "Secret_ValueHolderName"   TEXT NOT NULL,
                "Secret_ValueHolderId"     TEXT NOT NULL,
                "Secret_ValueName"         TEXT NOT NULL,
                PRIMARY KEY(
                    "Context_HasValue",
                    "Context_Name",
                    "Context_Id",
                    "ValueHolderName",
                    "ValueHolderId",
                    "ValueName",
                    "Secret_Context_HasValue",
                    "Secret_Context_Name",
                    "Secret_Context_Id",
                    "Secret_ValueHolderName",
                    "Secret_ValueHolderId",
                    "Secret_ValueName"
                )
            );
        """;
        sql.Open();
        cmd.ExecuteNonQuery();
    }

    public void IncludeInGroup(CacheKey groupCacheKey, ReadOnlySpan<CacheKey> memberCacheKeys)
    {
        using var sql = _persistenceManager.GetSqlConnection();

        using var cmdClear = sql.CreateCommand();
        cmdClear.CommandText = """
            DELETE FROM SecretsCache_Groups
            WHERE Context_HasValue = $x0
            AND Context_Name = $x1
            AND Context_Id = $x2
            AND ValueHolderName = $x3
            AND ValueHolderId = $x4
            AND ValueName = $x5
        """;
        cmdClear.Parameters.AddWithValue("x0", groupCacheKey.Context.HasValue);
        cmdClear.Parameters.AddWithValue("x1", groupCacheKey.Context?.Name ?? string.Empty);
        cmdClear.Parameters.AddWithValue("x2", groupCacheKey.Context?.Id ?? string.Empty);
        cmdClear.Parameters.AddWithValue("x3", groupCacheKey.ValueHolderName);
        cmdClear.Parameters.AddWithValue("x4", groupCacheKey.ValueHolderId);
        cmdClear.Parameters.AddWithValue("x5", groupCacheKey.ValueName);

        using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SecretsCache_Groups(
                Context_HasValue,
                Context_Name,
                Context_Id,
                ValueHolderName,
                ValueHolderId,
                ValueName,
                Secret_Context_HasValue,
                Secret_Context_Name,
                Secret_Context_Id,
                Secret_ValueHolderName,
                Secret_ValueHolderId,
                Secret_ValueName)
            VALUES ($x0, $x1, $x2, $x3, $x4, $x5, $x6, $x7, $x8, $x9, $x10, $x11)
            ON CONFLICT DO NOTHING
        """;
        for (int i = 0; i < 12; i++)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = $"x{i}";
            cmd.Parameters.Add(param);
        }
        cmd.Parameters[0].Value = groupCacheKey.Context.HasValue;
        cmd.Parameters[1].Value = groupCacheKey.Context?.Name ?? string.Empty;
        cmd.Parameters[2].Value = groupCacheKey.Context?.Id ?? string.Empty;
        cmd.Parameters[3].Value = groupCacheKey.ValueHolderName;
        cmd.Parameters[4].Value = groupCacheKey.ValueHolderId;
        cmd.Parameters[5].Value = groupCacheKey.ValueName;
        sql.Open();
        using var transaction = sql.BeginTransaction();

        cmdClear.Transaction = transaction;
        cmd.Transaction = transaction;

        cmdClear.ExecuteNonQuery();

        foreach (var cacheKey in memberCacheKeys)
        {
            cmd.Parameters[6].Value = cacheKey.Context.HasValue;
            cmd.Parameters[7].Value = cacheKey.Context?.Name ?? string.Empty;
            cmd.Parameters[8].Value = cacheKey.Context?.Id ?? string.Empty;
            cmd.Parameters[9].Value = cacheKey.ValueHolderName;
            cmd.Parameters[10].Value = cacheKey.ValueHolderId;
            cmd.Parameters[11].Value = cacheKey.ValueName;
            cmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void Remove(CacheKey cacheKey)
    {
        throw new NotImplementedException();
    }

    public void Set(CacheKey cacheKey, ReadOnlySpan<byte> secretBytes, byte flags, TimeSpan expiration)
    {
        if (expiration != Timeout.InfiniteTimeSpan)
        {
            throw new NotImplementedException("Timeout not implemented");
        }

        using var sql = _persistenceManager.GetSqlConnection();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SecretsCache_Secrets(Context_HasValue, Context_Name, Context_Id, ValueHolderName, ValueHolderId, ValueName, SecretBytes, Flags)
            VALUES ($x1, $x2, $x3, $x4, $x5, $x6, $x7, $x8)
            ON CONFLICT DO UPDATE SET SecretBytes=excluded.SecretBytes, Flags=excluded.Flags
        """;
        cmd.Parameters.AddWithValue("x1", cacheKey.Context.HasValue);
        cmd.Parameters.AddWithValue("x2", cacheKey.Context?.Name ?? string.Empty);
        cmd.Parameters.AddWithValue("x3", cacheKey.Context?.Id ?? string.Empty);
        cmd.Parameters.AddWithValue("x4", cacheKey.ValueHolderName);
        cmd.Parameters.AddWithValue("x5", cacheKey.ValueHolderId);
        cmd.Parameters.AddWithValue("x6", cacheKey.ValueName);
        cmd.Parameters.AddWithValue("x7", secretBytes.ToArray());
        cmd.Parameters.AddWithValue("x8", flags);
        sql.Open();
        cmd.ExecuteNonQuery();
    }

    public bool TryUse<TState, TResult>(CacheKey cacheKey, TState state, SecretTransform<TState, TResult> transform, [MaybeNullWhen(false)] out TResult result) where TResult : notnull
    {
        byte[] secretBytes;
        byte flags;

        using (var sql = _persistenceManager.GetSqlConnection())
        {
            using var cmd = sql.CreateCommand();
            cmd.CommandText = """
                SELECT SecretBytes, Flags FROM SecretsCache_Secrets
                WHERE Context_HasValue = $x1
                AND Context_Name = $x2
                AND Context_Id = $x3
                AND ValueHolderName = $x4
                AND ValueHolderId = $x5
                AND ValueName = $x6
            """;
            cmd.Parameters.AddWithValue("x1", cacheKey.Context.HasValue);
            cmd.Parameters.AddWithValue("x2", cacheKey.Context?.Name ?? string.Empty);
            cmd.Parameters.AddWithValue("x3", cacheKey.Context?.Id ?? string.Empty);
            cmd.Parameters.AddWithValue("x4", cacheKey.ValueHolderName);
            cmd.Parameters.AddWithValue("x5", cacheKey.ValueHolderId);
            cmd.Parameters.AddWithValue("x6", cacheKey.ValueName);
            sql.Open();
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                result = default;
                return false;
            }
            secretBytes = (byte[])reader[0];
            flags = reader.GetByte(1);
            if (reader.Read())
            {
                throw new InvalidOperationException("Expecting only one result");
            }
        }

        result = transform(state, secretBytes, flags);
        return true;
    }

    public bool TryUseGroup<TState, TResult>(CacheKey groupCacheKey, TState state, SecretTransform<TState, TResult> transform, [MaybeNullWhen(false)] out List<TResult> result) where TResult : notnull
    {
        List<(byte[], byte)> secrets;
        using (var sql = _persistenceManager.GetSqlConnection())
        {
            using var cmd = sql.CreateCommand();
            cmd.CommandText = """
                SELECT SecretBytes, Flags FROM SecretsCache_Secrets S
                INNER JOIN SecretsCache_Groups G
                ON  S.Context_HasValue = G.Secret_Context_HasValue
                AND S.Context_Name     = G.Secret_Context_Name
                AND S.Context_Id       = G.Secret_Context_Id
                AND S.ValueHolderName  = G.Secret_ValueHolderName
                AND S.ValueHolderId    = G.Secret_ValueHolderId
                AND S.ValueName        = G.Secret_ValueName
                WHERE G.Context_HasValue = $x0
                AND   G.Context_Name     = $x1
                AND   G.Context_Id       = $x2
                AND   G.ValueHolderName  = $x3
                AND   G.ValueHolderId    = $x4
                AND   G.ValueName        = $x5
            """;
            cmd.Parameters.AddWithValue("x0", groupCacheKey.Context.HasValue);
            cmd.Parameters.AddWithValue("x1", groupCacheKey.Context?.Name ?? string.Empty);
            cmd.Parameters.AddWithValue("x2", groupCacheKey.Context?.Id ?? string.Empty);
            cmd.Parameters.AddWithValue("x3", groupCacheKey.ValueHolderName);
            cmd.Parameters.AddWithValue("x4", groupCacheKey.ValueHolderId);
            cmd.Parameters.AddWithValue("x5", groupCacheKey.ValueName);
            sql.Open();
            using var reader = cmd.ExecuteReader();
            if (!reader.HasRows)
            {
                result = default;
                return false;
            }

            secrets = new();
            while (reader.Read())
            {
                var secretBytes = (byte[])reader[0];
                var flags = reader.GetByte(1);
                secrets.Add((secretBytes, flags));
            }
        }

        result = secrets.Select(x => transform(state, x.Item1, x.Item2)).ToList();
        return true;
    }
}

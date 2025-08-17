using Proton.Sdk.Drive;
using System.Diagnostics.CodeAnalysis;

namespace unofficial_pdrive_cli;

public sealed class RemoteHashCache
{
    private readonly PersistenceManager _persistenceManager;

    public RemoteHashCache(PersistenceManager persistenceManager)
    {
        _persistenceManager = persistenceManager;

        using var sql = _persistenceManager.GetSqlConnection();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS "RemoteHashCache" (
                "NodeId"        TEXT NOT NULL,
                "VolumeId"      TEXT NOT NULL,
                "ShareId"       TEXT NOT NULL,
                "RevisionId"    TEXT NOT NULL,
                "Hash"          TEXT NOT NULL,
                "AccessTime"    INTEGER NOT NULL,
                PRIMARY KEY("NodeId","VolumeId","ShareId","RevisionId")
            );
        """;
        sql.Open();
        cmd.ExecuteNonQuery();
    }

    public void Add(INodeIdentity nodeIdentity, IRevisionForTransfer revision, string hash)
    {
        using var sql = _persistenceManager.GetSqlConnection();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            INSERT INTO RemoteHashCache(NodeId, VolumeId, ShareId, RevisionId, Hash, AccessTime)
            VALUES ($x0, $x1, $x2, $x3, $x4, strftime('%s', 'now'))
            ON CONFLICT DO UPDATE SET Hash=excluded.Hash, AccessTime=excluded.AccessTime
        """;
        cmd.Parameters.AddWithValue("x0", nodeIdentity.NodeId.Value);
        cmd.Parameters.AddWithValue("x1", nodeIdentity.VolumeId.Value);
        cmd.Parameters.AddWithValue("x2", nodeIdentity.ShareId.Value);
        cmd.Parameters.AddWithValue("x3", revision.RevisionId.Value);
        cmd.Parameters.AddWithValue("x4", hash);
        sql.Open();
        cmd.ExecuteNonQuery();
    }

    public bool TryGet(INodeIdentity nodeIdentity, IRevisionForTransfer revision, [MaybeNullWhen(false)] out string hash)
    {
        using var sql = _persistenceManager.GetSqlConnection();

        using var cmdGet = sql.CreateCommand();
        cmdGet.CommandText = """
            SELECT Hash FROM RemoteHashCache
            WHERE NodeId = $x0
            AND VolumeId = $x1
            AND ShareId = $x2
            AND RevisionId = $x3
        """;
        cmdGet.Parameters.AddWithValue("x0", nodeIdentity.NodeId.Value);
        cmdGet.Parameters.AddWithValue("x1", nodeIdentity.VolumeId.Value);
        cmdGet.Parameters.AddWithValue("x2", nodeIdentity.ShareId.Value);
        cmdGet.Parameters.AddWithValue("x3", revision.RevisionId.Value);

        using var cmdUpdate = sql.CreateCommand();
        cmdUpdate.CommandText = """
            UPDATE RemoteHashCache
            SET AccessTime = strftime('%s', 'now')
            WHERE NodeId = $x0
            AND VolumeId = $x1
            AND ShareId = $x2
            AND RevisionId = $x3
        """;
        cmdUpdate.Parameters.AddWithValue("x0", nodeIdentity.NodeId.Value);
        cmdUpdate.Parameters.AddWithValue("x1", nodeIdentity.VolumeId.Value);
        cmdUpdate.Parameters.AddWithValue("x2", nodeIdentity.ShareId.Value);
        cmdUpdate.Parameters.AddWithValue("x3", revision.RevisionId.Value);

        sql.Open();
        using var transaction = sql.BeginTransaction();
        cmdGet.Transaction = transaction;
        cmdUpdate.Transaction = transaction;

        using (var reader = cmdGet.ExecuteReader())
        {
            if (!reader.Read())
            {
                hash = null;
                return false;
            }

            hash = (string)reader[0];

            if (reader.Read())
            {
                throw new InvalidOperationException("Expecting only one result");
            }
        }

        cmdUpdate.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }
}

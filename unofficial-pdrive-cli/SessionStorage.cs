namespace unofficial_pdrive_cli;

public sealed class SessionStorage
{
    private readonly PersistenceManager _persistenceManager;

    public SessionStorage(PersistenceManager persistenceManager)
    {
        _persistenceManager = persistenceManager;

        using var sql = _persistenceManager.GetSqlConnection();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS "Session" (
                "SessionId"                       TEXT NOT NULL,
                "Username"                        TEXT NOT NULL,
                "UserId"                          TEXT NOT NULL,
                "AccessToken"                     TEXT NOT NULL,
                "RefreshToken"                    TEXT NOT NULL,
                "IsWaitingForSecondFactorCode"    INTEGER NOT NULL,
                "PasswordMode"                    INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS "Session_Scopes" (
                "Scope"    TEXT NOT NULL
            );
        """;
        sql.Open();
        cmd.ExecuteNonQuery();
    }

    public void StoreSession(StoredSession session)
    {
        using var sql = _persistenceManager.GetSqlConnection();

        using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            DELETE FROM Session;
            DELETE FROM Session_Scopes;

            INSERT INTO Session(
                SessionId,
                Username,
                UserId,
                AccessToken,
                RefreshToken,
                IsWaitingForSecondFactorCode,
                PasswordMode)
            VALUES ($x0, $x1, $x2, $x3, $x4, $x5, $x6);
        """;
        cmd.Parameters.AddWithValue("x0", session.SessionId);
        cmd.Parameters.AddWithValue("x1", session.Username);
        cmd.Parameters.AddWithValue("x2", session.UserId);
        cmd.Parameters.AddWithValue("x3", session.AccessToken);
        cmd.Parameters.AddWithValue("x4", session.RefreshToken);
        cmd.Parameters.AddWithValue("x5", session.IsWaitingForSecondFactorCode);
        cmd.Parameters.AddWithValue("x6", session.PasswordMode);

        using var scopeCmd = sql.CreateCommand();
        scopeCmd.CommandText = """
            INSERT INTO Session_Scopes(Scope) VALUES ($x0)
        """;
        var scopeParam = scopeCmd.CreateParameter();
        scopeParam.ParameterName = "x0";
        scopeCmd.Parameters.Add(scopeParam);

        sql.Open();
        using var transaction = sql.BeginTransaction();

        cmd.Transaction = transaction;
        scopeCmd.Transaction = transaction;

        cmd.ExecuteNonQuery();

        foreach (var scope in session.Scopes)
        {
            scopeParam.Value = scope;
            scopeCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public bool TryLoadSession(out StoredSession session)
    {
        List<string> scopes;

        using (var sql = _persistenceManager.GetSqlConnection())
        {
            using var sessionCmd = sql.CreateCommand();
            sessionCmd.CommandText = """
                SELECT
                    SessionId,
                    Username,
                    UserId,
                    AccessToken,
                    RefreshToken,
                    IsWaitingForSecondFactorCode,
                    PasswordMode
                FROM Session
            """;

            using var scopeCmd = sql.CreateCommand();
            scopeCmd.CommandText = """
                SELECT Scope FROM Session_Scopes
            """;

            sql.Open();
            using var transaction = sql.BeginTransaction();

            sessionCmd.Transaction = transaction;
            scopeCmd.Transaction = transaction;

            var sessionReader = sessionCmd.ExecuteReader();
            if (!sessionReader.Read())
            {
                session = default;
                return false;
            }
            session = new(
                SessionId: sessionReader.GetString(0),
                Username: sessionReader.GetString(1),
                UserId: sessionReader.GetString(2),
                AccessToken: sessionReader.GetString(3),
                RefreshToken: sessionReader.GetString(4),
                Scopes: Array.Empty<string>(),
                IsWaitingForSecondFactorCode: sessionReader.GetBoolean(5),
                PasswordMode: sessionReader.GetInt32(6)
            );
            if (sessionReader.Read())
            {
                throw new InvalidOperationException("Expecting only one result");
            }

            scopes = new();
            var scopeReader = scopeCmd.ExecuteReader();
            while (scopeReader.Read())
            {
                scopes.Add(scopeReader.GetString(0));
            }

            transaction.Commit();
        }

        session = session with
        {
            Scopes = scopes.ToArray()
        };
        return true;
    }

    public void UpdateTokens(string accessToken, string refreshToken)
    {
        using var sql = _persistenceManager.GetSqlConnection();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            UPDATE Session
            SET AccessToken = $x0,
            RefreshToken = $x1
        """;
        cmd.Parameters.AddWithValue("x0", accessToken);
        cmd.Parameters.AddWithValue("x1", refreshToken);
        sql.Open();
        cmd.ExecuteNonQuery();
    }
}

public readonly record struct StoredSession
(
    string SessionId,
    string Username,
    string UserId,
    string AccessToken,
    string RefreshToken,
    string[] Scopes,
    bool IsWaitingForSecondFactorCode,
    int PasswordMode
);

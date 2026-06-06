/// Creates and queries the SQLite database at %APPDATA%\Fermata\fermata.db.
/// Does not perform any UI interactions — callers are responsible for threading.
using Microsoft.Data.Sqlite;

namespace FermataUI.Services;

public record LaunchRecord(
    long Id, string Timestamp, string Application,
    string ExePath, string Outcome, long WaitedMs, string? Journal);

public record WeeklySummary(int Total, int Continued, int Cancelled);

public class DatabaseService
{
    private readonly string _dbPath = Path.Combine(ConfigService.DataDir, "fermata.db");
    private readonly string _connString;

    public DatabaseService()
    {
        _connString = $"Data Source={_dbPath}";
        EnsureSchema();
    }

    public long LogLaunch(string application, string exePath, string timestamp)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO launches (timestamp, application, exe_path, outcome, waited_ms)
            VALUES ($ts, $app, $exe, 'pending', 0);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$ts", timestamp);
        cmd.Parameters.AddWithValue("$app", application);
        cmd.Parameters.AddWithValue("$exe", exePath);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void UpdateOutcome(long launchId, string outcome, long waitedMs)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE launches SET outcome=$o, waited_ms=$w WHERE id=$id";
        cmd.Parameters.AddWithValue("$o", outcome);
        cmd.Parameters.AddWithValue("$w", waitedMs);
        cmd.Parameters.AddWithValue("$id", launchId);
        cmd.ExecuteNonQuery();
    }

    public void InsertJournalEntry(long launchId, string timestamp, string entry)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO journal_entries (launch_id, timestamp, entry) VALUES ($lid, $ts, $e)";
        cmd.Parameters.AddWithValue("$lid", launchId);
        cmd.Parameters.AddWithValue("$ts", timestamp);
        cmd.Parameters.AddWithValue("$e", entry);
        cmd.ExecuteNonQuery();
    }

    public List<LaunchRecord> GetHistory(int limit = 50, int offset = 0)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT l.id, l.timestamp, l.application, l.exe_path, l.outcome, l.waited_ms,
                   j.entry
            FROM launches l
            LEFT JOIN journal_entries j ON j.launch_id = l.id
            ORDER BY l.id DESC
            LIMIT $limit OFFSET $offset
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$offset", offset);

        var results = new List<LaunchRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new LaunchRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return results;
    }

    public WeeklySummary GetWeeklySummary()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COUNT(*) as total,
                SUM(CASE WHEN outcome='continued' THEN 1 ELSE 0 END) as continued,
                SUM(CASE WHEN outcome='cancelled' THEN 1 ELSE 0 END) as cancelled
            FROM launches
            WHERE timestamp >= datetime('now', '-7 days')
              AND outcome != 'pending'
            """;
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
            return new WeeklySummary(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
        return new WeeklySummary(0, 0, 0);
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS launches (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp   TEXT NOT NULL,
                application TEXT NOT NULL,
                exe_path    TEXT NOT NULL,
                outcome     TEXT NOT NULL,
                waited_ms   INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS journal_entries (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                launch_id   INTEGER NOT NULL REFERENCES launches(id),
                timestamp   TEXT NOT NULL,
                entry       TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connString);
        conn.Open();
        return conn;
    }
}

using System.IO;
using Microsoft.Data.Sqlite;
using SurfaceTensionApp.Models;

namespace SurfaceTensionApp.Services;

/// <summary>
/// SQLite database for persistent storage of all measurement runs.
/// DB file: ~/Documents/SurfaceTensionApp/measurements.db
/// </summary>
public class DatabaseService : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _conn;

    public DatabaseService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "SurfaceTensionApp");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "measurements.db");
        InitDb();
    }

    private void InitDb()
    {
        _conn = new SqliteConnection($"Data Source={_dbPath}");
        _conn.Open();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS sessions (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at  TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                name        TEXT,
                notes       TEXT
            );

            CREATE TABLE IF NOT EXISTS runs (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id      INTEGER NOT NULL,
                speed_name      TEXT NOT NULL,
                speed_mms       REAL NOT NULL,
                batch           INTEGER NOT NULL DEFAULT 1,
                run_number      INTEGER NOT NULL,
                peak_force      REAL NOT NULL,
                validated_peak  REAL,
                contact_pos     REAL,
                point_count     INTEGER NOT NULL,
                is_outlier      INTEGER NOT NULL DEFAULT 0,
                created_at      TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );

            CREATE TABLE IF NOT EXISTS run_data (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id  INTEGER NOT NULL,
                t       REAL NOT NULL,
                f       REAL NOT NULL,
                p       REAL NOT NULL,
                pr      REAL NOT NULL,
                FOREIGN KEY (run_id) REFERENCES runs(id)
            );

            CREATE INDEX IF NOT EXISTS idx_runs_session ON runs(session_id);
            CREATE INDEX IF NOT EXISTS idx_rundata_run ON run_data(run_id);
        ";
        cmd.ExecuteNonQuery();
    }

    // ─────────────────────────────────────────────
    // Session management
    // ─────────────────────────────────────────────
    public long CreateSession(string? name = null, string? notes = null)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = "INSERT INTO sessions (name, notes) VALUES ($name, $notes); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$name", name ?? $"Session {DateTime.Now:yyyy-MM-dd HH:mm}");
        cmd.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    public List<SessionInfo> GetSessions()
    {
        var list = new List<SessionInfo>();
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = @"
            SELECT s.id, s.created_at, s.name, s.notes, COUNT(r.id) as run_count
            FROM sessions s LEFT JOIN runs r ON s.id = r.session_id
            GROUP BY s.id ORDER BY s.created_at DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SessionInfo
            {
                Id = reader.GetInt64(0),
                CreatedAt = reader.GetString(1),
                Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Notes = reader.IsDBNull(3) ? "" : reader.GetString(3),
                RunCount = reader.GetInt32(4),
            });
        }
        return list;
    }

    public void DeleteSession(long sessionId)
    {
        using var tx = _conn!.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        // Delete run_data for all runs in session
        cmd.CommandText = "DELETE FROM run_data WHERE run_id IN (SELECT id FROM runs WHERE session_id=$sid)";
        cmd.Parameters.AddWithValue("$sid", sessionId);
        cmd.ExecuteNonQuery();
        cmd.CommandText = "DELETE FROM runs WHERE session_id=$sid";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "DELETE FROM sessions WHERE id=$sid";
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    // ─────────────────────────────────────────────
    // Save runs
    // ─────────────────────────────────────────────
    public void SaveRun(long sessionId, string speedName, double speedMmS, int batch,
                        int runNumber, TestRun run, bool isOutlier)
    {
        using var tx = _conn!.BeginTransaction();

        // Insert run record
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO runs (session_id, speed_name, speed_mms, batch, run_number, 
                              peak_force, validated_peak, contact_pos, point_count, is_outlier)
            VALUES ($sid, $sn, $smms, $b, $rn, $pf, $vp, $cp, $pc, $out);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$sid", sessionId);
        cmd.Parameters.AddWithValue("$sn", speedName);
        cmd.Parameters.AddWithValue("$smms", speedMmS);
        cmd.Parameters.AddWithValue("$b", batch);
        cmd.Parameters.AddWithValue("$rn", runNumber);
        cmd.Parameters.AddWithValue("$pf", run.PeakForce);
        cmd.Parameters.AddWithValue("$vp", run.ValidatedPeak.HasValue ? (object)run.ValidatedPeak.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$cp", run.ContactPosition.HasValue ? (object)run.ContactPosition.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$pc", run.PointCount);
        cmd.Parameters.AddWithValue("$out", isOutlier ? 1 : 0);
        long runId = (long)cmd.ExecuteScalar()!;

        // Bulk insert data points
        if (run.Times.Count > 0)
        {
            using var dataCmd = _conn.CreateCommand();
            dataCmd.Transaction = tx;
            // Batch insert in chunks
            for (int i = 0; i < run.Times.Count; i++)
            {
                dataCmd.CommandText = "INSERT INTO run_data (run_id, t, f, p, pr) VALUES ($rid, $t, $f, $p, $pr)";
                dataCmd.Parameters.Clear();
                dataCmd.Parameters.AddWithValue("$rid", runId);
                dataCmd.Parameters.AddWithValue("$t", run.Times[i]);
                dataCmd.Parameters.AddWithValue("$f", run.Forces[i]);
                dataCmd.Parameters.AddWithValue("$p", i < run.Positions.Count ? run.Positions[i] : 0);
                dataCmd.Parameters.AddWithValue("$pr", i < run.RelPositions.Count ? run.RelPositions[i] : 0);
                dataCmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    // ─────────────────────────────────────────────
    // Load session data
    // ─────────────────────────────────────────────
    public Dictionary<string, SpeedGroup> LoadSession(long sessionId)
    {
        var allData = new Dictionary<string, SpeedGroup>();

        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = "SELECT id, speed_name, speed_mms, batch, run_number, peak_force, validated_peak, contact_pos, point_count, is_outlier FROM runs WHERE session_id=$sid ORDER BY id";
        cmd.Parameters.AddWithValue("$sid", sessionId);

        var runInfos = new List<(long runId, string speedName, double speedMmS, int batch, int runNumber, double peakForce, double? validatedPeak, double? contactPos, int pointCount, bool isOutlier)>();

        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                runInfos.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetDouble(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetDouble(5),
                    reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    reader.GetInt32(8),
                    reader.GetInt32(9) != 0
                ));
            }
        }

        foreach (var ri in runInfos)
        {
            string key = $"{ri.speedName}_{ri.batch}";
            if (!allData.TryGetValue(key, out var group))
            {
                group = new SpeedGroup
                {
                    Key = key,
                    BaseName = ri.speedName,
                    SpeedMmS = ri.speedMmS,
                    Batch = ri.batch,
                };
                allData[key] = group;
            }

            var run = new TestRun();
            run.ValidatedPeak = ri.validatedPeak;
            run.ContactPosition = ri.contactPos;

            // Load data points
            using var dataCmd = _conn!.CreateCommand();
            dataCmd.CommandText = "SELECT t, f, p, pr FROM run_data WHERE run_id=$rid ORDER BY id";
            dataCmd.Parameters.AddWithValue("$rid", ri.runId);
            using var dr = dataCmd.ExecuteReader();
            while (dr.Read())
            {
                run.Times.Add(dr.GetDouble(0));
                run.Forces.Add(dr.GetDouble(1));
                run.Positions.Add(dr.GetDouble(2));
                run.RelPositions.Add(dr.GetDouble(3));
            }

            group.Runs.Add(run);
            group.PeakForces.Add(ri.peakForce);
        }

        // Recompute outliers
        foreach (var g in allData.Values)
            g.ComputeOutliers();

        return allData;
    }

    public string DbPath => _dbPath;

    public void Dispose()
    {
        _conn?.Close();
        _conn?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Session metadata for the session browser.</summary>
public class SessionInfo
{
    public long Id { get; init; }
    public string CreatedAt { get; init; } = "";
    public string Name { get; init; } = "";
    public string Notes { get; init; } = "";
    public int RunCount { get; init; }
    public string Display => $"{CreatedAt}  —  {Name}  ({RunCount} runs)";
}

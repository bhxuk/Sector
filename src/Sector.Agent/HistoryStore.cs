using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Sector.Shared;

namespace Sector.Agent
{
    /// <summary>
    /// Keeps the history of temperatures, fans, loads and drive throughput in SQLite.
    /// Every poll feeds a per-minute and a per-15-minute bucket (min, average, max).
    /// Minutes are kept for 7 days; 15-minute buckets for as long as the settings say (a year by default).
    /// </summary>
    public sealed class HistoryStore : IDisposable
    {
        private const int MinuteKeepDays = 7;
        private const int Quarter = 15 * 60;

        private readonly SqliteConnection _db;
        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _ids = new Dictionary<string, int>();
        private readonly Dictionary<int, Bucket> _minute = new Dictionary<int, Bucket>();
        private readonly Dictionary<int, Bucket> _quarter = new Dictionary<int, Bucket>();
        private long _minuteStart = -1;
        private long _quarterStart = -1;
        private DateTime _lastCleanup = DateTime.MinValue;

        private class Bucket
        {
            public float Min = float.MaxValue, Max = float.MinValue;
            public double Sum;
            public int Count;
            public void Add(float v) { if (v < Min) Min = v; if (v > Max) Max = v; Sum += v; Count++; }
        }

        public HistoryStore()
        {
            Directory.CreateDirectory(Constants.DataFolder);
            string path = Path.Combine(Constants.DataFolder, "history.db");
            _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Private }.ToString());
            _db.Open();
            Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;");
            Exec(@"
CREATE TABLE IF NOT EXISTS sensors (id INTEGER PRIMARY KEY, key TEXT NOT NULL UNIQUE, name TEXT, type TEXT, hardware TEXT, first_seen INTEGER, last_seen INTEGER);
CREATE TABLE IF NOT EXISTS m1  (sensor INTEGER NOT NULL, t INTEGER NOT NULL, mn REAL, av REAL, mx REAL, PRIMARY KEY (sensor, t)) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS m15 (sensor INTEGER NOT NULL, t INTEGER NOT NULL, mn REAL, av REAL, mx REAL, PRIMARY KEY (sensor, t)) WITHOUT ROWID;
");
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT id, key FROM sensors";
            using var r = cmd.ExecuteReader();
            while (r.Read()) _ids[r.GetString(1)] = r.GetInt32(0);
        }

        /// <summary>Which readings are worth keeping. Clocks and voltages flicker constantly and are rarely wanted later.</summary>
        public static bool ShouldRecord(HardwareNode hw, SensorReading s)
        {
            if (!s.Value.HasValue) return false;
            switch (s.Type)
            {
                case "Temperature":
                    return !(hw.Type == "Storage" && (s.Name.StartsWith("Warning") || s.Name.StartsWith("Critical")));
                case "Fan":
                case "Control":
                    return true;
                case "Power":
                    return hw.Type == "Cpu" || hw.Type.StartsWith("Gpu") || hw.Type == "Psu";
                case "Load":
                    return (hw.Type == "Cpu" && s.Name == "CPU Total")
                        || (hw.Type.StartsWith("Gpu") && s.Name == "GPU Core")
                        || (hw.Type == "Memory" && s.Name == "Memory")
                        || (hw.Type == "Storage" && (s.Name == "Used Space" || s.Name == "Total Activity"));
                case "Throughput":
                    return hw.Type == "Storage";
                default:
                    return false;
            }
        }

        public void Add(Snapshot snap)
        {
            long now = new DateTimeOffset(snap.TimeUtc).ToUnixTimeSeconds();
            long minute = now - now % 60;
            long quarter = now - now % Quarter;

            lock (_gate)
            {
                if (_minuteStart >= 0 && minute != _minuteStart) Flush("m1", _minute, _minuteStart);
                if (_quarterStart >= 0 && quarter != _quarterStart) Flush("m15", _quarter, _quarterStart);
                _minuteStart = minute;
                _quarterStart = quarter;

                using var tx = _db.BeginTransaction();
                foreach (var hw in snap.Hardware)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (!ShouldRecord(hw, s)) continue;
                        int id = IdFor(s, hw, now, tx);
                        float v = s.Value.Value;
                        if (!_minute.TryGetValue(id, out var b1)) _minute[id] = b1 = new Bucket();
                        if (!_quarter.TryGetValue(id, out var b2)) _quarter[id] = b2 = new Bucket();
                        b1.Add(v);
                        b2.Add(v);
                    }
                }
                tx.Commit();

                if (DateTime.UtcNow - _lastCleanup > TimeSpan.FromHours(1)) Cleanup(now);
            }
        }

        private int IdFor(SensorReading s, HardwareNode hw, long now, SqliteTransaction tx)
        {
            if (_ids.TryGetValue(s.Id, out int id)) return id;
            using var cmd = _db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO sensors (key, name, type, hardware, first_seen, last_seen) VALUES ($k, $n, $t, $h, $now, $now) " +
                              "ON CONFLICT(key) DO UPDATE SET name = excluded.name, hardware = excluded.hardware; SELECT id FROM sensors WHERE key = $k;";
            cmd.Parameters.AddWithValue("$k", s.Id);
            cmd.Parameters.AddWithValue("$n", s.Name);
            cmd.Parameters.AddWithValue("$t", s.Type);
            cmd.Parameters.AddWithValue("$h", hw.Name);
            cmd.Parameters.AddWithValue("$now", now);
            id = Convert.ToInt32(cmd.ExecuteScalar());
            _ids[s.Id] = id;
            return id;
        }

        private void Flush(string table, Dictionary<int, Bucket> buckets, long t)
        {
            if (buckets.Count == 0) return;
            try
            {
                using var tx = _db.BeginTransaction();
                using var cmd = _db.CreateCommand();
                cmd.Transaction = tx;
                // A bucket can be written twice if the agent restarts part-way through it: merge rather than overwrite.
                cmd.CommandText = $"INSERT INTO {table} (sensor, t, mn, av, mx) VALUES ($s, $t, $mn, $av, $mx) " +
                                  "ON CONFLICT(sensor, t) DO UPDATE SET mn = MIN(mn, excluded.mn), mx = MAX(mx, excluded.mx), av = (av + excluded.av) / 2";
                var ps = cmd.Parameters.Add("$s", SqliteType.Integer);
                var pt = cmd.Parameters.Add("$t", SqliteType.Integer);
                var pmn = cmd.Parameters.Add("$mn", SqliteType.Real);
                var pav = cmd.Parameters.Add("$av", SqliteType.Real);
                var pmx = cmd.Parameters.Add("$mx", SqliteType.Real);
                foreach (var kv in buckets)
                {
                    if (kv.Value.Count == 0) continue;
                    ps.Value = kv.Key;
                    pt.Value = t;
                    pmn.Value = kv.Value.Min;
                    pav.Value = kv.Value.Sum / kv.Value.Count;
                    pmx.Value = kv.Value.Max;
                    cmd.ExecuteNonQuery();
                }
                using (var seen = _db.CreateCommand())
                {
                    seen.Transaction = tx;
                    seen.CommandText = "UPDATE sensors SET last_seen = $t WHERE id IN (" + string.Join(",", buckets.Keys) + ")";
                    seen.Parameters.AddWithValue("$t", t);
                    seen.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch (Exception ex) { Log.Write("History write: " + ex.Message); }
            buckets.Clear();
        }

        private void Cleanup(long now)
        {
            _lastCleanup = DateTime.UtcNow;
            try
            {
                int keep = Math.Max(7, ConfigStore.Current?.HistoryDays ?? 365);
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "DELETE FROM m1 WHERE t < $a; DELETE FROM m15 WHERE t < $b;";
                cmd.Parameters.AddWithValue("$a", now - MinuteKeepDays * 86400L);
                cmd.Parameters.AddWithValue("$b", now - keep * 86400L);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { Log.Write("History cleanup: " + ex.Message); }
        }

        public List<HistorySensor> Sensors()
        {
            var list = new List<HistorySensor>();
            lock (_gate)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT key, name, type, hardware, first_seen, last_seen FROM sensors ORDER BY hardware, type, name";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new HistorySensor
                    {
                        Id = r.GetString(0),
                        Name = r.IsDBNull(1) ? "" : r.GetString(1),
                        Type = r.IsDBNull(2) ? "" : r.GetString(2),
                        Hardware = r.IsDBNull(3) ? "" : r.GetString(3),
                        FirstSeen = r.IsDBNull(4) ? 0 : r.GetInt64(4),
                        LastSeen = r.IsDBNull(5) ? 0 : r.GetInt64(5)
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// Returns up to about 600 points per sensor for the range, from the minute table for short ranges
        /// and the 15-minute table for long ones. The bucket still in progress is included so charts reach "now".
        /// </summary>
        public List<HistorySeries> Query(IEnumerable<string> keys, DateTime fromUtc, DateTime toUtc)
        {
            long from = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
            long to = new DateTimeOffset(DateTime.SpecifyKind(toUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
            if (to <= from) to = from + 3600;
            long range = to - from;

            bool useMinutes = range <= 2 * 86400 && from >= DateTimeOffset.UtcNow.ToUnixTimeSeconds() - MinuteKeepDays * 86400L;
            string table = useMinutes ? "m1" : "m15";
            int baseStep = useMinutes ? 60 : Quarter;
            long step = Math.Max(baseStep, range / 600);
            step = (step + baseStep - 1) / baseStep * baseStep;

            var result = new List<HistorySeries>();
            var meta = Sensors().ToDictionary(s => s.Id);

            lock (_gate)
            {
                foreach (string key in keys.Distinct())
                {
                    if (!_ids.TryGetValue(key, out int id)) continue;
                    meta.TryGetValue(key, out var m);
                    var series = new HistorySeries { Id = key, Name = m?.Name ?? "", Type = m?.Type ?? "", Hardware = m?.Hardware ?? "", StepSeconds = (int)step };

                    using var cmd = _db.CreateCommand();
                    cmd.CommandText = $"SELECT (t / $step) * $step AS b, MIN(mn), AVG(av), MAX(mx) FROM {table} " +
                                      "WHERE sensor = $s AND t >= $a AND t <= $z GROUP BY b ORDER BY b";
                    cmd.Parameters.AddWithValue("$step", step);
                    cmd.Parameters.AddWithValue("$s", id);
                    cmd.Parameters.AddWithValue("$a", from);
                    cmd.Parameters.AddWithValue("$z", to);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            series.Points.Add(new HistoryPoint
                            {
                                T = r.GetInt64(0),
                                Min = (float)r.GetDouble(1),
                                Avg = (float)r.GetDouble(2),
                                Max = (float)r.GetDouble(3)
                            });
                        }
                    }

                    var live = useMinutes ? (_minute, _minuteStart) : (_quarter, _quarterStart);
                    if (live.Item2 >= from && live.Item2 <= to && live.Item1.TryGetValue(id, out var b) && b.Count > 0)
                    {
                        long t = live.Item2 / step * step;
                        if (series.Points.Count == 0 || series.Points[^1].T < t)
                            series.Points.Add(new HistoryPoint { T = t, Min = b.Min, Avg = (float)(b.Sum / b.Count), Max = b.Max });
                    }
                    result.Add(series);
                }
            }
            return result;
        }

        private void Exec(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                Flush("m1", _minute, _minuteStart);
                Flush("m15", _quarter, _quarterStart);
                _db.Dispose();
            }
        }
    }
}

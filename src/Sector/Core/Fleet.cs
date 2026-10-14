using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Sector.Shared;

namespace Sector.Core
{
    /// <summary>The last few minutes of one sensor, for sparklines.</summary>
    public class Recent
    {
        private readonly float[] _v;
        private int _start, _count;
        public Recent(int capacity) { _v = new float[capacity]; }

        public void Add(float value)
        {
            if (_count < _v.Length) _v[(_start + _count++) % _v.Length] = value;
            else { _v[_start] = value; _start = (_start + 1) % _v.Length; }
        }

        public int Count => _count;

        public IEnumerable<float> Values()
        {
            for (int i = 0; i < _count; i++) yield return _v[(_start + i) % _v.Length];
        }

        public void Prepend(IEnumerable<float> older)
        {
            var all = older.Concat(Values().ToList()).ToList();
            _start = 0;
            _count = 0;
            foreach (var x in all.Skip(Math.Max(0, all.Count - _v.Length))) Add(x);
        }
    }

    /// <summary>One PC as Sector sees it: its last reading, whether it's reachable, and recent values.</summary>
    public class MachineState
    {
        public MachineEntry Entry { get; }
        public AgentClient Client { get; }
        public Snapshot Snapshot { get; set; }
        public AgentInfo Info { get; set; }
        public bool Online { get; set; }
        public bool EverOnline { get; set; }
        public string Error { get; set; } = "";
        public DateTime LastOk { get; set; } = DateTime.MinValue;
        public DateTime FailingSince { get; set; } = DateTime.MinValue;
        public bool Seeded { get; set; }
        public Dictionary<string, Recent> RecentValues { get; } = new Dictionary<string, Recent>();

        public MachineState(MachineEntry entry)
        {
            Entry = entry;
            Client = new AgentClient(entry);
        }

        public string Name => !string.IsNullOrWhiteSpace(Entry.Name) ? Entry.Name : Snapshot?.Machine ?? Entry.Address;

        public IEnumerable<float> RecentFor(string sensorId) =>
            sensorId != null && RecentValues.TryGetValue(sensorId, out var r) ? r.Values() : Enumerable.Empty<float>();

        public DriveInfo HottestDrive => Snapshot?.Drives.Where(d => d.Temperature.HasValue).OrderByDescending(d => d.Temperature).FirstOrDefault();
    }

    /// <summary>Polls every PC Sector knows about and tells the window and the tray when something changes.</summary>
    public static class Fleet
    {
        public static List<MachineState> Machines { get; } = new List<MachineState>();
        public static event Action<MachineState> Updated;
        public static event Action ListChanged;

        private static CancellationTokenSource _cts;
        private const int RecentCapacity = 450;   // 15 minutes at the default 2 seconds

        public static MachineState Selected =>
            Machines.FirstOrDefault(m => m.Entry.Id == Settings.Current.SelectedMachine) ?? Machines.FirstOrDefault();

        public static void Start()
        {
            Machines.Clear();
            foreach (var e in Settings.Current.Machines) Machines.Add(new MachineState(e));
            _cts = new CancellationTokenSource();
            foreach (var m in Machines.ToList()) StartLoop(m, _cts.Token);
        }

        public static void Stop() => _cts?.Cancel();

        public static MachineState Add(MachineEntry e)
        {
            Settings.Current.Machines.Add(e);
            Settings.Save();
            var m = new MachineState(e);
            Machines.Add(m);
            StartLoop(m, _cts.Token);
            ListChanged?.Invoke();
            return m;
        }

        public static void Remove(MachineState m)
        {
            if (m.Entry.IsLocal) return;
            Settings.Current.Machines.RemoveAll(x => x.Id == m.Entry.Id);
            if (Settings.Current.SelectedMachine == m.Entry.Id) Settings.Current.SelectedMachine = "";
            Settings.Save();
            Machines.Remove(m);
            m.Client.Dispose();
            ListChanged?.Invoke();
        }

        public static void RaiseListChanged() => ListChanged?.Invoke();

        private static void StartLoop(MachineState m, CancellationToken stop)
        {
            _ = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested && Machines.Contains(m))
                {
                    await PollOnce(m);
                    int secs = Math.Max(1, Settings.Current.RefreshSeconds);
                    // Machines that aren't on screen still get checked for alerts, just less often.
                    if (m != Selected && !m.Entry.IsLocal) secs = Math.Max(secs, 5);
                    if (!m.Online) secs = Math.Max(secs, 10);
                    try { await Task.Delay(TimeSpan.FromSeconds(secs), stop); } catch (TaskCanceledException) { return; }
                }
            });
        }

        public static async Task PollOnce(MachineState m)
        {
            try
            {
                if (m.Info == null || DateTime.UtcNow - m.LastOk > TimeSpan.FromMinutes(5)) m.Info = await m.Client.Info();
                var snap = await m.Client.Snapshot();
                if (!m.Seeded) await Seed(m, snap);
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    m.Snapshot = snap;
                    m.Online = true;
                    m.EverOnline = true;
                    m.Error = "";
                    m.LastOk = DateTime.UtcNow;
                    m.FailingSince = DateTime.MinValue;
                    foreach (var hw in snap.Hardware)
                        foreach (var s in hw.Sensors)
                        {
                            if (!s.Value.HasValue) continue;
                            if (!m.RecentValues.TryGetValue(s.Id, out var r)) m.RecentValues[s.Id] = r = new Recent(RecentCapacity);
                            r.Add(s.Value.Value);
                        }
                    Updated?.Invoke(m);
                });
            }
            catch (Exception ex)
            {
                string why = Explain(m, ex);
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    m.Online = false;
                    m.Error = why;
                    if (m.FailingSince == DateTime.MinValue) m.FailingSince = DateTime.UtcNow;
                    Updated?.Invoke(m);
                });
            }
        }

        private static string Explain(MachineState m, Exception ex)
        {
            if (ex is AgentException) return ex.Message;
            if (m.Entry.IsLocal)
            {
                string status = LocalAgent.Status();
                return status switch
                {
                    "Not installed" => "The Sector agent isn't installed on this PC.",
                    "Stopped" => "The Sector agent is stopped.",
                    "Starting" => "The Sector agent is starting...",
                    _ => "The Sector agent isn't answering yet."
                };
            }
            if (ex is TaskCanceledException) return "No answer from " + m.Entry.Address + ". Is it on and connected?";
            return "Can't reach " + m.Entry.Address + ": " + ex.Message;
        }

        /// <summary>Fills the sparklines with the last few minutes from the agent's history, so they aren't empty at start-up.</summary>
        private static async Task Seed(MachineState m, Snapshot snap)
        {
            m.Seeded = true;
            try
            {
                var ids = snap.Drives.Select(d => d.TemperatureSensor)
                              .Concat(new[] { snap.Summary.CpuTempSensor, snap.Summary.GpuTempSensor })
                              .Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                if (ids.Count == 0) return;
                var series = await m.Client.History(ids, DateTime.UtcNow.AddMinutes(-15), DateTime.UtcNow);
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    foreach (var s in series)
                    {
                        if (s.Points.Count == 0) continue;
                        // History is one point a minute; repeat each to match the live rate so the line isn't squashed.
                        int per = Math.Max(1, 60 / Math.Max(1, Settings.Current.RefreshSeconds));
                        var values = s.Points.SelectMany(p => Enumerable.Repeat(p.Avg, per));
                        if (!m.RecentValues.TryGetValue(s.Id, out var r)) m.RecentValues[s.Id] = r = new Recent(RecentCapacity);
                        r.Prepend(values);
                    }
                });
            }
            catch { }
        }
    }
}

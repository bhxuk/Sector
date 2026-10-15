using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sector.Core
{
    /// <summary>A PC that Sector shows. "This PC" is always first; others (like the NAS) are added by address and key.</summary>
    public class MachineEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Address { get; set; } = "";
        public int Port { get; set; } = Shared.Constants.DefaultPort;
        public string Key { get; set; } = "";          // encrypted for this Windows user
        public bool IsLocal { get; set; }
        public bool Alerts { get; set; } = true;
    }

    public class AlertSettings
    {
        public bool Enabled { get; set; } = true;
        public float HddWarn { get; set; } = 45;
        public float HddCritical { get; set; } = 50;
        public float SsdWarn { get; set; } = 65;
        public float SsdCritical { get; set; } = 75;
        public float CpuWarn { get; set; } = 85;
        public float CpuCritical { get; set; } = 95;
        public float GpuWarn { get; set; } = 83;
        public float GpuCritical { get; set; } = 90;
        public bool Offline { get; set; } = true;
        public int OfflineMinutes { get; set; } = 3;
        public bool Health { get; set; } = true;
        public bool FanStopped { get; set; } = false;
        public int RepeatMinutes { get; set; } = 30;
        /// <summary>Per-drive warning temperature, keyed by machine id + serial.</summary>
        public Dictionary<string, float> DriveWarn { get; set; } = new Dictionary<string, float>();
    }

    public class AlertEvent
    {
        public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
        public string Machine { get; set; } = "";
        public string Level { get; set; } = "Warning";   // Warning, Critical, Info, Resolved
        public string Title { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public class Settings
    {
        public static Settings Current { get; private set; } = new Settings();

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sector");
        private static string FilePath => Path.Combine(Folder, "settings.json");

        public string Theme { get; set; } = "Dark";          // Dark, Light, System
        public string Accent { get; set; } = "Amber";
        public bool GlassWindow { get; set; } = true;
        public bool StartWithWindows { get; set; } = true;
        public bool Fahrenheit { get; set; } = false;
        public int RefreshSeconds { get; set; } = 2;
        public bool FirstRun { get; set; } = true;
        /// <summary>The version that last ran, so Sector can say when an update has finished.</summary>
        public string LastVersion { get; set; } = "";
        public bool CheckForUpdates { get; set; } = true;
        public bool AutoUpdate { get; set; } = false;
        public string TrayShows { get; set; } = "Hottest drive";   // Hottest drive, CPU, GPU, Nothing
        public string TrayMachine { get; set; } = "";               // machine id; "" = the first one with drives
        public string SelectedMachine { get; set; } = "";
        public bool CompactDrives { get; set; } = false;
        public List<MachineEntry> Machines { get; set; } = new List<MachineEntry>();
        public AlertSettings Alerts { get; set; } = new AlertSettings();
        public List<AlertEvent> AlertLog { get; set; } = new List<AlertEvent>();
        /// <summary>Your own names for drives ("Bay 1", "Films"), keyed by machine id + serial.</summary>
        public Dictionary<string, string> DriveNames { get; set; } = new Dictionary<string, string>();
        /// <summary>Last health seen per drive, so a drive that was already flagged doesn't alert again on every start.</summary>
        public Dictionary<string, string> HealthSeen { get; set; } = new Dictionary<string, string>();
        /// <summary>Sensors hidden from the overview, by machine id + sensor id.</summary>
        public List<string> Hidden { get; set; } = new List<string>();
        public double WindowWidth { get; set; } = 1240;
        public double WindowHeight { get; set; } = 820;

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch (Exception ex)
            {
                Log.Write("Settings unreadable, starting fresh: " + ex.Message);
                try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
                Current = new Settings();
            }
            Current.Machines ??= new List<MachineEntry>();
            Current.Alerts ??= new AlertSettings();
            Current.Alerts.DriveWarn ??= new Dictionary<string, float>();
            Current.AlertLog ??= new List<AlertEvent>();
            Current.DriveNames ??= new Dictionary<string, string>();
            Current.Hidden ??= new List<string>();
            Current.HealthSeen ??= new Dictionary<string, string>();
            if (!Current.Machines.Any(m => m.IsLocal))
                Current.Machines.Insert(0, new MachineEntry { Id = "local", Name = "This PC", Address = "127.0.0.1", IsLocal = true });
            if (Current.RefreshSeconds < 1) Current.RefreshSeconds = 2;
        }

        private static readonly object Gate = new object();

        public static void Save()
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    if (Current.AlertLog.Count > 300) Current.AlertLog = Current.AlertLog.Skip(Current.AlertLog.Count - 300).ToList();
                    string tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
                    File.Move(tmp, FilePath, true);
                }
                catch (Exception ex) { Log.Write("Settings save failed: " + ex.Message); }
            }
        }

        public string DriveName(string machineId, Shared.DriveInfo d)
        {
            if (d == null) return "";
            if (DriveNames.TryGetValue(DriveKey(machineId, d), out var n) && !string.IsNullOrWhiteSpace(n)) return n;
            return d.Model;
        }

        public static string DriveKey(string machineId, Shared.DriveInfo d) =>
            machineId + "|" + (string.IsNullOrEmpty(d.Serial) ? d.Model + "#" + d.Number : d.Serial);
    }

    public static class Log
    {
        private static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Settings.Folder);
                    string path = Path.Combine(Settings.Folder, "sector.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    /// <summary>Keeps keys for other PCs unreadable to other Windows users.</summary>
    public static class Secret
    {
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                var bytes = System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(plain), null,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return "dp:" + Convert.ToBase64String(bytes);
            }
            catch { return plain; }
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            if (!stored.StartsWith("dp:")) return stored;
            try
            {
                var bytes = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(3)), null,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sector.Shared;

namespace Sector.Agent
{
    /// <summary>Loads, creates and saves the agent's settings, and keeps the firewall rule in step with them.</summary>
    public static class ConfigStore
    {
        private static readonly object Gate = new object();
        public static AgentConfig Current { get; private set; }

        public const string FirewallRule = "Sector Agent";

        public static AgentConfig Load()
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Constants.DataFolder);
                var c = AgentConfig.Load();
                bool changed = false;
                if (string.IsNullOrWhiteSpace(c.Key)) { c.Key = NewKey(); changed = true; }
                if (c.Port <= 0 || c.Port > 65535) { c.Port = Constants.DefaultPort; changed = true; }
                if (c.HistoryDays < 7) { c.HistoryDays = 7; changed = true; }
                if (c.PollMilliseconds < 1000) { c.PollMilliseconds = 1000; changed = true; }
                Current = c;
                if (changed || !File.Exists(Constants.AgentConfigPath)) Save();
                return c;
            }
        }

        public static void Save()
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Constants.DataFolder);
                string tmp = Constants.AgentConfigPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, new JsonSerializerOptions(Json.Options) { WriteIndented = true }));
                File.Move(tmp, Constants.AgentConfigPath, true);
            }
        }

        /// <summary>A pairing key that's easy to read out and type: five groups of four letters and digits.</summary>
        public static string NewKey()
        {
            const string alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";
            var sb = new StringBuilder();
            for (int i = 0; i < 20; i++)
            {
                if (i > 0 && i % 4 == 0) sb.Append('-');
                sb.Append(alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]);
            }
            return sb.ToString();
        }

        public static bool KeyMatches(string given)
        {
            if (string.IsNullOrEmpty(given) || Current == null) return false;
            var a = Encoding.UTF8.GetBytes(Normalise(given));
            var b = Encoding.UTF8.GetBytes(Normalise(Current.Key));
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        private static string Normalise(string key) => key.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();

        /// <summary>Opens the port to this network and Tailscale only (never the whole internet), or closes it.</summary>
        public static void ApplyFirewall(bool allow, int port)
        {
            RunNetsh($"advfirewall firewall delete rule name=\"{FirewallRule}\"");
            if (!allow) return;
            RunNetsh($"advfirewall firewall add rule name=\"{FirewallRule}\" dir=in action=allow protocol=TCP localport={port} " +
                     "remoteip=LocalSubnet,100.64.0.0/10,fd7a:115c:a1e0::/48 profile=any " +
                     "description=\"Lets your other PCs read this PC's stats in Sector (local network and Tailscale only)\"");
        }

        private static void RunNetsh(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh.exe", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(15000);
            }
            catch (Exception ex) { Log.Write("netsh: " + ex.Message); }
        }
    }
}

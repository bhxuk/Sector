using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sector.Shared;

namespace Sector.Agent
{
    /// <summary>
    /// Finds new versions on GitHub, downloads Setup.exe and runs it silently. The agent runs as a Windows
    /// service, so this needs no permission prompt, works on a NAS nobody is signed in to, and can be
    /// started from another PC. Only the official release asset from github.com/bhxuk/sector is ever run.
    /// </summary>
    public static class Updater
    {
        private static readonly object Gate = new object();
        private static readonly UpdateStatus Status = new UpdateStatus { Current = Program.Version };
        private static string _setupUrl;
        private static int _busy;

        private static string Folder => Path.Combine(Constants.DataFolder, "updates");

        public static UpdateStatus Snapshot()
        {
            lock (Gate)
            {
                return new UpdateStatus
                {
                    State = Status.State,
                    Current = Program.Version,
                    Latest = Status.Latest,
                    Available = Status.Available,
                    Progress = Status.Progress,
                    Message = Status.Message,
                    Auto = ConfigStore.Current?.AutoUpdate ?? false,
                    CheckedUtc = Status.CheckedUtc
                };
            }
        }

        private static void Set(string state, string message = null, int? progress = null)
        {
            lock (Gate)
            {
                Status.State = state;
                if (message != null) Status.Message = message;
                if (progress.HasValue) Status.Progress = progress.Value;
            }
        }

        private static HttpClient Http(TimeSpan timeout)
        {
            var h = new HttpClient { Timeout = timeout };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Sector-Agent/" + Program.Version);
            return h;
        }

        /// <summary>Asks GitHub for the newest release. Returns true if it's newer than this agent.</summary>
        public static async Task<bool> Check()
        {
            if (Status.State == "Downloading" || Status.State == "Installing") return Status.Available;
            Set("Checking");
            try
            {
                using var http = Http(TimeSpan.FromSeconds(20));
                using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Constants.Repo}/releases/latest"));
                string latest = (doc.RootElement.GetProperty("tag_name").GetString() ?? "").TrimStart('v', 'V');
                string url = null;
                if (doc.RootElement.TryGetProperty("assets", out var assets))
                    foreach (var a in assets.EnumerateArray())
                        if ((a.GetProperty("name").GetString() ?? "").Equals("Sector-Setup.exe", StringComparison.OrdinalIgnoreCase))
                            url = a.GetProperty("browser_download_url").GetString();
                bool newer = Version.TryParse(latest, out var lv) && Version.TryParse(Program.Version, out var cv) && lv > cv && IsOfficial(url);
                lock (Gate)
                {
                    Status.Latest = latest;
                    Status.Available = newer;
                    Status.CheckedUtc = DateTime.UtcNow;
                    Status.State = "Idle";
                    Status.Message = newer ? $"Sector {latest} is available." : "Sector is up to date.";
                    _setupUrl = newer ? url : null;
                }
                return newer;
            }
            catch (Exception ex)
            {
                Set("Idle", "Couldn't check for updates: " + ex.Message);
                return false;
            }
        }

        /// <summary>Only ever install the Setup.exe attached to an official release of this repository.</summary>
        private static bool IsOfficial(string url) =>
            !string.IsNullOrEmpty(url) &&
            url.StartsWith($"https://github.com/{Constants.Repo}/releases/download/", StringComparison.OrdinalIgnoreCase);

        /// <summary>Downloads and installs the newest version. Returns straight away; progress shows in the status.</summary>
        public static bool Start()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
            _ = Task.Run(async () =>
            {
                try { await Install(); }
                catch (Exception ex)
                {
                    Log.Write("Update failed: " + ex);
                    Set("Failed", "The update didn't install: " + ex.Message);
                }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
            return true;
        }

        private static async Task Install()
        {
            if (_setupUrl == null && !await Check())
            {
                Set("Idle", "Sector is already up to date.");
                return;
            }
            string url, version;
            lock (Gate) { url = _setupUrl; version = Status.Latest; }
            if (!IsOfficial(url)) throw new InvalidOperationException("The download link isn't an official Sector release.");

            Directory.CreateDirectory(Folder);
            foreach (var old in Directory.GetFiles(Folder, "Sector-Setup-*.exe")) { try { File.Delete(old); } catch { } }
            string path = Path.Combine(Folder, $"Sector-Setup-{version}.exe");

            Set("Downloading", $"Downloading Sector {version}...", 0);
            Log.Write($"Downloading update {version}.");
            using (var http = Http(TimeSpan.FromMinutes(15)))
            using (var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                res.EnsureSuccessStatusCode();
                long total = res.Content.Headers.ContentLength ?? 0;
                await using var src = await res.Content.ReadAsStreamAsync();
                await using var dst = File.Create(path);
                var buf = new byte[81920];
                long done = 0;
                int read;
                while ((read = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read));
                    done += read;
                    if (total > 0) Set("Downloading", null, (int)(done * 100 / total));
                }
                if (total > 0 && done != total) throw new IOException("The download was cut short.");
            }

            try { if (File.Exists(Constants.UpdatedMarker)) File.Delete(Constants.UpdatedMarker); } catch { }

            // Tell any open Sector windows first, so they can close and arrange to reopen after the install.
            Set("Installing", $"Installing Sector {version}. Sector will reopen in a moment.", 100);
            Log.Write($"Installing update {version}.");
            await Task.Delay(TimeSpan.FromSeconds(6));

            Process.Start(new ProcessStartInfo(path,
                $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOAPP /LOG=\"{Path.Combine(Folder, "install.log")}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Folder
            });
            // Setup stops this service, replaces the files and starts the new version.
        }

        /// <summary>Checks a minute after start, then every 6 hours; installs on its own if automatic updates are on.</summary>
        public static async Task RunSchedule(CancellationToken stop)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(1), stop); } catch (TaskCanceledException) { return; }
            while (!stop.IsCancellationRequested)
            {
                bool newer = await Check();
                if (newer && ConfigStore.Current.AutoUpdate)
                {
                    Log.Write("Installing update automatically.");
                    Start();
                }
                try { await Task.Delay(TimeSpan.FromHours(6), stop); } catch (TaskCanceledException) { return; }
            }
        }
    }
}

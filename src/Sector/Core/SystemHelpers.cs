using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Sector.Core
{
    public static class Native
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        public static void UseDarkTitleBar(IntPtr hwnd)
        {
            if (Theme.IsLight) return;
            try
            {
                int on = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
            }
            catch { }
        }

        public static void RoundCorners(IntPtr hwnd)
        {
            try
            {
                int round = 2;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            }
            catch { }
        }
    }

    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void Apply(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled)
                    key.SetValue("Sector", "\"" + Environment.ProcessPath + "\" --background");
                else if (key.GetValue("Sector") != null)
                    key.DeleteValue("Sector");
            }
            catch (Exception ex) { Log.Write("Startup registry failed: " + ex.Message); }
        }
    }

    public static class IconCache
    {
        private static ImageSource _app;

        public static ImageSource AppIcon()
        {
            if (_app != null) return _app;
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/sector.ico", UriKind.Absolute);
                var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapFrame best = decoder.Frames[0];
                foreach (var f in decoder.Frames) if (f.PixelWidth > best.PixelWidth) best = f;
                _app = best;
            }
            catch { }
            return _app;
        }
    }

    public static class Shell
    {
        public static void Open(string target, string args = null)
        {
            try
            {
                var psi = new ProcessStartInfo(target) { UseShellExecute = true };
                if (!string.IsNullOrEmpty(args)) psi.Arguments = args;
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Write("Open failed for " + target + ": " + ex.Message); }
        }

        /// <summary>Runs a program as administrator (Windows asks first). Returns false if the person said no.</summary>
        public static async Task<bool> RunElevated(string exe, string args, bool wait = true)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
                if (p != null && wait) await p.WaitForExitAsync();
                return true;
            }
            catch (System.ComponentModel.Win32Exception) { return false; }   // UAC prompt cancelled
            catch (Exception ex) { Log.Write("Elevated run failed: " + ex.Message); return false; }
        }
    }

    /// <summary>The Sector agent service on this PC: is it there, is it running, start it.</summary>
    public static class LocalAgent
    {
        public static string Folder => AppContext.BaseDirectory;
        public static string AgentExe => Path.Combine(Folder, "Sector.Agent.exe");
        public static string PawnIoSetup => Path.Combine(Folder, "PawnIO_setup.exe");

        public static string Status()
        {
            try
            {
                using var sc = new ServiceController(Shared.Constants.ServiceName);
                return sc.Status switch
                {
                    ServiceControllerStatus.Running => "Running",
                    ServiceControllerStatus.StartPending => "Starting",
                    ServiceControllerStatus.Stopped => "Stopped",
                    _ => sc.Status.ToString()
                };
            }
            catch { return "Not installed"; }
        }

        /// <summary>Installs (if needed) and starts the service. One UAC prompt.</summary>
        public static Task<bool> InstallAndStart()
        {
            string exe = AgentExe.Replace("\"", "");
            string script =
                $"sc.exe query {Shared.Constants.ServiceName} >nul 2>&1 || sc.exe create {Shared.Constants.ServiceName} binPath= \"\\\"{exe}\\\"\" start= delayed-auto DisplayName= \"Sector Agent\" & " +
                $"sc.exe description {Shared.Constants.ServiceName} \"Reads this PC's temperatures, fans and drive health for Sector.\" & " +
                $"sc.exe failure {Shared.Constants.ServiceName} reset= 86400 actions= restart/3000/restart/5000/restart/30000 & " +
                $"sc.exe start {Shared.Constants.ServiceName}";
            return Shell.RunElevated("cmd.exe", "/c " + script);
        }

        public static bool PawnIoBundled => File.Exists(PawnIoSetup);

        /// <summary>Installs the PawnIO driver (needed for CPU temperatures and fan speeds), then restarts the agent so it loads it.</summary>
        public static async Task<bool> InstallPawnIo()
        {
            bool ok;
            if (PawnIoBundled)
                ok = await Shell.RunElevated(PawnIoSetup, "-install");
            else
                ok = await Shell.RunElevated("cmd.exe", "/c winget install -e --id namazso.PawnIO --accept-package-agreements --accept-source-agreements");
            if (!ok) return false;
            await Shell.RunElevated("cmd.exe", $"/c sc.exe stop {Shared.Constants.ServiceName} & timeout /t 3 /nobreak >nul & sc.exe start {Shared.Constants.ServiceName}");
            return true;
        }
    }

    public static class Updates
    {
        public const string Repo = "bhxuk/sector";
        public static string LatestVersion { get; private set; }
        public static string LatestUrl { get; private set; } = "https://github.com/" + Repo + "/releases/latest";
        public static string LatestSetupUrl { get; private set; }

        public static Version Current => typeof(Updates).Assembly.GetName().Version ?? new Version(1, 0, 0);
        public static string CurrentText => Current.Major + "." + Current.Minor + "." + Current.Build;

        public static async Task<bool> Check()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Sector/" + CurrentText);
                using var doc = JsonDocument.Parse(await http.GetStringAsync("https://api.github.com/repos/" + Repo + "/releases/latest"));
                string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                LatestUrl = doc.RootElement.GetProperty("html_url").GetString() ?? LatestUrl;
                LatestSetupUrl = null;
                if (doc.RootElement.TryGetProperty("assets", out var assets))
                    foreach (var a in assets.EnumerateArray())
                        if ((a.GetProperty("name").GetString() ?? "").Equals("Sector-Setup.exe", StringComparison.OrdinalIgnoreCase))
                            LatestSetupUrl = a.GetProperty("browser_download_url").GetString();
                LatestVersion = tag.TrimStart('v', 'V');
                return Version.TryParse(LatestVersion, out var v) && v > new Version(Current.Major, Current.Minor, Math.Max(0, Current.Build));
            }
            catch (Exception ex)
            {
                Log.Write("Update check failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Downloads the newest Setup.exe and runs it quietly. The installer closes Sector, updates it and starts it again.</summary>
        public static async Task<bool> DownloadAndInstall(Action<string> status = null)
        {
            try
            {
                if (string.IsNullOrEmpty(LatestSetupUrl) && !await Check()) return false;
                if (string.IsNullOrEmpty(LatestSetupUrl)) return false;
                status?.Invoke("Downloading Sector " + LatestVersion + "...");
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Sector/" + CurrentText);
                var bytes = await http.GetByteArrayAsync(LatestSetupUrl);
                string dir = Path.Combine(Path.GetTempPath(), "Sector");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "Sector-Setup-" + LatestVersion + ".exe");
                await File.WriteAllBytesAsync(path, bytes);
                status?.Invoke("Installing...");
                Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Update failed: " + ex.Message);
                status?.Invoke("Couldn't update: " + ex.Message);
                return false;
            }
        }
    }
}

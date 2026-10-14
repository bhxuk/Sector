using System;
using System.Globalization;
using System.Windows.Media;
using Sector.Shared;

namespace Sector.Core
{
    /// <summary>Turns raw readings into the words and colours people see.</summary>
    public static class Format
    {
        private static CultureInfo C => CultureInfo.CurrentCulture;

        public static bool F => Settings.Current.Fahrenheit;

        public static double ToUnit(double celsius) => F ? celsius * 9 / 5 + 32 : celsius;

        public static string TempUnit => F ? "°F" : "°C";

        public static string Temp(float? c, bool unit = true)
        {
            if (!c.HasValue) return "–";
            return ToUnit(c.Value).ToString("0", C) + (unit ? TempUnit : "°");
        }

        public static string TempPrecise(float? c) => c.HasValue ? ToUnit(c.Value).ToString("0.0", C) + TempUnit : "–";

        public static string Percent(float? v) => v.HasValue ? v.Value.ToString("0", C) + "%" : "–";

        public static string Rpm(float? v) => v.HasValue ? v.Value.ToString("#,0", C) + " RPM" : "–";

        public static string Mhz(float? v)
        {
            if (!v.HasValue) return "–";
            return v.Value >= 1000 ? (v.Value / 1000).ToString("0.00", C) + " GHz" : v.Value.ToString("0", C) + " MHz";
        }

        public static string Watts(float? v) => v.HasValue ? v.Value.ToString(v.Value < 10 ? "0.0" : "0", C) + " W" : "–";

        public static string Gb(double? gb)
        {
            if (!gb.HasValue) return "–";
            double v = gb.Value;
            if (v >= 1000) return (v / 1000).ToString(v >= 10000 ? "0" : "0.0", C) + " TB";
            if (v >= 1) return v.ToString(v >= 100 ? "0" : "0.0", C) + " GB";
            return (v * 1000).ToString("0", C) + " MB";
        }

        /// <summary>The hardware monitor reports memory in GiB; show it as people expect ("12.4 GB").</summary>
        public static string GiB(float? v) => v.HasValue ? v.Value.ToString(v.Value >= 100 ? "0" : "0.0", C) + " GB" : "–";

        public static string Rate(float? bytesPerSecond)
        {
            if (!bytesPerSecond.HasValue) return "–";
            double v = bytesPerSecond.Value;
            if (v >= 1e9) return (v / 1e9).ToString("0.0", C) + " GB/s";
            if (v >= 1e6) return (v / 1e6).ToString(v >= 1e8 ? "0" : "0.0", C) + " MB/s";
            if (v >= 1e3) return (v / 1e3).ToString("0", C) + " KB/s";
            return v.ToString("0", C) + " B/s";
        }

        public static string Hours(float? hours)
        {
            if (!hours.HasValue) return "–";
            double h = hours.Value;
            if (h < 48) return h.ToString("0", C) + " hours";
            double days = h / 24;
            if (days < 365) return days.ToString("0", C) + " days";
            return (days / 365.25).ToString("0.0", C) + " years";
        }

        public static string Uptime(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
            if (t.TotalHours >= 1) return $"{t.Hours}h {t.Minutes}m";
            return $"{t.Minutes}m";
        }

        public static string Ago(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "never";
            var t = DateTime.UtcNow - utc;
            if (t.TotalSeconds < 60) return "just now";
            if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} min ago";
            if (t.TotalHours < 24) return $"{(int)t.TotalHours} h ago";
            return utc.ToLocalTime().ToString("d MMM HH:mm", C);
        }

        /// <summary>Any sensor value in its own unit.</summary>
        public static string Sensor(string type, float? v)
        {
            if (!v.HasValue) return "–";
            float x = v.Value;
            return type switch
            {
                "Temperature" => Temp(x),
                "Fan" => Rpm(x),
                "Load" or "Control" or "Level" or "Humidity" => Percent(x),
                "Clock" => Mhz(x),
                "Power" => Watts(x),
                "Voltage" => x.ToString("0.000", C) + " V",
                "Current" => x.ToString("0.00", C) + " A",
                "Frequency" => x.ToString("0.0", C) + " Hz",
                "Flow" => x.ToString("0.0", C) + " L/h",
                "Data" => Gb(x * 1.073741824),
                "SmallData" => x >= 1024 ? (x / 1024).ToString("0.0", C) + " GB" : x.ToString("0", C) + " MB",
                "Throughput" => Rate(x),
                "Factor" => x.ToString(x >= 100 ? "#,0" : "0.##", C),
                "TimeSpan" => Uptime(x),
                "Energy" => x.ToString("#,0", C) + " mWh",
                "Noise" => x.ToString("0", C) + " dBA",
                _ => x.ToString("0.##", C)
            };
        }

        public static string TypeName(string type) => type switch
        {
            "Temperature" => "Temperatures",
            "Fan" => "Fans",
            "Control" => "Fan control",
            "Load" => "Load",
            "Clock" => "Clocks",
            "Power" => "Power",
            "Voltage" => "Voltages",
            "Current" => "Current",
            "Data" or "SmallData" => "Data",
            "Throughput" => "Throughput",
            "Level" => "Levels",
            "Factor" => "Counters",
            _ => type
        };

        public static string HardwareKind(string type) => type switch
        {
            "Cpu" => "Processor",
            "GpuNvidia" or "GpuAmd" or "GpuIntel" => "Graphics",
            "Memory" => "Memory",
            "Motherboard" => "Motherboard",
            "SuperIO" => "Motherboard sensors",
            "Storage" => "Drive",
            "Cooler" => "Cooler",
            "Controller" => "Controller",
            "EmbeddedController" => "Embedded controller",
            "Psu" => "Power supply",
            "PowerMonitor" => "Power monitor",
            "Network" => "Network",
            _ => type
        };

        // ---------- Temperatures and colours ----------

        public static (float warn, float crit) DriveLimits(string machineId, DriveInfo d)
        {
            var a = Settings.Current.Alerts;
            bool solid = d.Media == "SSD" || d.Media == "NVMe";
            float warn = solid ? a.SsdWarn : a.HddWarn;
            float crit = solid ? a.SsdCritical : a.HddCritical;
            if (a.DriveWarn.TryGetValue(Settings.DriveKey(machineId, d), out var custom) && custom > 0)
            {
                crit = custom + (crit - warn);
                warn = custom;
            }
            return (warn, crit);
        }

        public static Brush Level(float? value, float warn, float crit)
        {
            if (!value.HasValue) return Theme.Muted;
            if (value.Value >= crit) return Theme.Red;
            if (value.Value >= warn) return Theme.Amber;
            return Theme.Green;
        }

        /// <summary>Cool to hot without thresholds, for plain sensor lists.</summary>
        public static Brush TempColour(float? c)
        {
            if (!c.HasValue) return Theme.Muted;
            if (c.Value >= 85) return Theme.Red;
            if (c.Value >= 65) return Theme.Amber;
            return Theme.Text;
        }

        public static Brush Health(string health) => health switch
        {
            "Good" => Theme.Green,
            "Caution" => Theme.Amber,
            "Bad" => Theme.Red,
            _ => Theme.Muted
        };

        public static string HealthWords(string health) => health switch
        {
            "Good" => "Good",
            "Caution" => "Keep an eye on it",
            "Bad" => "Back it up now",
            _ => "Unknown"
        };

        /// <summary>A set of distinct line colours that read well on both themes.</summary>
        public static readonly Color[] Palette =
        {
            Color.FromRgb(0xFF, 0xB5, 0x47), Color.FromRgb(0x5E, 0xD3, 0xC3), Color.FromRgb(0x5B, 0x8C, 0xFF), Color.FromRgb(0xF0, 0x6A, 0x9A),
            Color.FromRgb(0x8F, 0xD1, 0x6A), Color.FromRgb(0xA0, 0x7C, 0xF0), Color.FromRgb(0xFF, 0x8A, 0x4C), Color.FromRgb(0x4C, 0xC9, 0xF0),
            Color.FromRgb(0xE2, 0xC2, 0x6A), Color.FromRgb(0xC9, 0x7C, 0xD9), Color.FromRgb(0x6A, 0xD8, 0x9E), Color.FromRgb(0xF2, 0x7B, 0x6B)
        };

        public static Brush PaletteBrush(int i) => Theme.Brush(Palette[((i % Palette.Length) + Palette.Length) % Palette.Length]);
    }
}

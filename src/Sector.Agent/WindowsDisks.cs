using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace Sector.Agent
{
    /// <summary>What Windows itself knows about each physical disk: serial, media type, bus, health and drive letters.</summary>
    public static class WindowsDisks
    {
        public class Disk
        {
            public int Number;
            public string Name = "";
            public string Serial = "";
            public string Firmware = "";
            public string Media = "";
            public string Bus = "";
            public string Health = "";
            public double SizeGB;
            public List<string> Letters = new List<string>();
        }

        private static Dictionary<int, Disk> _cache = new Dictionary<int, Disk>();
        private static DateTime _read = DateTime.MinValue;
        private static readonly object Gate = new object();

        public static Dictionary<int, Disk> Get()
        {
            lock (Gate)
            {
                if (DateTime.UtcNow - _read < TimeSpan.FromSeconds(60)) return _cache;
                _read = DateTime.UtcNow;
                try { _cache = Read(); }
                catch (Exception ex) { Log.Write("Windows disks: " + ex.Message); }
                return _cache;
            }
        }

        private static Dictionary<int, Disk> Read()
        {
            var disks = new Dictionary<int, Disk>();
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();

            using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(
                "SELECT DeviceId, FriendlyName, SerialNumber, FirmwareVersion, MediaType, BusType, HealthStatus, Size FROM MSFT_PhysicalDisk")))
            {
                foreach (ManagementObject o in s.Get())
                {
                    using (o)
                    {
                        if (!int.TryParse(o["DeviceId"]?.ToString(), out int n)) continue;
                        var d = new Disk
                        {
                            Number = n,
                            Name = (o["FriendlyName"]?.ToString() ?? "").Trim(),
                            Serial = (o["SerialNumber"]?.ToString() ?? "").Trim(),
                            Firmware = (o["FirmwareVersion"]?.ToString() ?? "").Trim(),
                            Media = MediaName(ToInt(o["MediaType"])),
                            Bus = BusName(ToInt(o["BusType"])),
                            Health = HealthName(ToInt(o["HealthStatus"])),
                            SizeGB = ToDouble(o["Size"]) / 1e9
                        };
                        if (d.Bus == "NVMe") d.Media = "NVMe";
                        disks[n] = d;
                    }
                }
            }

            try
            {
                using var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DiskNumber, DriveLetter FROM MSFT_Partition"));
                foreach (ManagementObject o in s.Get())
                {
                    using (o)
                    {
                        int n = ToInt(o["DiskNumber"]);
                        int letter = ToInt(o["DriveLetter"]);
                        if (letter >= 'A' && letter <= 'Z' && disks.TryGetValue(n, out var d))
                            d.Letters.Add(((char)letter) + ":");
                    }
                }
            }
            catch (Exception ex) { Log.Write("Partitions: " + ex.Message); }

            foreach (var d in disks.Values) d.Letters = d.Letters.Distinct().OrderBy(x => x).ToList();
            return disks;
        }

        private static int ToInt(object o)
        {
            if (o == null) return -1;
            try { return o is char c ? c : Convert.ToInt32(o); } catch { return -1; }
        }

        private static double ToDouble(object o)
        {
            if (o == null) return 0;
            try { return Convert.ToDouble(o); } catch { return 0; }
        }

        private static string MediaName(int v) => v switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "" };

        private static string HealthName(int v) => v switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" };

        private static string BusName(int v) => v switch
        {
            1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "FireWire", 5 => "SSA", 6 => "Fibre Channel", 7 => "USB",
            8 => "RAID", 9 => "iSCSI", 10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC", 15 => "Virtual",
            16 => "Storage Spaces", 17 => "NVMe", _ => ""
        };
    }
}

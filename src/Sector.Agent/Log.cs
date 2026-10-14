using System;
using System.IO;
using Sector.Shared;

namespace Sector.Agent
{
    /// <summary>A small log in C:\ProgramData\Sector\agent.log that never grows past 1 MB.</summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string PathName => Path.Combine(Constants.DataFolder, "agent.log");

        public static bool ToConsole { get; set; }

        public static void Write(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message;
            if (ToConsole) Console.WriteLine(line);
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Constants.DataFolder);
                    var fi = new FileInfo(PathName);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        string old = PathName + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(PathName, old);
                    }
                    File.AppendAllText(PathName, line + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace CSGO_Font_Manager
{
    internal static class AppLog
    {
        private static readonly object Sync = new object();
        private const long MaxLogBytes = 2 * 1024 * 1024;

        public static string LogPath
        {
            get
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                string directory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(directory, "font-manager.log");
            }
        }

        public static void StartSession()
        {
            Write("INFO", "============================================================");
            Write("INFO", "Font Manager started. Version " + Form1.VersionNumber);
            Write("INFO", "Executable: " + Assembly.GetExecutingAssembly().Location);
            Write("INFO", "OS: " + Environment.OSVersion);
            Write("INFO", ".NET: " + Environment.Version);
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message, Exception exception = null)
        {
            if (exception == null)
            {
                Write("ERROR", message);
                return;
            }

            Write("ERROR", message + Environment.NewLine + exception);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    RotateIfNeeded();
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                                  " [" + level + "] " + message + Environment.NewLine;
                    File.AppendAllText(LogPath, line, new UTF8Encoding(false));
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Font Manager logging failed: " + exception.Message);
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(LogPath)) return;
                FileInfo info = new FileInfo(LogPath);
                if (info.Length < MaxLogBytes) return;

                string oldPath = LogPath + ".old";
                if (File.Exists(oldPath)) File.Delete(oldPath);
                File.Move(LogPath, oldPath);
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Font Manager log rotation failed: " + exception.Message);
            }
        }
    }
}

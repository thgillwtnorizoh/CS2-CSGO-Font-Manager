using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    /// <summary>
    /// Owns process shutdown and Steam relaunch behavior for CS2 and legacy CS:GO.
    /// </summary>
    public partial class Form1
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_CLOSE = 0x0010;

        private static bool IsCsgoRunning()
        {
            try { return Process.GetProcessesByName("csgo").Any(process => !process.HasExited); }
            catch { return false; }
        }

        private async void DualRestart(object sender, EventArgs e)
        {
            if (gameTarget == GameTarget.CS2)
                await RestartCs2Async();
            else
                await RestartCsgoAsync();
        }

        private async Task RestartCs2Async()
        {
            Process[] processes = Process.GetProcessesByName("cs2");
            if (processes.Length == 0)
            {
                SyncGameUi();
                return;
            }

            if (MessageBox.Show("Close CS2 and relaunch it through Steam now?", "Restart CS2",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            restartCs2Button.Enabled = false;
            restartCs2Button.Text = "Closing CS2...";

            try
            {
                foreach (Process process in processes)
                {
                    try { if (!process.HasExited) process.CloseMainWindow(); }
                    catch { }
                }

                bool exited = await WaitForProcessesToExit(processes, 15000);
                if (!exited)
                {
                    MessageBox.Show("CS2 did not close within 15 seconds.");
                    return;
                }

                Process.Start(new ProcessStartInfo { FileName = "steam://rungameid/730", UseShellExecute = true });
                AppLog.Info("Restarted CS2 through Steam app 730.");
            }
            catch (Exception exception)
            {
                AppLog.Error("Restart CS2 failed.", exception);
                MessageBox.Show("Restart failed.\n\n" + exception.Message);
            }
            finally
            {
                SyncGameUi();
            }
        }

        private async Task RestartCsgoAsync()
        {
            Process[] processes = Process.GetProcessesByName("csgo");
            if (processes.Length == 0)
            {
                SyncGameUi();
                return;
            }

            if (MessageBox.Show("Close CS:GO and relaunch it through Steam now?", "Restart CS:GO",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            restartCs2Button.Enabled = false;
            restartCs2Button.Text = "Closing CS:GO...";
            AppLog.Info("Restart CS:GO requested. Running process count: " + processes.Length + ".");

            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        if (process.HasExited) continue;
                        bool closeRequested = process.CloseMainWindow();
                        AppLog.Info("CS:GO CloseMainWindow PID " + process.Id + " returned " + closeRequested + ".");
                        SendCloseToProcessWindows(process.Id);
                    }
                    catch (Exception exception)
                    {
                        AppLog.Warn("Could not request a normal CS:GO close: " + exception.Message);
                    }
                }

                bool exited = await WaitForProcessesToExit(processes, 10000);
                if (!exited)
                {
                    DialogResult force = MessageBox.Show(
                        "CS:GO ignored the normal window-close request.\n\nForce close CS:GO and continue the restart?",
                        "CS:GO Still Running", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (force != DialogResult.Yes)
                    {
                        AppLog.Warn("CS:GO restart cancelled after graceful close timed out.");
                        return;
                    }

                    restartCs2Button.Text = "Force closing CS:GO...";
                    foreach (Process process in processes)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                AppLog.Warn("Force closing CS:GO PID " + process.Id + " after WM_CLOSE timeout.");
                                process.Kill();
                            }
                        }
                        catch (Exception exception)
                        {
                            AppLog.Warn("Could not force close CS:GO: " + exception.Message);
                        }
                    }

                    exited = await WaitForProcessesToExit(processes, 5000);
                    if (!exited)
                    {
                        MessageBox.Show("CS:GO is still running after the force-close attempt. The relaunch was cancelled.",
                            "Restart CS:GO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        AppLog.Warn("CS:GO remained running after force-close attempt; relaunch cancelled.");
                        return;
                    }
                }

                int appId = Settings != null && !string.IsNullOrWhiteSpace(Settings.LegacyCsgoPath) &&
                            new DirectoryInfo(Settings.LegacyCsgoPath).Name.Equals("csgo legacy", StringComparison.OrdinalIgnoreCase)
                    ? 4465480
                    : 730;

                restartCs2Button.Text = "Launching CS:GO...";
                Process.Start(new ProcessStartInfo
                {
                    FileName = "steam://rungameid/" + appId,
                    UseShellExecute = true
                });
                AppLog.Info("CS:GO relaunch requested through Steam app " + appId + ".");
            }
            catch (Exception exception)
            {
                AppLog.Error("Restart CS:GO failed.", exception);
                MessageBox.Show("Restart CS:GO failed.\n\n" + exception.Message,
                    "Restart Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SyncGameUi();
            }
        }

        private static void SendCloseToProcessWindows(int processId)
        {
            int windowCount = 0;
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                uint ownerPid;
                GetWindowThreadProcessId(hWnd, out ownerPid);
                if (ownerPid != (uint)processId)
                    return true;

                windowCount++;
                PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);

            AppLog.Info("Sent WM_CLOSE to " + windowCount + " top-level window(s) for CS:GO PID " + processId + ".");
        }

        private static Task<bool> WaitForProcessesToExit(Process[] processes, int timeoutMilliseconds)
        {
            return Task.Run(delegate
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
                {
                    bool anyRunning = false;
                    foreach (Process process in processes)
                    {
                        try
                        {
                            if (!process.HasExited)
                                anyRunning = true;
                        }
                        catch { }
                    }

                    if (!anyRunning)
                        return true;

                    System.Threading.Thread.Sleep(200);
                }
                return false;
            });
        }
    }
}

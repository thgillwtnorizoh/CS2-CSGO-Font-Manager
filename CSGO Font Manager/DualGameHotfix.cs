using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private static readonly bool DualGameHotfixBootstrapRegistered = RegisterDualGameHotfixBootstrap();
        private bool dualGameHotfixInstalled;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_CLOSE = 0x0010;

        private static bool RegisterDualGameHotfixBootstrap()
        {
            Application.Idle += DualGameHotfixOnIdle;
            return true;
        }

        private static void DualGameHotfixOnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.IsDisposed || form.dualGameHotfixInstalled)
                    continue;
                if (!form.dualReady || form.restartCs2Button == null || form.settingLink == null)
                    continue;

                form.InstallDualGameHotfix();
            }
        }

        private void InstallDualGameHotfix()
        {
            if (dualGameHotfixInstalled) return;
            dualGameHotfixInstalled = true;

            restartCs2Button.Click -= DualRestart;
            restartCs2Button.Click += DualRestartHotfix;

            LinkLabel oldSetting = settingLink;
            LinkLabel replacement = new LinkLabel
            {
                AutoSize = true,
                Text = "Setting",
                LinkColor = oldSetting.LinkColor,
                ActiveLinkColor = oldSetting.ActiveLinkColor,
                VisitedLinkColor = oldSetting.VisitedLinkColor,
                BackColor = oldSetting.BackColor,
                Font = oldSetting.Font,
                TabStop = true,
                Visible = oldSetting.Visible
            };
            replacement.LinkClicked += delegate { ShowPathSettingsHotfix(); };

            Controls.Remove(oldSetting);
            oldSetting.Dispose();
            settingLink = replacement;
            Controls.Add(settingLink);
            settingLink.BringToFront();
            LayoutDualBits();

            AppLog.Info("Dual-game runtime hotfix installed: CS:GO window-close fallback and readable path-setting buttons.");
        }

        private async void DualRestartHotfix(object sender, EventArgs e)
        {
            if (gameTarget == GameTarget.CS2)
            {
                DualRestart(sender, e);
                return;
            }

            Process[] processes = Process.GetProcessesByName("csgo");
            if (processes.Length == 0)
            {
                SyncGameUi();
                return;
            }

            if (MessageBox.Show(
                    "Close CS:GO and relaunch it through Steam now?",
                    "Restart CS:GO", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
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

                        // Source 1/fullscreen CS:GO can expose no useful Process.MainWindowHandle.
                        // Enumerate every top-level window owned by the process and send WM_CLOSE directly.
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
                        MessageBox.Show(
                            "CS:GO is still running after the force-close attempt. The relaunch was cancelled.",
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

        private void ShowPathSettingsHotfix()
        {
            using (Form dialog = new Form())
            {
                dialog.Text = "Setting - Game Paths";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ClientSize = new Size(760, 255);
                dialog.BackColor = BackColor;
                dialog.ForeColor = Color.White;

                TextBox cs2Path = PathBox(Settings == null ? null : Settings.Cs2Path, 15, 55);
                TextBox csgoPath = PathBox(Settings == null ? null : Settings.LegacyCsgoPath, 15, 135);
                dialog.Controls.Add(new Label
                {
                    Text = "CS2 (Steam app 730)",
                    AutoSize = true,
                    Location = new Point(15, 35),
                    ForeColor = Color.White
                });
                dialog.Controls.Add(new Label
                {
                    Text = "CS:GO (app 4465480; app 730 legacy-beta fallback)",
                    AutoSize = true,
                    Location = new Point(15, 115),
                    ForeColor = Color.White
                });
                dialog.Controls.Add(cs2Path);
                dialog.Controls.Add(csgoPath);

                Button detectCs2 = PButton("Detect", 585, 53);
                Button browseCs2 = PButton("Browse", 665, 53);
                Button detectCsgo = PButton("Detect", 585, 133);
                Button browseCsgo = PButton("Browse", 665, 133);
                detectCs2.Click += delegate { string path = FindCs2(); if (path != null) cs2Path.Text = path; };
                detectCsgo.Click += delegate { string path = FindCsgo(); if (path != null) csgoPath.Text = path; };
                browseCs2.Click += delegate { string path = BrowsePath(cs2Path.Text); if (path != null) cs2Path.Text = path; };
                browseCsgo.Click += delegate { string path = BrowsePath(csgoPath.Text); if (path != null) csgoPath.Text = path; };
                dialog.Controls.Add(detectCs2);
                dialog.Controls.Add(browseCs2);
                dialog.Controls.Add(detectCsgo);
                dialog.Controls.Add(browseCsgo);

                Button save = PButton("Save", 585, 205);
                Button cancel = PButton("Cancel", 665, 205);
                save.BackColor = Color.MediumSpringGreen;
                cancel.BackColor = Color.FromArgb(120, 190, 255);
                save.ForeColor = Color.Black;
                cancel.ForeColor = Color.Black;
                cancel.DialogResult = DialogResult.Cancel;

                save.Click += delegate
                {
                    string candidateCs2 = cs2Path.Text.Trim();
                    string candidateCsgo = csgoPath.Text.Trim();
                    if (candidateCs2.Length > 0 && !ValidCs2(candidateCs2))
                    {
                        MessageBox.Show("Invalid CS2 path. Expected game\\bin\\win64\\cs2.exe and game\\csgo\\panorama\\fonts\\fonts.conf.");
                        return;
                    }
                    if (candidateCsgo.Length > 0 && !ValidCsgo(candidateCsgo))
                    {
                        MessageBox.Show("Invalid CS:GO path. Expected csgo.exe and csgo\\panorama\\fonts\\fonts.conf.");
                        return;
                    }

                    Settings.Cs2Path = candidateCs2.Length == 0 ? null : candidateCs2;
                    Settings.CsgoPath = Settings.Cs2Path;
                    Settings.LegacyCsgoPath = candidateCsgo.Length == 0 ? null : candidateCsgo;
                    SaveNow();
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                };

                dialog.Controls.Add(save);
                dialog.Controls.Add(cancel);
                dialog.CancelButton = cancel;
                dialog.ShowDialog(this);
            }

            SyncGameUi();
        }
    }
}

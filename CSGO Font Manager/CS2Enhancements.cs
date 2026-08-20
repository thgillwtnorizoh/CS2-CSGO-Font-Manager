using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private Button restartCs2Button;
        private Timer cs2ProcessTimer;
        private bool cs2EnhancementsInitialized;

        private void InitializeCs2Enhancements()
        {
            if (cs2EnhancementsInitialized) return;
            cs2EnhancementsInitialized = true;

            AppLog.StartSession();
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs args)
            {
                AppLog.Error("Unhandled UI exception.", args.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs args)
            {
                Exception exception = args.ExceptionObject as Exception;
                AppLog.Error("Unhandled application exception.", exception);
            };

            listBox1.DragDrop -= fontLibrary_DragDrop;
            listBox1.DragDrop += fontLibrary_DragDrop_cs2Guard;

            listBox1.SelectedIndexChanged += listBox1_SelectedIndexChanged_cs2Enhancements;
            addFont_button.Click += viewChanged_cs2Enhancements;
            donate_button.Click += viewChanged_cs2Enhancements;

            InsertRestartButtonBelowApply();
            AppLog.Info("CS2 enhancements initialized. Log file: " + AppLog.LogPath);
        }

        private void InsertRestartButtonBelowApply()
        {
            if (restartCs2Button != null) return;

            const int restartHeight = 29;
            const int gap = 6;
            int delta = restartHeight + gap;

            Size originalListSize = listBox1.Size;
            Point originalPreviewLocation = fontPreview_richTextBox.Location;
            Point originalApplyLocation = apply_button.Location;

            SuspendLayout();
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + delta);

            // Only the controls below Apply should move down. Restore the existing main layout.
            listBox1.Size = originalListSize;
            fontPreview_richTextBox.Location = originalPreviewLocation;
            apply_button.Location = originalApplyLocation;

            restartCs2Button = new Button
            {
                Name = "restartCs2Button",
                Text = "Restart CS2",
                FlatStyle = FlatStyle.Popup,
                Font = donate_button.Font,
                BackColor = Color.FromArgb(120, 190, 255),
                ForeColor = SystemColors.ControlText,
                Location = new Point(apply_button.Left, apply_button.Bottom + gap),
                Size = new Size(apply_button.Width, restartHeight),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                UseVisualStyleBackColor = false
            };
            restartCs2Button.Click += restartCs2Button_Click;
            Controls.Add(restartCs2Button);
            restartCs2Button.BringToFront();

            cs2ProcessTimer = new Timer { Interval = 1000 };
            cs2ProcessTimer.Tick += delegate { UpdateRestartCs2ButtonState(); };
            cs2ProcessTimer.Start();

            UpdateRestartCs2ButtonState();
            ResumeLayout(false);
        }

        private void listBox1_SelectedIndexChanged_cs2Enhancements(object sender, EventArgs e)
        {
            if (CurrentFormView == FormViews.Main && listBox1.SelectedItem != null &&
                listBox1.SelectedItem.ToString() == DefaultFontName)
            {
                // The stock font can also use the size multiplier.
                trackBar1.Visible = true;
                fontPreview_richTextBox.Visible = false;
            }
            UpdateRestartCs2ButtonState();
        }

        private void viewChanged_cs2Enhancements(object sender, EventArgs e)
        {
            BeginInvoke((MethodInvoker)UpdateRestartCs2ButtonState);
        }

        private void UpdateRestartCs2ButtonState()
        {
            if (restartCs2Button == null) return;

            bool mainView = CurrentFormView == FormViews.Main;
            bool running = IsCs2Running();
            restartCs2Button.Visible = mainView;
            restartCs2Button.Enabled = mainView && running;
            restartCs2Button.Text = running ? "Restart CS2" : "Restart CS2 (not running)";
        }

        private static bool IsCs2Running()
        {
            try
            {
                return Process.GetProcessesByName("cs2").Any(p => !p.HasExited);
            }
            catch
            {
                return false;
            }
        }

        private async void restartCs2Button_Click(object sender, EventArgs e)
        {
            Process[] processes = Process.GetProcessesByName("cs2");
            if (processes.Length == 0)
            {
                UpdateRestartCs2ButtonState();
                return;
            }

            if (MessageBox.Show(
                    "Close CS2 and relaunch it through Steam now?\n\nThis lets CS2 load the newly applied font configuration.",
                    "Restart CS2", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            restartCs2Button.Enabled = false;
            restartCs2Button.Text = "Closing CS2...";
            AppLog.Info("Restart CS2 requested. Running process count: " + processes.Length);

            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        if (!process.HasExited) process.CloseMainWindow();
                    }
                    catch (Exception exception)
                    {
                        AppLog.Warn("Could not request a clean CS2 window close: " + exception.Message);
                    }
                }

                bool exited = await Task.Run(delegate
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    while (stopwatch.ElapsedMilliseconds < 15000)
                    {
                        bool anyRunning = false;
                        foreach (Process process in processes)
                        {
                            try
                            {
                                if (!process.HasExited) anyRunning = true;
                            }
                            catch { }
                        }
                        if (!anyRunning) return true;
                        System.Threading.Thread.Sleep(200);
                    }
                    return false;
                });

                if (!exited)
                {
                    AppLog.Warn("CS2 did not exit within 15 seconds; relaunch was cancelled.");
                    MessageBox.Show(
                        "CS2 did not close within 15 seconds. Close it normally, then use Restart CS2 again.",
                        "CS2 Still Running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                restartCs2Button.Text = "Launching CS2...";
                Process.Start(new ProcessStartInfo
                {
                    FileName = "steam://rungameid/730",
                    UseShellExecute = true
                });
                AppLog.Info("CS2 relaunch requested through Steam app ID 730.");
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to restart CS2.", exception);
                MessageBox.Show("Failed to restart CS2.\n\n" + exception.Message,
                    "Restart Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UpdateRestartCs2ButtonState();
            }
        }

        private void fontLibrary_DragDrop_cs2Guard(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                fontLibrary_DragDrop(sender, e);
                return;
            }

            string[] incoming = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (incoming == null || incoming.Length == 0) return;

            List<string> accepted = new List<string>();
            List<string> temporaryFiles = new List<string>();

            try
            {
                foreach (string file in incoming)
                {
                    string extension = Path.GetExtension(file).ToLowerInvariant();
                    if (!IsFontExtension(extension))
                    {
                        // Let the original handler deal with ZIPs and unsupported file types.
                        accepted.Add(file);
                        continue;
                    }

                    FontEncodingInfo encoding = FontEncodingInspector.Inspect(file);
                    AppLog.Info("Import encoding check: " + Path.GetFileName(file) + " => " +
                                encoding.EncodingDescription + "; " + encoding.Detail);

                    if (!encoding.IsSupportedContainer || encoding.IsUnicode)
                    {
                        accepted.Add(file);
                        continue;
                    }

                    if (encoding.CanAutoConvertSymbolToUnicode)
                    {
                        DialogResult choice = MessageBox.Show(
                            "Font: " + Path.GetFileName(file) + "\n" +
                            "Encoding: " + encoding.EncodingDescription + "\n\n" +
                            "Counter-Strike 2 needs a Unicode-encoded font and may revert a non-Unicode font.\n\n" +
                            "Do you want Font Manager to create a CS2-compatible Unicode BMP copy?\n\n" +
                            "Yes = Re-encode copy\nNo = Import anyway\nCancel = Skip this font",
                            "Non-Unicode Font Detected", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

                        if (choice == DialogResult.Cancel)
                        {
                            AppLog.Info("Import skipped by user after non-Unicode warning: " + file);
                            continue;
                        }

                        if (choice == DialogResult.No)
                        {
                            AppLog.Warn("Non-Unicode font imported without conversion by user choice: " + file);
                            accepted.Add(file);
                            continue;
                        }

                        string tempDirectory = Path.Combine(Path.GetTempPath(), "FontManagerUnicode");
                        Directory.CreateDirectory(tempDirectory);
                        string converted = Path.Combine(tempDirectory,
                            Path.GetFileNameWithoutExtension(file) + "_Unicode_" + Guid.NewGuid().ToString("N") + extension);
                        string error;
                        if (!FontEncodingInspector.TryCreateUnicodeBmpCopy(file, converted, out error))
                        {
                            AppLog.Error("Unicode conversion failed for " + file + ": " + error);
                            MessageBox.Show(
                                "Font Manager could not create a Unicode copy.\n\n" + error,
                                "Re-encode Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            continue;
                        }

                        FontEncodingInfo convertedInfo = FontEncodingInspector.Inspect(converted);
                        AppLog.Info("Created Unicode copy: " + converted + " => " +
                                    convertedInfo.EncodingDescription + "; " + convertedInfo.Detail);
                        temporaryFiles.Add(converted);
                        accepted.Add(converted);
                    }
                    else
                    {
                        DialogResult importAnyway = MessageBox.Show(
                            "Font: " + Path.GetFileName(file) + "\n" +
                            "Encoding: " + encoding.EncodingDescription + "\n\n" +
                            "Counter-Strike 2 needs a Unicode-encoded font and may revert this font.\n" +
                            "Automatic conversion is not available for this encoding yet.\n\n" +
                            "Import it anyway?",
                            "Non-Unicode Font Detected", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                        if (importAnyway == DialogResult.Yes)
                        {
                            AppLog.Warn("Unsupported non-Unicode encoding imported by user choice: " + file);
                            accepted.Add(file);
                        }
                        else
                        {
                            AppLog.Info("Import skipped after unsupported encoding warning: " + file);
                        }
                    }
                }

                if (accepted.Count == 0) return;

                DataObject forwardedData = new DataObject();
                forwardedData.SetData(DataFormats.FileDrop, accepted.ToArray());
                DragEventArgs forwarded = new DragEventArgs(
                    forwardedData, e.KeyState, e.X, e.Y, e.AllowedEffect, e.Effect);
                fontLibrary_DragDrop(sender, forwarded);
                AppLog.Info("Import handler completed for " + accepted.Count + " file(s).");
            }
            catch (Exception exception)
            {
                AppLog.Error("Font import guard failed.", exception);
                MessageBox.Show("Font import failed.\n\n" + exception.Message,
                    "Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                foreach (string temporaryFile in temporaryFiles)
                {
                    try { if (File.Exists(temporaryFile)) File.Delete(temporaryFile); } catch { }
                }
            }
        }
    }
}

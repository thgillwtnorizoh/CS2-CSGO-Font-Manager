using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private void InitializeGamePaths()
        {
            if (Settings == null) return;

            if (string.IsNullOrWhiteSpace(Settings.Cs2Path) || !ValidCs2(Settings.Cs2Path))
                Settings.Cs2Path = ValidCs2(Settings.CsgoPath) ? Settings.CsgoPath : FindCs2();

            if (!string.IsNullOrWhiteSpace(Settings.Cs2Path))
                Settings.CsgoPath = Settings.Cs2Path;

            if (string.IsNullOrWhiteSpace(Settings.LegacyCsgoPath) || !ValidCsgo(Settings.LegacyCsgoPath))
                Settings.LegacyCsgoPath = FindCsgo();

            if (string.IsNullOrWhiteSpace(Settings.ActiveGame))
                Settings.ActiveGame = "CS2";

            if (Settings.SpecificFontAssignments == null)
                Settings.SpecificFontAssignments = new Dictionary<string, string>();
            if (Settings.CsgoSpecificFontAssignments == null)
                Settings.CsgoSpecificFontAssignments = new Dictionary<string, string>();

            SaveNow();
        }

        private void ShowPathSettings()
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

                dialog.Controls.Add(new Label { Text = "CS2 (Steam app 730)", AutoSize = true, Location = new Point(15, 35), ForeColor = Color.White });
                dialog.Controls.Add(new Label { Text = "CS:GO (app 4465480; app 730 legacy-beta fallback)", AutoSize = true, Location = new Point(15, 115), ForeColor = Color.White });
                dialog.Controls.Add(cs2Path);
                dialog.Controls.Add(csgoPath);

                Button detectCs2 = PathDialogButton("Detect", 585, 53);
                Button browseCs2 = PathDialogButton("Browse", 665, 53);
                Button detectCsgo = PathDialogButton("Detect", 585, 133);
                Button browseCsgo = PathDialogButton("Browse", 665, 133);

                detectCs2.Click += delegate { string path = FindCs2(); if (path != null) cs2Path.Text = path; };
                detectCsgo.Click += delegate { string path = FindCsgo(); if (path != null) csgoPath.Text = path; };
                browseCs2.Click += delegate { string path = BrowsePath(cs2Path.Text); if (path != null) cs2Path.Text = path; };
                browseCsgo.Click += delegate { string path = BrowsePath(csgoPath.Text); if (path != null) csgoPath.Text = path; };

                dialog.Controls.Add(detectCs2);
                dialog.Controls.Add(browseCs2);
                dialog.Controls.Add(detectCsgo);
                dialog.Controls.Add(browseCsgo);

                Button save = PathDialogButton("Save", 585, 205);
                Button cancel = PathDialogButton("Cancel", 665, 205);
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

        private TextBox PathBox(string text, int x, int y)
        {
            return new TextBox
            {
                Text = text ?? string.Empty,
                Location = new Point(x, y),
                Size = new Size(555, 23),
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private Button PathDialogButton(string text, int x, int y)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(72, 27),
                FlatStyle = FlatStyle.Popup,
                BackColor = Color.FromArgb(70, 75, 80),
                ForeColor = Color.White
            };
        }

        private string BrowsePath(string current)
        {
            using (FolderBrowserDialog browser = new FolderBrowserDialog())
            {
                if (Directory.Exists(current))
                    browser.SelectedPath = current;
                return browser.ShowDialog(this) == DialogResult.OK ? browser.SelectedPath : null;
            }
        }

        private static IEnumerable<string> SteamRoots()
        {
            HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string;
            if (!string.IsNullOrWhiteSpace(steamPath))
                roots.Add(steamPath.Replace('/', '\\'));

            string libraryFile = string.IsNullOrWhiteSpace(steamPath)
                ? null
                : Path.Combine(steamPath.Replace('/', '\\'), "steamapps", "libraryfolders.vdf");

            if (libraryFile != null && File.Exists(libraryFile))
            {
                string text = File.ReadAllText(libraryFile);
                foreach (Match match in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
                    roots.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
                foreach (Match match in Regex.Matches(text, "^\\s*\"\\d+\"\\s+\"([^\"]+)\"", RegexOptions.Multiline))
                    roots.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
            }

            return roots.Where(Directory.Exists);
        }

        private static string ManifestDir(string path)
        {
            if (!File.Exists(path)) return null;
            Match match = Regex.Match(File.ReadAllText(path), "\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string FindCs2()
        {
            foreach (string root in SteamRoots())
            {
                string steamApps = Path.Combine(root, "steamapps");
                string installDir = ManifestDir(Path.Combine(steamApps, "appmanifest_730.acf"));
                if (installDir != null)
                {
                    string path = Path.Combine(steamApps, "common", installDir);
                    if (ValidCs2(path)) return path;
                }

                string fallback = Path.Combine(steamApps, "common", "Counter-Strike Global Offensive");
                if (ValidCs2(fallback)) return fallback;
            }
            return null;
        }

        private static string FindCsgo()
        {
            foreach (string root in SteamRoots())
            {
                string steamApps = Path.Combine(root, "steamapps");
                string installDir = ManifestDir(Path.Combine(steamApps, "appmanifest_4465480.acf"));
                if (installDir != null)
                {
                    string path = Path.Combine(steamApps, "common", installDir);
                    if (ValidCsgo(path)) return path;
                }

                string standaloneFallback = Path.Combine(steamApps, "common", "csgo legacy");
                if (ValidCsgo(standaloneFallback)) return standaloneFallback;
            }

            foreach (string root in SteamRoots())
            {
                string legacyBeta = Path.Combine(root, "steamapps", "common", "Counter-Strike Global Offensive");
                if (ValidCsgo(legacyBeta)) return legacyBeta;
            }
            return null;
        }

        private static bool ValidCs2(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) &&
                   File.Exists(Path.Combine(path, "game", "bin", "win64", "cs2.exe")) &&
                   File.Exists(Path.Combine(path, "game", "csgo", "panorama", "fonts", "fonts.conf"));
        }

        private static bool ValidCsgo(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) &&
                   File.Exists(Path.Combine(path, "csgo.exe")) &&
                   File.Exists(Path.Combine(path, "csgo", "panorama", "fonts", "fonts.conf"));
        }
    }
}

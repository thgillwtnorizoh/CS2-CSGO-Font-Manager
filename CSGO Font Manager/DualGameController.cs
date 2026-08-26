using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private enum GameTarget { CS2, CSGO }

        private GameTarget gameTarget;
        private bool dualReady;
        private Label gameHint;
        private LinkLabel settingLink;
        private FlowLayoutPanel cs2SpecificFlow;
        private FlowLayoutPanel csgoSpecificFlow;

        // These are persistent ownership markers, not release-version strings.
        // Keeping 4.0 lets 4.0.x releases recognize and clean blocks written by earlier 4.x builds.
        private const string CSGO_BEGIN = "<!-- Font Manager 4.0 CS:GO begin -->";
        private const string CSGO_END = "<!-- Font Manager 4.0 CS:GO end -->";
        private const string CSGO_PATTERN_BEGIN = "<!-- Font Manager 4.0 CS:GO patterns begin -->";
        private const string CSGO_PATTERN_END = "<!-- Font Manager 4.0 CS:GO patterns end -->";

        private static string CsgoBackup => DataPath + "fonts.conf.csgo.original";
        private static string CsgoGenerated => DataPath + "fonts.conf.csgo.generated";

        private static readonly bool DualGameBootstrapRegistered = RegisterDualGameBootstrap();

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_CLOSE = 0x0010;

        private static readonly FamilySpec[] CsgoFamilies =
        {
            new FamilySpec("notosans", "General UI", "Default CS:GO Panorama labels, messages and text-entry controls."),
            new FamilySpec("Stratum2", "General UI", "Primary CS:GO display type: menu headings, category labels, HUD presentation and branded UI."),
            new FamilySpec("Stratum2 Bold", "General UI", "Emphasized display text such as winner/MVP plaques and demolition progress titles."),
            new FamilySpec("ForceStratum2", "HUD / Compatibility", "Forced combat HUD Stratum: health, armor, money, weapons and killfeed-related surfaces in fallback languages."),
            new FamilySpec("Stratum2 Regular Monodigit", "Numbers / Technical", "Ordinary fixed-width numbers: scoreboard cells, quantities and regular numeric values."),
            new FamilySpec("Stratum2 Bold Monodigit", "Numbers / Technical", "Prominent fixed-width numbers: timers, money, scores, round counters and important statistics."),
            new FamilySpec("Stratum2 Monodigit", "Numbers / Technical", "Compass headings, respawn countdowns, radial menus, demos and stat values."),
            new FamilySpec("notomono-regular", "Numbers / Technical", "Generic monospace/technical timing and data-oriented Panorama text."),
            new FamilySpec("Noto Sans ExtCond", "Fallback / Special", "Rare extended-condensed text used by end-of-match accolades and Retake bombsite presentation."),
            new FamilySpec("Arial Unicode MS", "Fallback / Special", "Multilingual fallback after Valve families. Replacing it can reduce glyph coverage."),
            new FamilySpec("Arial", "Fallback / Special", "Generic compatibility and language fallback used by legacy CS:GO Fontconfig."),
            new FamilySpec("notoserif", "Fallback / Special", "Generic serif family exposed by legacy Fontconfig; uncommon in normal CS:GO UI.")
        };

        static Form1()
        {
            VersionNumber = ReleaseInfo.Version;
        }

        private static bool RegisterDualGameBootstrap()
        {
            Application.Idle += DualGameBootstrapOnIdle;
            return true;
        }

        private static void DualGameBootstrapOnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.IsDisposed || form.dualReady)
                    continue;
                if (!form.pigUiV2Initialized || form.restartCs2Button == null || form.specificSearchTextBox == null)
                    continue;

                form.InitializeDualGameController();

                // Font Manager is single-instance with one main Form1. Once the controller is wired,
                // the bootstrap has no reason to run on every future idle cycle.
                Application.Idle -= DualGameBootstrapOnIdle;
                break;
            }
        }

        private void InitializeDualGameController()
        {
            if (dualReady) return;
            dualReady = true;

            InitializeGamePaths();
            gameTarget = Settings != null && string.Equals(Settings.ActiveGame, "CSGO", StringComparison.OrdinalIgnoreCase)
                ? GameTarget.CSGO
                : GameTarget.CS2;
            cs2SpecificFlow = specificFamilyFlow;

            version_label.Text = "Version " + VersionNumber;
            title_label.Cursor = Cursors.Hand;
            title_label.Click += GameTitle_Click;

            gameHint = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe Script", 9f),
                ForeColor = Color.FromArgb(130, 145, 150),
                BackColor = Color.Transparent
            };
            Controls.Add(gameHint);
            InitializeGameHintFont();

            settingLink = new LinkLabel
            {
                AutoSize = true,
                Text = "Setting",
                LinkColor = linkLabel3.LinkColor,
                ActiveLinkColor = linkLabel3.ActiveLinkColor,
                VisitedLinkColor = linkLabel3.VisitedLinkColor,
                BackColor = Color.Transparent
            };
            settingLink.LinkClicked += SettingLink_LinkClicked;
            Controls.Add(settingLink);

            apply_button.Click -= pigUiV2_ApplyButtonClick;
            apply_button.Click += DualApply;

            specificApplyButton.Click -= specificApplyButton_Click;
            specificApplyButton.Click += SpecificApplyButton_Click;

            restartCs2Button.Click -= restartCs2Button_Click;
            restartCs2Button.Click += DualRestart;

            specificSettingTabButton.Click -= pigSpecificSettingTabButton_Click;
            specificSettingTabButton.Click += DualSpecificTab;

            specificViewCombo.SelectedIndexChanged -= pigUiV2_ViewChanged;
            specificViewCombo.SelectedIndexChanged += DualSpecificView;

            SizeChanged += DualGame_SizeChanged;
            if (cs2ProcessTimer != null)
                cs2ProcessTimer.Tick += DualGameProcessTimer_Tick;

            InitializeSpecificSettingsState();
            ApplyGameMode(false);
            QueueStartupLayoutFinalization();

            AppLog.Info("Font Manager " + VersionNumber + " dual-game controller initialized; active=" + GameName() + ".");
        }

        private void GameTitle_Click(object sender, EventArgs e)
        {
            gameTarget = gameTarget == GameTarget.CS2 ? GameTarget.CSGO : GameTarget.CS2;
            ApplyGameMode(true);
        }

        private void SettingLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ShowPathSettings();
        }

        private void DualGame_SizeChanged(object sender, EventArgs e)
        {
            LayoutDualBits();
        }

        private void DualGameProcessTimer_Tick(object sender, EventArgs e)
        {
            SyncGameUi();
        }

        private string GameName()
        {
            return gameTarget == GameTarget.CS2 ? "CS2" : "CS:GO";
        }

        private void SaveNow()
        {
            try
            {
                if (SettingsManager != null && Settings != null)
                    SettingsManager.Save(Settings);
            }
            catch (Exception exception)
            {
                AppLog.Warn("Could not save settings: " + exception.Message);
            }
        }

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

        private void ApplyGameMode(bool clicked)
        {
            if (Settings != null)
            {
                Settings.ActiveGame = gameTarget == GameTarget.CS2 ? "CS2" : "CSGO";
                if (!string.IsNullOrWhiteSpace(Settings.Cs2Path))
                    Settings.CsgoPath = Settings.Cs2Path;
                SaveNow();
            }

            SwitchSpecificFlow();
            SyncActiveSpecificControlsFromSettingsFast();
            SyncGameUi();
            ApplyAuthoritativePigLayout();
            LayoutDualBits();
            ApplySpecificTabContrast();

            bool missingPath = gameTarget == GameTarget.CS2
                ? !ValidCs2(Settings == null ? null : Settings.Cs2Path)
                : !ValidCsgo(Settings == null ? null : Settings.LegacyCsgoPath);

            if (clicked && missingPath)
            {
                MessageBox.Show(GameName() + " is not detected. Use Setting to detect or manually select its install path.",
                    GameName() + " Path", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void SyncGameUi()
        {
            if (!dualReady || IsDisposed) return;

            title_label.Text = GameName() + " Fonts";
            version_label.Text = "Version " + VersionNumber;
            gameHint.Text = gameTarget == GameTarget.CS2
                ? "← click here to change to cs:go"
                : "← click here to change to cs2";

            specificApplyButton.Text = "Apply Specific Font Settings to " + GameName();
            specificApplyButton.Visible = true;

            bool running = gameTarget == GameTarget.CS2 ? IsCs2Running() : IsCsgoRunning();
            restartCs2Button.Enabled = CurrentFormView == FormViews.Main && running;
            restartCs2Button.Text = running ? "Restart " + GameName() : "Restart " + GameName() + " (not running)";

            if (specificRestartButton != null)
            {
                specificRestartButton.Enabled = restartCs2Button.Enabled;
                specificRestartButton.Text = restartCs2Button.Text;
            }

            if (specificToolTip != null)
                specificToolTip.SetToolTip(specificSearchTextBox,
                    "Find family names and predicted " + GameName() + " usage. Enter jumps to the next match.");

            LayoutDualBits();
        }

        private void LayoutDualBits()
        {
            if (!dualReady || gameHint == null || settingLink == null) return;

            gameHint.Location = new Point(title_label.Right + 10, 9);
            settingLink.Location = new Point(Math.Max(70, linkLabel3.Left - 7 - settingLink.Width), ClientSize.Height - 23);
            settingLink.Visible = CurrentFormView == FormViews.Main;
            gameHint.BringToFront();
            settingLink.BringToFront();

            if (gameTarget == GameTarget.CSGO && specificSettingsTabActive && csgoSpecificFlow != null)
                LayoutCsgoFlow();
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

        private void DualApply(object sender, EventArgs e)
        {
            if (CurrentFormView == FormViews.AddSystemFont)
            {
                ImportSelectedSystemFontWithEncodingCheck();
                return;
            }

            if (gameTarget == GameTarget.CS2)
            {
                apply_button_cs2_enhanced_Click(sender, e);
                return;
            }

            ApplyCsgoGeneral();
        }

        private void SpecificApplyButton_Click(object sender, EventArgs e)
        {
            CaptureActiveSpecificControlsToSettings();
            SaveNow();

            if (gameTarget == GameTarget.CS2)
                ApplySpecificFontSettings();
            else
                ApplyCsgoSpecific();
        }

        private void ApplyCsgoGeneral()
        {
            if (listBox1.SelectedItem == null)
            {
                MessageBox.Show("Please select a font first.");
                return;
            }
            if (Settings == null || !ValidCsgo(Settings.LegacyCsgoPath))
            {
                MessageBox.Show("CS:GO path is unknown. Use Setting first.");
                return;
            }

            string directory = Path.Combine(Settings.LegacyCsgoPath, "csgo", "panorama", "fonts");
            string configPath = Path.Combine(directory, "fonts.conf");
            string selection = listBox1.SelectedItem.ToString();
            float scale = GetCurrentFontScale();
            bool useDefault = selection == DefaultFontName;

            string question = (useDefault ? "Apply the CS:GO default font" : "Apply " + selection + " to all CS:GO fonts") +
                              " at " + scale.ToString("0.00", CultureInfo.InvariantCulture) + "x?";
            if (MessageBox.Show(question, "Apply to CS:GO", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;

            try
            {
                if (useDefault && Math.Abs(scale - 1f) < .0001f)
                {
                    RestoreCsgo(configPath, directory);
                    Settings.ActiveFont = DefaultFontName;
                    SaveNow();
                    CsgoDone("Stock CS:GO font configuration restored.");
                    return;
                }

                string family = null;
                string pattern = null;
                CleanCsgoFonts(directory);
                string baseConfig = CsgoBase(File.ReadAllText(configPath));

                if (!useDefault)
                {
                    string sourcePath;
                    string actualFamily;
                    if (!TryFindImportedFont(selection, out sourcePath, out actualFamily))
                        throw new FileNotFoundException("Imported font not found: " + selection);

                    string managedName = "fontmanager_csgo_custom" + Path.GetExtension(sourcePath).ToLowerInvariant();
                    File.Copy(sourcePath, Path.Combine(directory, managedName), true);
                    family = actualFamily;
                    pattern = Path.GetFileNameWithoutExtension(managedName);
                }

                string generated = BuildCsgo(baseConfig, family,
                    pattern == null ? new string[0] : new[] { pattern }, null, scale);
                if (!IsWellFormedXml(generated))
                    throw new InvalidDataException("Generated CS:GO fonts.conf is invalid XML.");

                WriteCsgo(configPath, generated);
                Settings.ActiveFont = selection;
                SaveNow();
                CsgoDone("CS:GO font setting applied successfully.");
            }
            catch (Exception exception)
            {
                AppLog.Error("CS:GO general apply failed.", exception);
                MessageBox.Show("CS:GO apply failed.\n\n" + exception.Message);
            }
        }

        private void CsgoDone(string message)
        {
            MessageBox.Show(message +
                            (IsCsgoRunning() ? "\n\nCS:GO is running. Use Restart CS:GO to load the change." : ""),
                "CS:GO Font");
        }

        private static string CsgoBase(string current)
        {
            if (File.Exists(CsgoGenerated) && File.Exists(CsgoBackup) && current == File.ReadAllText(CsgoGenerated))
                return File.ReadAllText(CsgoBackup);

            string clean = StripCsgo(current);
            File.WriteAllText(CsgoBackup, clean, new UTF8Encoding(false));
            return clean;
        }

        private static string StripCsgo(string config)
        {
            config = Regex.Replace(config,
                "[ \\t]*" + Regex.Escape(CSGO_PATTERN_BEGIN) + ".*?" + Regex.Escape(CSGO_PATTERN_END) + "\\s*",
                "", RegexOptions.Singleline);
            return Regex.Replace(config,
                "[ \\t]*" + Regex.Escape(CSGO_BEGIN) + ".*?" + Regex.Escape(CSGO_END) + "\\s*",
                "", RegexOptions.Singleline);
        }

        private static string BuildCsgo(string baseConfig, string globalFamily, IEnumerable<string> patterns,
            Dictionary<string, string> familyMap, float scale)
        {
            string config = StripCsgo(baseConfig);
            List<string> patternList = patterns.Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (patternList.Count > 0)
            {
                StringBuilder patternBlock = new StringBuilder("\t" + CSGO_PATTERN_BEGIN + "\n");
                foreach (string pattern in patternList)
                    patternBlock.Append("\t<fontpattern>").Append(SecurityElement.Escape(pattern)).Append("</fontpattern>\n");
                patternBlock.Append("\t" + CSGO_PATTERN_END + "\n");

                int cacheIndex = config.IndexOf("<cachedir>", StringComparison.OrdinalIgnoreCase);
                int insertIndex = cacheIndex < 0
                    ? config.LastIndexOf("</fontconfig>", StringComparison.OrdinalIgnoreCase)
                    : config.LastIndexOf('\n', cacheIndex) + 1;
                config = config.Insert(insertIndex, patternBlock.ToString());
            }

            StringBuilder block = new StringBuilder("\t" + CSGO_BEGIN + "\n");
            if (globalFamily != null)
            {
                block.Append("\t<match target=\"pattern\"><edit name=\"family\" mode=\"assign\" binding=\"strong\"><string>")
                    .Append(SecurityElement.Escape(globalFamily)).Append("</string></edit>");
                AddScale(block, scale);
                block.Append("</match>\n");
            }

            if (familyMap != null)
            {
                foreach (KeyValuePair<string, string> mapping in familyMap)
                {
                    if (mapping.Value == null) continue;
                    block.Append("\t<match target=\"pattern\"><test name=\"family\" compare=\"eq\" qual=\"any\"><string>")
                        .Append(SecurityElement.Escape(mapping.Key))
                        .Append("</string></test><edit name=\"family\" mode=\"assign\" binding=\"strong\"><string>")
                        .Append(SecurityElement.Escape(mapping.Value))
                        .Append("</string></edit></match>\n");
                }
            }

            if (globalFamily == null && Math.Abs(scale - 1f) >= .0001f)
            {
                block.Append("\t<match target=\"pattern\">");
                AddScale(block, scale);
                block.Append("</match>\n");
            }

            block.Append("\t" + CSGO_END + "\n");
            int end = config.LastIndexOf("</fontconfig>", StringComparison.OrdinalIgnoreCase);
            if (end < 0)
                throw new InvalidDataException("Legacy fonts.conf has no </fontconfig>.");
            return config.Insert(end, block.ToString());
        }

        private static void AddScale(StringBuilder builder, float scale)
        {
            if (Math.Abs(scale - 1f) < .0001f) return;
            builder.Append("<edit name=\"pixelsize\" mode=\"assign\"><times><name>pixelsize</name><double>")
                .Append(scale.ToString("0.00", CultureInfo.InvariantCulture))
                .Append("</double></times></edit>");
        }

        private static void WriteCsgo(string path, string generated)
        {
            string temporary = path + ".fontmanager.tmp";
            File.WriteAllText(temporary, generated, new UTF8Encoding(false));
            File.Copy(temporary, path, true);
            File.Delete(temporary);
            File.WriteAllText(CsgoGenerated, generated, new UTF8Encoding(false));
        }

        private static void RestoreCsgo(string configPath, string directory)
        {
            string current = File.ReadAllText(configPath);
            if (File.Exists(CsgoGenerated) && File.Exists(CsgoBackup) && current == File.ReadAllText(CsgoGenerated))
                File.Copy(CsgoBackup, configPath, true);
            else
            {
                string clean = StripCsgo(current);
                if (clean != current)
                    File.WriteAllText(configPath, clean, new UTF8Encoding(false));
            }

            if (File.Exists(CsgoGenerated)) File.Delete(CsgoGenerated);
            if (File.Exists(CsgoBackup)) File.Delete(CsgoBackup);
            CleanCsgoFonts(directory);
        }

        private static void CleanCsgoFonts(string directory)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory, "fontmanager_csgo_*.*"))
            {
                if (!IsFontExtension(Path.GetExtension(file))) continue;
                try { File.Delete(file); } catch { }
            }
        }

        private void SwitchSpecificFlow()
        {
            if (cs2SpecificFlow == null)
                cs2SpecificFlow = specificFamilyFlow;

            if (gameTarget == GameTarget.CS2)
            {
                if (csgoSpecificFlow != null)
                    csgoSpecificFlow.Visible = false;
                specificFamilyFlow = cs2SpecificFlow;
                specificFamilyFlow.Visible = specificSettingsTabActive;
                CacheSpecificFamilyControls();
            }
            else
            {
                if (csgoSpecificFlow == null)
                    MakeCsgoFlow();
                cs2SpecificFlow.Visible = false;
                specificFamilyFlow = csgoSpecificFlow;
                specificFamilyFlow.Visible = specificSettingsTabActive;
                CacheCsgoRows();
            }

            if (specificSearchTextBox != null)
                specificSearchTextBox.Text = string.Empty;
        }

        private void MakeCsgoFlow()
        {
            csgoSpecificFlow = new FlowLayoutPanel
            {
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                BackColor = Color.FromArgb(27, 27, 29),
                Padding = new Padding(5),
                Visible = false
            };
            specificSettingsPanel.Controls.Add(csgoSpecificFlow);

            string group = null;
            foreach (FamilySpec spec in CsgoFamilies)
            {
                if (group != spec.Group)
                {
                    group = spec.Group;
                    csgoSpecificFlow.Controls.Add(CreateSpecificGroupHeader(group));
                }
                csgoSpecificFlow.Controls.Add(CreateCsgoSpecificRow(spec));
            }
        }

        private Control CreateCsgoSpecificRow(FamilySpec spec)
        {
            Panel row = new Panel
            {
                Height = 92,
                Margin = new Padding(0, 2, 0, 4),
                BackColor = Color.FromArgb(37, 37, 40),
                BorderStyle = BorderStyle.FixedSingle,
                Tag = spec
            };

            Label family = new Label
            {
                Text = spec.Family,
                Font = new Font("Microsoft Sans Serif", 9f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(10, 8),
                Size = new Size(230, 20)
            };
            Label role = new Label
            {
                Text = spec.Role,
                ForeColor = Color.FromArgb(175, 185, 192),
                Location = new Point(10, 31),
                Size = new Size(390, 48)
            };
            ComboBox combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, 28, 30),
                ForeColor = Color.White,
                Tag = spec.Family
            };
            combo.SetBounds(415, 24, 210, 24);
            combo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            FillCsgoCombo(combo);
            combo.DropDown += delegate { FillCsgoCombo(combo); };
            combo.SelectedIndexChanged += delegate
            {
                if (combo.SelectedItem == null || Settings == null) return;
                if (Settings.CsgoSpecificFontAssignments == null)
                    Settings.CsgoSpecificFontAssignments = new Dictionary<string, string>();
                Settings.CsgoSpecificFontAssignments[combo.Tag.ToString()] = combo.SelectedItem.ToString();
                SaveNow();
            };

            row.Controls.Add(family);
            row.Controls.Add(role);
            row.Controls.Add(combo);
            row.Resize += delegate
            {
                int comboWidth = Math.Max(170, Math.Min(260, row.ClientSize.Width / 3));
                combo.SetBounds(row.ClientSize.Width - comboWidth - 10, 24, comboWidth, 24);
                role.Width = Math.Max(160, combo.Left - role.Left - 12);
            };
            return row;
        }

        private void FillCsgoCombo(ComboBox combo)
        {
            string selected;
            if (combo.SelectedItem != null)
                selected = combo.SelectedItem.ToString();
            else if (Settings != null && Settings.CsgoSpecificFontAssignments != null &&
                     Settings.CsgoSpecificFontAssignments.TryGetValue(combo.Tag.ToString(), out selected))
            {
            }
            else
                selected = SpecificUseGeneral;

            combo.Items.Clear();
            combo.Items.Add(SpecificUseGeneral);
            combo.Items.Add(SpecificValveDefault);
            foreach (string name in GetImportedFontNames())
                combo.Items.Add(name);
            combo.SelectedItem = combo.Items.Contains(selected) ? selected : SpecificUseGeneral;
        }

        private void CacheCsgoRows()
        {
            pigSpecificRows.Clear();
            pigSpecificHeaders.Clear();
            foreach (Control control in csgoSpecificFlow.Controls)
            {
                FamilySpec spec = control.Tag as FamilySpec;
                if (spec != null)
                    pigSpecificRows[spec.Family] = control;
                else
                {
                    Label label = control as Label;
                    if (label != null)
                        pigSpecificHeaders[label.Text] = label;
                }
            }
        }

        private void DualSpecificTab(object sender, EventArgs e)
        {
            if (gameTarget == GameTarget.CS2)
            {
                pigSpecificSettingTabButton_Click(sender, e);
            }
            else
            {
                specificSettingsTabActive = true;
                SwitchSpecificFlow();
                RefreshSpecificGeneralSelectionLabel();
                LayoutSpecificSettingsUi();
                LayoutPigSpecificTopRow();
                LayoutCsgoFlow();
                EnsureAllSpecificRowsVisible();
                NavigateSpecificSearch(false);
                UpdateSpecificTabVisuals();
                SyncSpecificPreviewBridge();
            }

            SyncActiveSpecificControlsFromSettingsFast();
            ApplySpecificTabContrast();
            SyncGameUi();
        }

        private void DualSpecificView(object sender, EventArgs e)
        {
            if (gameTarget == GameTarget.CS2)
            {
                pigUiV2_ViewChanged(sender, e);
                return;
            }

            if (csgoSpecificFlow == null || specificViewCombo.SelectedItem == null) return;
            bool all = specificViewCombo.SelectedItem.ToString() == "All families";
            csgoSpecificFlow.Controls.Clear();

            if (all)
            {
                foreach (FamilySpec spec in CsgoFamilies.OrderBy(x => x.Family, StringComparer.OrdinalIgnoreCase))
                {
                    Control row;
                    if (pigSpecificRows.TryGetValue(spec.Family, out row))
                        csgoSpecificFlow.Controls.Add(row);
                }
            }
            else
            {
                string group = null;
                foreach (FamilySpec spec in CsgoFamilies)
                {
                    if (group != spec.Group)
                    {
                        group = spec.Group;
                        Control header;
                        if (pigSpecificHeaders.TryGetValue(group.ToUpperInvariant(), out header))
                            csgoSpecificFlow.Controls.Add(header);
                    }
                    Control row;
                    if (pigSpecificRows.TryGetValue(spec.Family, out row))
                        csgoSpecificFlow.Controls.Add(row);
                }
            }

            EnsureAllSpecificRowsVisible();
            LayoutCsgoFlow();
            NavigateSpecificSearch(false);
        }

        private void LayoutCsgoFlow()
        {
            if (csgoSpecificFlow == null || !specificSettingsTabActive) return;
            int width = specificSettingsPanel.ClientSize.Width;
            int height = specificSettingsPanel.ClientSize.Height;
            int applyY = height - 30 - 7 - 42;
            csgoSpecificFlow.SetBounds(0, 37, width, Math.Max(100, applyY - 44));
            csgoSpecificFlow.Visible = true;
            csgoSpecificFlow.BringToFront();
            ResizeSpecificFamilyRows();
        }

        private void ApplyCsgoSpecific()
        {
            if (Settings == null || !ValidCsgo(Settings.LegacyCsgoPath))
            {
                MessageBox.Show("CS:GO path is unknown. Use Setting first.");
                return;
            }

            string directory = Path.Combine(Settings.LegacyCsgoPath, "csgo", "panorama", "fonts");
            string configPath = Path.Combine(directory, "fonts.conf");
            string general = listBox1.SelectedItem == null ? DefaultFontName : listBox1.SelectedItem.ToString();
            float scale = GetCurrentFontScale();

            Dictionary<string, string> selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (FamilySpec spec in CsgoFamilies)
            {
                string value;
                if (Settings.CsgoSpecificFontAssignments == null ||
                    !Settings.CsgoSpecificFontAssignments.TryGetValue(spec.Family, out value))
                    value = SpecificUseGeneral;

                selections[spec.Family] = value == SpecificValveDefault
                    ? null
                    : value == SpecificUseGeneral
                        ? (general == DefaultFontName ? null : general)
                        : value;
            }

            if (MessageBox.Show("Apply CS:GO Specific Setting?\n\n" +
                                selections.Count(x => x.Value != null) + " families will use imported replacements.\n" +
                                "Global size: " + scale.ToString("0.00", CultureInfo.InvariantCulture) + "x",
                    "CS:GO Specific", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;

            try
            {
                CleanCsgoFonts(directory);
                string baseConfig = CsgoBase(File.ReadAllText(configPath));
                Dictionary<string, string> actualFamilies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                List<string> patterns = new List<string>();
                int index = 0;

                foreach (string selection in selections.Values.Where(x => x != null).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string sourcePath;
                    string actualFamily;
                    if (!TryFindImportedFont(selection, out sourcePath, out actualFamily))
                        throw new FileNotFoundException("Imported font not found: " + selection);

                    string managedName = "fontmanager_csgo_specific_" + (index++).ToString("00") +
                                         Path.GetExtension(sourcePath).ToLowerInvariant();
                    File.Copy(sourcePath, Path.Combine(directory, managedName), true);
                    actualFamilies[selection] = actualFamily;
                    patterns.Add(Path.GetFileNameWithoutExtension(managedName));
                }

                Dictionary<string, string> familyMap = new Dictionary<string, string>();
                foreach (KeyValuePair<string, string> selection in selections)
                    familyMap[selection.Key] = selection.Value == null ? null : actualFamilies[selection.Value];

                string generated = BuildCsgo(baseConfig, null, patterns, familyMap, scale);
                if (!IsWellFormedXml(generated))
                    throw new InvalidDataException("Generated CS:GO specific config is invalid XML.");

                WriteCsgo(configPath, generated);
                Settings.ActiveFont = "Specific Setting";
                SaveNow();
                CsgoDone("CS:GO specific font settings applied successfully.");
            }
            catch (Exception exception)
            {
                AppLog.Error("CS:GO specific apply failed.", exception);
                MessageBox.Show("CS:GO specific apply failed.\n\n" + exception.Message);
            }
        }

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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CSGO_Font_Manager
{
    public class Settings
    {
        // Backward-compatibility alias used by the original/CS2 code. 4.0 keeps this synced to Cs2Path.
        public string CsgoPath { get; set; }
        public string Cs2Path { get; set; }
        public string LegacyCsgoPath { get; set; }
        public string ActiveGame { get; set; } = "CS2";

        public bool ProTips { get; set; } = true;
        public bool HideNewUpdates { get; set; }
        public string ActiveFont { get; set; }
        public float FontScale { get; set; } = 1.00f;
        public string SpecificFontViewMode { get; set; } = "Group by UI role";
        public Dictionary<string, string> SpecificFontAssignments { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> CsgoSpecificFontAssignments { get; set; } = new Dictionary<string, string>();
    }

    public partial class Form1
    {
        private enum GameTarget { CS2, CSGO }
        private GameTarget gameTarget;
        private bool dualReady;
        private Label gameHint;
        private LinkLabel settingLink;
        private FlowLayoutPanel cs2SpecificFlow, csgoSpecificFlow;
        private const string CSGO_BEGIN = "<!-- Font Manager 4.0 CS:GO begin -->";
        private const string CSGO_END = "<!-- Font Manager 4.0 CS:GO end -->";
        private const string CSGO_PATTERN_BEGIN = "<!-- Font Manager 4.0 CS:GO patterns begin -->";
        private const string CSGO_PATTERN_END = "<!-- Font Manager 4.0 CS:GO patterns end -->";
        private static string CsgoBackup => DataPath + "fonts.conf.csgo.original";
        private static string CsgoGenerated => DataPath + "fonts.conf.csgo.generated";
        private static readonly bool DualBootstrap = RegisterDualBootstrap();

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

        static Form1() { VersionNumber = "4.0 (forked)"; }
        private static bool RegisterDualBootstrap() { Application.Idle += DualIdle; return true; }
        private static void DualIdle(object s, EventArgs e)
        {
            foreach (Form f in Application.OpenForms)
            {
                Form1 x = f as Form1;
                if (x != null && !x.dualReady && x.pigUiV2Initialized && x.restartCs2Button != null && x.specificSearchTextBox != null)
                    x.InitDualGame();
            }
        }

        private void InitDualGame()
        {
            dualReady = true;
            InitPaths();
            gameTarget = Settings != null && string.Equals(Settings.ActiveGame, "CSGO", StringComparison.OrdinalIgnoreCase) ? GameTarget.CSGO : GameTarget.CS2;
            cs2SpecificFlow = specificFamilyFlow;
            version_label.Text = "Version 4.0 (forked)";
            title_label.Cursor = Cursors.Hand;
            title_label.Click += (s, e) => { gameTarget = gameTarget == GameTarget.CS2 ? GameTarget.CSGO : GameTarget.CS2; ApplyGameMode(true); };

            gameHint = new Label { AutoSize = true, Font = new Font("Segoe Script", 9f), ForeColor = Color.FromArgb(130, 145, 150), BackColor = Color.Transparent };
            Controls.Add(gameHint);
            settingLink = new LinkLabel { AutoSize = true, Text = "Setting", LinkColor = linkLabel3.LinkColor, ActiveLinkColor = linkLabel3.ActiveLinkColor };
            settingLink.LinkClicked += (s, e) => ShowPathSettings();
            Controls.Add(settingLink);

            apply_button.Click -= pigUiV2_ApplyButtonClick;
            apply_button.Click += DualApply;
            specificApplyButton.Click -= specificApplyButton_Click;
            specificApplyButton.Click += (s, e) => { if (gameTarget == GameTarget.CS2) ApplySpecificFontSettings(); else ApplyCsgoSpecific(); };
            restartCs2Button.Click -= restartCs2Button_Click;
            restartCs2Button.Click += DualRestart;
            specificSettingTabButton.Click -= pigSpecificSettingTabButton_Click;
            specificSettingTabButton.Click += DualSpecificTab;
            specificViewCombo.SelectedIndexChanged -= pigUiV2_ViewChanged;
            specificViewCombo.SelectedIndexChanged += DualSpecificView;
            SizeChanged += (s, e) => LayoutDualBits();
            if (cs2ProcessTimer != null) cs2ProcessTimer.Tick += (s, e) => SyncGameUi();
            ApplyGameMode(false);
            AppLog.Info("Font Manager 4.0 dual-game prototype ready; active=" + GameName() + ".");
        }

        private string GameName() { return gameTarget == GameTarget.CS2 ? "CS2" : "CS:GO"; }
        private void SaveNow() { try { if (SettingsManager != null && Settings != null) SettingsManager.Save(Settings); } catch { } }
        private void InitPaths()
        {
            if (Settings == null) return;
            if (string.IsNullOrWhiteSpace(Settings.Cs2Path) || !ValidCs2(Settings.Cs2Path))
                Settings.Cs2Path = ValidCs2(Settings.CsgoPath) ? Settings.CsgoPath : FindCs2();
            if (!string.IsNullOrWhiteSpace(Settings.Cs2Path)) Settings.CsgoPath = Settings.Cs2Path;
            if (string.IsNullOrWhiteSpace(Settings.LegacyCsgoPath) || !ValidCsgo(Settings.LegacyCsgoPath)) Settings.LegacyCsgoPath = FindCsgo();
            if (string.IsNullOrWhiteSpace(Settings.ActiveGame)) Settings.ActiveGame = "CS2";
            if (Settings.CsgoSpecificFontAssignments == null) Settings.CsgoSpecificFontAssignments = new Dictionary<string, string>();
            SaveNow();
        }

        private void ApplyGameMode(bool clicked)
        {
            if (Settings != null) { Settings.ActiveGame = gameTarget == GameTarget.CS2 ? "CS2" : "CSGO"; if (!string.IsNullOrWhiteSpace(Settings.Cs2Path)) Settings.CsgoPath = Settings.Cs2Path; SaveNow(); }
            SwitchSpecificFlow();
            SyncGameUi();
            ApplyAuthoritativePigLayout();
            LayoutDualBits();
            if (clicked && ((gameTarget == GameTarget.CS2 && !ValidCs2(Settings == null ? null : Settings.Cs2Path)) || (gameTarget == GameTarget.CSGO && !ValidCsgo(Settings == null ? null : Settings.LegacyCsgoPath))))
                MessageBox.Show(GameName() + " is not detected. Use Setting to detect or manually select its install path.", GameName() + " Path", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SyncGameUi()
        {
            if (!dualReady || IsDisposed) return;
            title_label.Text = GameName() + " Fonts";
            version_label.Text = "Version 4.0 (forked)";
            gameHint.Text = gameTarget == GameTarget.CS2 ? "← click here to change to cs:go" : "← click here to change to cs2";
            specificApplyButton.Text = "Apply Specific Font Settings to " + GameName();
            bool run = gameTarget == GameTarget.CS2 ? IsCs2Running() : IsCsgoRunning();
            restartCs2Button.Enabled = CurrentFormView == FormViews.Main && run;
            restartCs2Button.Text = run ? "Restart " + GameName() : "Restart " + GameName() + " (not running)";
            if (specificRestartButton != null) { specificRestartButton.Enabled = restartCs2Button.Enabled; specificRestartButton.Text = restartCs2Button.Text; }
            if (specificToolTip != null) specificToolTip.SetToolTip(specificSearchTextBox, "Find family names and predicted " + GameName() + " usage. Enter jumps to the next match.");
            LayoutDualBits();
        }

        private void LayoutDualBits()
        {
            if (!dualReady || gameHint == null || settingLink == null) return;
            gameHint.Location = new Point(title_label.Right + 10, 9);
            settingLink.Location = new Point(Math.Max(70, linkLabel3.Left - 7 - settingLink.Width), ClientSize.Height - 23);
            settingLink.Visible = CurrentFormView == FormViews.Main;
            gameHint.BringToFront(); settingLink.BringToFront();
            if (gameTarget == GameTarget.CSGO && specificSettingsTabActive && csgoSpecificFlow != null) LayoutCsgoFlow();
        }

        private void ShowPathSettings()
        {
            using (Form f = new Form())
            {
                f.Text = "Setting - Game Paths"; f.StartPosition = FormStartPosition.CenterParent; f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false; f.ClientSize = new Size(760, 255); f.BackColor = BackColor; f.ForeColor = Color.White;
                TextBox c2 = PathBox(Settings == null ? null : Settings.Cs2Path, 15, 55), cg = PathBox(Settings == null ? null : Settings.LegacyCsgoPath, 15, 135);
                f.Controls.Add(new Label { Text = "CS2 (Steam app 730)", AutoSize = true, Location = new Point(15, 35), ForeColor = Color.White });
                f.Controls.Add(new Label { Text = "CS:GO (app 4465480; app 730 legacy-beta fallback)", AutoSize = true, Location = new Point(15, 115), ForeColor = Color.White });
                f.Controls.Add(c2); f.Controls.Add(cg);
                Button d2 = PButton("Detect", 585, 53), b2 = PButton("Browse", 665, 53), dg = PButton("Detect", 585, 133), bg = PButton("Browse", 665, 133);
                d2.Click += (s, e) => { string p = FindCs2(); if (p != null) c2.Text = p; };
                dg.Click += (s, e) => { string p = FindCsgo(); if (p != null) cg.Text = p; };
                b2.Click += (s, e) => { string p = BrowsePath(c2.Text); if (p != null) c2.Text = p; };
                bg.Click += (s, e) => { string p = BrowsePath(cg.Text); if (p != null) cg.Text = p; };
                f.Controls.Add(d2); f.Controls.Add(b2); f.Controls.Add(dg); f.Controls.Add(bg);
                Button ok = PButton("Save", 585, 205), cancel = PButton("Cancel", 665, 205); cancel.DialogResult = DialogResult.Cancel;
                ok.BackColor = Color.MediumSpringGreen; cancel.BackColor = Color.FromArgb(120, 190, 255);
                ok.Click += (s, e) =>
                {
                    string p2 = c2.Text.Trim(), pg = cg.Text.Trim();
                    if (p2.Length > 0 && !ValidCs2(p2)) { MessageBox.Show("Invalid CS2 path. Expected game\\bin\\win64\\cs2.exe and game\\csgo\\panorama\\fonts\\fonts.conf."); return; }
                    if (pg.Length > 0 && !ValidCsgo(pg)) { MessageBox.Show("Invalid CS:GO path. Expected csgo.exe and csgo\\panorama\\fonts\\fonts.conf."); return; }
                    Settings.Cs2Path = p2.Length == 0 ? null : p2; Settings.CsgoPath = Settings.Cs2Path; Settings.LegacyCsgoPath = pg.Length == 0 ? null : pg; SaveNow(); f.DialogResult = DialogResult.OK; f.Close();
                };
                f.Controls.Add(ok); f.Controls.Add(cancel); f.CancelButton = cancel; f.ShowDialog(this);
            }
            SyncGameUi();
        }
        private TextBox PathBox(string t, int x, int y) { return new TextBox { Text = t ?? "", Location = new Point(x, y), Size = new Size(555, 23), BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle }; }
        private Button PButton(string t, int x, int y) { return new Button { Text = t, Location = new Point(x, y), Size = new Size(72, 27), FlatStyle = FlatStyle.Popup, BackColor = Color.FromArgb(70, 75, 80), ForeColor = Color.White }; }
        private string BrowsePath(string cur) { using (FolderBrowserDialog b = new FolderBrowserDialog()) { if (Directory.Exists(cur)) b.SelectedPath = cur; return b.ShowDialog(this) == DialogResult.OK ? b.SelectedPath : null; } }

        private static IEnumerable<string> SteamRoots()
        {
            HashSet<string> r = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string s = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string;
            if (!string.IsNullOrWhiteSpace(s)) r.Add(s.Replace('/', '\\'));
            string v = string.IsNullOrWhiteSpace(s) ? null : Path.Combine(s.Replace('/', '\\'), "steamapps", "libraryfolders.vdf");
            if (v != null && File.Exists(v))
            {
                string x = File.ReadAllText(v);
                foreach (Match m in Regex.Matches(x, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)) r.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                foreach (Match m in Regex.Matches(x, "^\\s*\"\\d+\"\\s+\"([^\"]+)\"", RegexOptions.Multiline)) r.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            }
            return r.Where(Directory.Exists);
        }
        private static string ManifestDir(string p) { if (!File.Exists(p)) return null; Match m = Regex.Match(File.ReadAllText(p), "\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase); return m.Success ? m.Groups[1].Value : null; }
        private static string FindCs2()
        {
            foreach (string r in SteamRoots()) { string a = Path.Combine(r, "steamapps"), d = ManifestDir(Path.Combine(a, "appmanifest_730.acf")); if (d != null) { string p = Path.Combine(a, "common", d); if (ValidCs2(p)) return p; } string q = Path.Combine(a, "common", "Counter-Strike Global Offensive"); if (ValidCs2(q)) return q; } return null;
        }
        private static string FindCsgo()
        {
            foreach (string r in SteamRoots()) { string a = Path.Combine(r, "steamapps"), d = ManifestDir(Path.Combine(a, "appmanifest_4465480.acf")); if (d != null) { string p = Path.Combine(a, "common", d); if (ValidCsgo(p)) return p; } string q = Path.Combine(a, "common", "csgo legacy"); if (ValidCsgo(q)) return q; }
            foreach (string r in SteamRoots()) { string p = Path.Combine(r, "steamapps", "common", "Counter-Strike Global Offensive"); if (ValidCsgo(p)) return p; } return null;
        }
        private static bool ValidCs2(string p) { return !string.IsNullOrWhiteSpace(p) && Directory.Exists(p) && File.Exists(Path.Combine(p, "game", "bin", "win64", "cs2.exe")) && File.Exists(Path.Combine(p, "game", "csgo", "panorama", "fonts", "fonts.conf")); }
        private static bool ValidCsgo(string p) { return !string.IsNullOrWhiteSpace(p) && Directory.Exists(p) && File.Exists(Path.Combine(p, "csgo.exe")) && File.Exists(Path.Combine(p, "csgo", "panorama", "fonts", "fonts.conf")); }

        private void DualApply(object sender, EventArgs e)
        {
            if (CurrentFormView == FormViews.AddSystemFont) { ImportSelectedSystemFontWithEncodingCheck(); return; }
            if (gameTarget == GameTarget.CS2) { apply_button_cs2_enhanced_Click(sender, e); return; }
            ApplyCsgoGeneral();
        }
        private void ApplyCsgoGeneral()
        {
            if (listBox1.SelectedItem == null) { MessageBox.Show("Please select a font first."); return; }
            if (Settings == null || !ValidCsgo(Settings.LegacyCsgoPath)) { MessageBox.Show("CS:GO path is unknown. Use Setting first."); return; }
            string dir = Path.Combine(Settings.LegacyCsgoPath, "csgo", "panorama", "fonts"), conf = Path.Combine(dir, "fonts.conf"), sel = listBox1.SelectedItem.ToString();
            float scale = GetCurrentFontScale(); bool def = sel == DefaultFontName;
            if (MessageBox.Show((def ? "Apply the CS:GO default font" : "Apply " + sel + " to all CS:GO fonts") + " at " + scale.ToString("0.00", CultureInfo.InvariantCulture) + "x?", "Apply to CS:GO", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                if (def && Math.Abs(scale - 1f) < .0001f) { RestoreCsgo(conf, dir); Settings.ActiveFont = DefaultFontName; SaveNow(); CsgoDone("Stock CS:GO font configuration restored."); return; }
                string family = null, pattern = null; CleanCsgoFonts(dir); string baseConf = CsgoBase(File.ReadAllText(conf));
                if (!def) { string src, fam; if (!TryFindImportedFont(sel, out src, out fam)) throw new FileNotFoundException("Imported font not found: " + sel); string name = "fontmanager_csgo_custom" + Path.GetExtension(src).ToLowerInvariant(); File.Copy(src, Path.Combine(dir, name), true); family = fam; pattern = Path.GetFileNameWithoutExtension(name); }
                string generated = BuildCsgo(baseConf, family, pattern == null ? new string[0] : new[] { pattern }, null, scale); if (!IsWellFormedXml(generated)) throw new InvalidDataException("Generated CS:GO fonts.conf is invalid XML."); WriteCsgo(conf, generated); Settings.ActiveFont = sel; SaveNow(); CsgoDone("CS:GO font setting applied successfully.");
            }
            catch (Exception ex) { AppLog.Error("CS:GO general apply failed.", ex); MessageBox.Show("CS:GO apply failed.\n\n" + ex.Message); }
        }
        private void CsgoDone(string m) { MessageBox.Show(m + (IsCsgoRunning() ? "\n\nCS:GO is running. Use Restart CS:GO to load the change." : ""), "CS:GO Font"); }

        private static string CsgoBase(string cur)
        {
            if (File.Exists(CsgoGenerated) && File.Exists(CsgoBackup) && cur == File.ReadAllText(CsgoGenerated)) return File.ReadAllText(CsgoBackup);
            string clean = StripCsgo(cur); File.WriteAllText(CsgoBackup, clean, new UTF8Encoding(false)); return clean;
        }
        private static string StripCsgo(string x)
        {
            x = Regex.Replace(x, "[ \\t]*" + Regex.Escape(CSGO_PATTERN_BEGIN) + ".*?" + Regex.Escape(CSGO_PATTERN_END) + "\\s*", "", RegexOptions.Singleline);
            return Regex.Replace(x, "[ \\t]*" + Regex.Escape(CSGO_BEGIN) + ".*?" + Regex.Escape(CSGO_END) + "\\s*", "", RegexOptions.Singleline);
        }
        private static string BuildCsgo(string baseConf, string globalFamily, IEnumerable<string> patterns, Dictionary<string, string> familyMap, float scale)
        {
            string x = StripCsgo(baseConf); List<string> p = patterns.Where(z => !string.IsNullOrWhiteSpace(z)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (p.Count > 0) { StringBuilder q = new StringBuilder("\t" + CSGO_PATTERN_BEGIN + "\n"); foreach (string z in p) q.Append("\t<fontpattern>").Append(SecurityElement.Escape(z)).Append("</fontpattern>\n"); q.Append("\t" + CSGO_PATTERN_END + "\n"); int c = x.IndexOf("<cachedir>", StringComparison.OrdinalIgnoreCase); x = x.Insert(c < 0 ? x.LastIndexOf("</fontconfig>", StringComparison.OrdinalIgnoreCase) : x.LastIndexOf('\n', c) + 1, q.ToString()); }
            StringBuilder b = new StringBuilder("\t" + CSGO_BEGIN + "\n");
            if (globalFamily != null) { b.Append("\t<match target=\"pattern\"><edit name=\"family\" mode=\"assign\" binding=\"strong\"><string>").Append(SecurityElement.Escape(globalFamily)).Append("</string></edit>"); AddScale(b, scale); b.Append("</match>\n"); }
            if (familyMap != null) foreach (KeyValuePair<string, string> m in familyMap) if (m.Value != null) b.Append("\t<match target=\"pattern\"><test name=\"family\" compare=\"eq\" qual=\"any\"><string>").Append(SecurityElement.Escape(m.Key)).Append("</string></test><edit name=\"family\" mode=\"assign\" binding=\"strong\"><string>").Append(SecurityElement.Escape(m.Value)).Append("</string></edit></match>\n");
            if (globalFamily == null && Math.Abs(scale - 1f) >= .0001f) { b.Append("\t<match target=\"pattern\">"); AddScale(b, scale); b.Append("</match>\n"); }
            b.Append("\t" + CSGO_END + "\n"); int end = x.LastIndexOf("</fontconfig>", StringComparison.OrdinalIgnoreCase); if (end < 0) throw new InvalidDataException("Legacy fonts.conf has no </fontconfig>."); return x.Insert(end, b.ToString());
        }
        private static void AddScale(StringBuilder b, float s) { if (Math.Abs(s - 1f) < .0001f) return; b.Append("<edit name=\"pixelsize\" mode=\"assign\"><times><name>pixelsize</name><double>").Append(s.ToString("0.00", CultureInfo.InvariantCulture)).Append("</double></times></edit>"); }
        private static void WriteCsgo(string path, string generated) { string t = path + ".fontmanager.tmp"; File.WriteAllText(t, generated, new UTF8Encoding(false)); File.Copy(t, path, true); File.Delete(t); File.WriteAllText(CsgoGenerated, generated, new UTF8Encoding(false)); }
        private static void RestoreCsgo(string conf, string dir) { string cur = File.ReadAllText(conf); if (File.Exists(CsgoGenerated) && File.Exists(CsgoBackup) && cur == File.ReadAllText(CsgoGenerated)) File.Copy(CsgoBackup, conf, true); else { string clean = StripCsgo(cur); if (clean != cur) File.WriteAllText(conf, clean, new UTF8Encoding(false)); } if (File.Exists(CsgoGenerated)) File.Delete(CsgoGenerated); if (File.Exists(CsgoBackup)) File.Delete(CsgoBackup); CleanCsgoFonts(dir); }
        private static void CleanCsgoFonts(string dir) { if (!Directory.Exists(dir)) return; foreach (string f in Directory.GetFiles(dir, "fontmanager_csgo_*.*")) if (IsFontExtension(Path.GetExtension(f))) try { File.Delete(f); } catch { } }

        private void SwitchSpecificFlow()
        {
            if (cs2SpecificFlow == null) cs2SpecificFlow = specificFamilyFlow;
            if (gameTarget == GameTarget.CS2)
            {
                if (csgoSpecificFlow != null) csgoSpecificFlow.Visible = false;
                specificFamilyFlow = cs2SpecificFlow;
                specificFamilyFlow.Visible = specificSettingsTabActive;
                CacheSpecificFamilyControls();
            }
            else
            {
                if (csgoSpecificFlow == null) MakeCsgoFlow();
                cs2SpecificFlow.Visible = false;
                specificFamilyFlow = csgoSpecificFlow;
                specificFamilyFlow.Visible = specificSettingsTabActive;
                CacheCsgoRows();
            }
            if (specificSearchTextBox != null) specificSearchTextBox.Text = "";
        }
        private void MakeCsgoFlow()
        {
            csgoSpecificFlow = new FlowLayoutPanel { AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, BackColor = Color.FromArgb(27, 27, 29), Padding = new Padding(5), Visible = false };
            specificSettingsPanel.Controls.Add(csgoSpecificFlow);
            string g = null; foreach (FamilySpec s in CsgoFamilies) { if (g != s.Group) { g = s.Group; csgoSpecificFlow.Controls.Add(CreateSpecificGroupHeader(g)); } csgoSpecificFlow.Controls.Add(CsgoRow(s)); }
        }
        private Control CsgoRow(FamilySpec s)
        {
            Panel r = new Panel { Height = 92, Margin = new Padding(0, 2, 0, 4), BackColor = Color.FromArgb(37, 37, 40), BorderStyle = BorderStyle.FixedSingle, Tag = s };
            Label n = new Label { Text = s.Family, Font = new Font("Microsoft Sans Serif", 9f, FontStyle.Bold), ForeColor = Color.White, Location = new Point(10, 8), Size = new Size(230, 20) };
            Label d = new Label { Text = s.Role, ForeColor = Color.FromArgb(175, 185, 192), Location = new Point(10, 31), Size = new Size(390, 48) };
            ComboBox c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(28, 28, 30), ForeColor = Color.White, Tag = s.Family };
            c.SetBounds(415, 24, 210, 24); c.Anchor = AnchorStyles.Top | AnchorStyles.Right; FillCsgoCombo(c); c.DropDown += (a, b) => FillCsgoCombo((ComboBox)a); c.SelectedIndexChanged += (a, b) => { ComboBox z = (ComboBox)a; if (z.SelectedItem != null && Settings != null) { if (Settings.CsgoSpecificFontAssignments == null) Settings.CsgoSpecificFontAssignments = new Dictionary<string, string>(); Settings.CsgoSpecificFontAssignments[z.Tag.ToString()] = z.SelectedItem.ToString(); SaveNow(); } };
            r.Controls.Add(n); r.Controls.Add(d); r.Controls.Add(c); r.Resize += (a, b) => { int w = Math.Max(170, Math.Min(260, r.ClientSize.Width / 3)); c.SetBounds(r.ClientSize.Width - w - 10, 24, w, 24); d.Width = Math.Max(160, c.Left - d.Left - 12); }; return r;
        }
        private void FillCsgoCombo(ComboBox c)
        {
            string old; if (c.SelectedItem != null) old = c.SelectedItem.ToString(); else if (Settings != null && Settings.CsgoSpecificFontAssignments != null && Settings.CsgoSpecificFontAssignments.TryGetValue(c.Tag.ToString(), out old)) { } else old = SpecificUseGeneral;
            c.Items.Clear(); c.Items.Add(SpecificUseGeneral); c.Items.Add(SpecificValveDefault); foreach (string n in GetImportedFontNames()) c.Items.Add(n); c.SelectedItem = c.Items.Contains(old) ? old : SpecificUseGeneral;
        }
        private void CacheCsgoRows()
        {
            pigSpecificRows.Clear(); pigSpecificHeaders.Clear(); foreach (Control c in csgoSpecificFlow.Controls) { FamilySpec s = c.Tag as FamilySpec; if (s != null) pigSpecificRows[s.Family] = c; else { Label l = c as Label; if (l != null) pigSpecificHeaders[l.Text] = l; } }
        }
        private void DualSpecificTab(object sender, EventArgs e)
        {
            if (gameTarget == GameTarget.CS2) { pigSpecificSettingTabButton_Click(sender, e); return; }
            specificSettingsTabActive = true; SwitchSpecificFlow(); RefreshSpecificGeneralSelectionLabel(); LayoutSpecificSettingsUi(); LayoutPigSpecificTopRow(); LayoutCsgoFlow(); EnsureAllSpecificRowsVisible(); NavigateSpecificSearch(false); UpdateSpecificTabVisuals(); SyncSpecificPreviewBridge(); SyncGameUi();
        }
        private void DualSpecificView(object sender, EventArgs e)
        {
            if (gameTarget == GameTarget.CS2) { pigUiV2_ViewChanged(sender, e); return; }
            if (csgoSpecificFlow == null || specificViewCombo.SelectedItem == null) return; bool all = specificViewCombo.SelectedItem.ToString() == "All families"; csgoSpecificFlow.Controls.Clear();
            if (all) foreach (FamilySpec s in CsgoFamilies.OrderBy(x => x.Family, StringComparer.OrdinalIgnoreCase)) { Control r; if (pigSpecificRows.TryGetValue(s.Family, out r)) csgoSpecificFlow.Controls.Add(r); }
            else { string g = null; foreach (FamilySpec s in CsgoFamilies) { if (g != s.Group) { g = s.Group; Control h; if (pigSpecificHeaders.TryGetValue(g.ToUpperInvariant(), out h)) csgoSpecificFlow.Controls.Add(h); } Control r; if (pigSpecificRows.TryGetValue(s.Family, out r)) csgoSpecificFlow.Controls.Add(r); } }
            EnsureAllSpecificRowsVisible(); LayoutCsgoFlow(); NavigateSpecificSearch(false);
        }
        private void LayoutCsgoFlow()
        {
            if (csgoSpecificFlow == null || !specificSettingsTabActive) return; int w = specificSettingsPanel.ClientSize.Width, h = specificSettingsPanel.ClientSize.Height, ay = h - 30 - 7 - 42; csgoSpecificFlow.SetBounds(0, 37, w, Math.Max(100, ay - 44)); csgoSpecificFlow.Visible = true; csgoSpecificFlow.BringToFront(); ResizeSpecificFamilyRows();
        }
        private void ApplyCsgoSpecific()
        {
            if (Settings == null || !ValidCsgo(Settings.LegacyCsgoPath)) { MessageBox.Show("CS:GO path is unknown. Use Setting first."); return; }
            string dir = Path.Combine(Settings.LegacyCsgoPath, "csgo", "panorama", "fonts"), conf = Path.Combine(dir, "fonts.conf"), general = listBox1.SelectedItem == null ? DefaultFontName : listBox1.SelectedItem.ToString(); float scale = GetCurrentFontScale();
            Dictionary<string, string> pick = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); foreach (FamilySpec s in CsgoFamilies) { string v; if (Settings.CsgoSpecificFontAssignments == null || !Settings.CsgoSpecificFontAssignments.TryGetValue(s.Family, out v)) v = SpecificUseGeneral; pick[s.Family] = v == SpecificValveDefault ? null : v == SpecificUseGeneral ? (general == DefaultFontName ? null : general) : v; }
            if (MessageBox.Show("Apply CS:GO Specific Setting?\n\n" + pick.Count(x => x.Value != null) + " families will use imported replacements.\nGlobal size: " + scale.ToString("0.00", CultureInfo.InvariantCulture) + "x", "CS:GO Specific", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                CleanCsgoFonts(dir); string baseConf = CsgoBase(File.ReadAllText(conf)); Dictionary<string, string> actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); List<string> patterns = new List<string>(); int i = 0;
                foreach (string sel in pick.Values.Where(x => x != null).Distinct(StringComparer.OrdinalIgnoreCase)) { string src, fam; if (!TryFindImportedFont(sel, out src, out fam)) throw new FileNotFoundException("Imported font not found: " + sel); string n = "fontmanager_csgo_specific_" + (i++).ToString("00") + Path.GetExtension(src).ToLowerInvariant(); File.Copy(src, Path.Combine(dir, n), true); actual[sel] = fam; patterns.Add(Path.GetFileNameWithoutExtension(n)); }
                Dictionary<string, string> map = new Dictionary<string, string>(); foreach (KeyValuePair<string, string> z in pick) map[z.Key] = z.Value == null ? null : actual[z.Value]; string generated = BuildCsgo(baseConf, null, patterns, map, scale); if (!IsWellFormedXml(generated)) throw new InvalidDataException("Generated CS:GO specific config is invalid XML."); WriteCsgo(conf, generated); Settings.ActiveFont = "Specific Setting"; SaveNow(); CsgoDone("CS:GO specific font settings applied successfully.");
            }
            catch (Exception ex) { AppLog.Error("CS:GO specific apply failed.", ex); MessageBox.Show("CS:GO specific apply failed.\n\n" + ex.Message); }
        }

        private static bool IsCsgoRunning() { try { return Process.GetProcessesByName("csgo").Any(p => !p.HasExited); } catch { return false; } }
        private async void DualRestart(object sender, EventArgs e)
        {
            string proc = gameTarget == GameTarget.CS2 ? "cs2" : "csgo", game = GameName(); Process[] ps = Process.GetProcessesByName(proc); if (ps.Length == 0) { SyncGameUi(); return; }
            if (MessageBox.Show("Close " + game + " and relaunch it through Steam now?", "Restart " + game, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            restartCs2Button.Enabled = false; restartCs2Button.Text = "Closing " + game + "...";
            try
            {
                foreach (Process p in ps) try { if (!p.HasExited) p.CloseMainWindow(); } catch { }
                bool gone = await Task.Run(() => { Stopwatch sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < 15000) { bool any = false; foreach (Process p in ps) try { if (!p.HasExited) any = true; } catch { } if (!any) return true; System.Threading.Thread.Sleep(200); } return false; });
                if (!gone) { MessageBox.Show(game + " did not close within 15 seconds."); return; }
                int app = gameTarget == GameTarget.CS2 ? 730 : (Settings != null && !string.IsNullOrWhiteSpace(Settings.LegacyCsgoPath) && new DirectoryInfo(Settings.LegacyCsgoPath).Name.Equals("csgo legacy", StringComparison.OrdinalIgnoreCase) ? 4465480 : 730);
                Process.Start(new ProcessStartInfo { FileName = "steam://rungameid/" + app, UseShellExecute = true }); AppLog.Info("Restarted " + game + " through Steam app " + app + ".");
            }
            catch (Exception ex) { AppLog.Error("Restart " + game + " failed.", ex); MessageBox.Show("Restart failed.\n\n" + ex.Message); }
            finally { SyncGameUi(); }
        }
    }
}

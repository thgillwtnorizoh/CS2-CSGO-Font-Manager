using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private const string SpecificUseGeneral = "Use General Selection";
        private const string SpecificValveDefault = "Valve Default";
        private const string SpecificOverrideBegin = "<!-- Font Manager specific-family overrides begin -->";
        private const string SpecificOverrideEnd = "<!-- Font Manager specific-family overrides end -->";

        private static readonly bool SpecificFontUiBootstrapRegistered = RegisterSpecificFontUiBootstrap();

        private sealed class FamilySpec
        {
            public string Family;
            public string Group;
            public string Role;

            public FamilySpec(string family, string group, string role)
            {
                Family = family;
                Group = group;
                Role = role;
            }
        }

        private sealed class ReplacementFont
        {
            public string SelectionName;
            public string FamilyName;
            public string SourcePath;
            public string ManagedPath;
        }

        private static readonly FamilySpec[] SpecificFamilyRegistry =
        {
            new FamilySpec("notosans", "General UI", "Default/general Panorama text: settings, ordinary labels and text entries."),
            new FamilySpec("Stratum2", "General UI", "Main CS2 branded UI: headings, menus, scoreboard metadata, buy-menu labels and team names."),
            new FamilySpec("ForceStratum2", "General UI", "Locale compatibility override, notably used for Vietnamese player/name layouts."),

            new FamilySpec("Stratum2 Condensed", "Compact / Display", "Dense navigation and compact labels: buy menu, ratings, scoreboard-style surfaces."),
            new FamilySpec("Stratum2 Medium Condensed", "Compact / Display", "Compact emphasized names/headings, including team-intro player names."),
            new FamilySpec("Stratum2 Bold Condensed", "Compact / Display", "Large compact display headings, including team-intro half/side headings."),
            new FamilySpec("Stratum2 Black Condensed", "Compact / Display", "Heavy condensed display text used by selected presentation surfaces."),
            new FamilySpec("Stratum2 Thin Condensed", "Compact / Display", "Rare thin condensed presentation text."),
            new FamilySpec("Stratum2 Light Condensed", "Compact / Display", "Rare light condensed presentation text."),

            new FamilySpec("Stratum2 Medium", "Display Weights", "Emphasized normal-width UI: missions, leaderboards, stats, buy menu and loadout text."),
            new FamilySpec("Stratum2 Medium Italic", "Display Weights", "Italic medium display variant used by selected styled surfaces."),
            new FamilySpec("Stratum2 Black", "Display Weights", "Heavy display text for high-impact UI."),
            new FamilySpec("Stratum2 Black Italic", "Display Weights", "Premier rating numbers and other high-impact italic numeric/display text."),
            new FamilySpec("Stratum2 Thin", "Display Weights", "Rare lightweight/decorative UI."),
            new FamilySpec("Stratum2 Thin Italic", "Display Weights", "Rare thin italic presentation variant."),
            new FamilySpec("Stratum2 Light", "Display Weights", "Secondary/de-emphasized presentation text across store, missions, HUD and inspect UI."),
            new FamilySpec("Stratum2 Light Italic", "Display Weights", "Rare light italic presentation variant."),

            new FamilySpec("Stratum2 TF", "Chat / Input / TF", "Chat input/placeholder and selected player-card/item text."),
            new FamilySpec("Stratum2 Medium TF", "Chat / Input / TF", "Medium TF presentation variant."),
            new FamilySpec("Stratum2 Bold TF", "Chat / Input / TF", "Bold TF presentation variant."),
            new FamilySpec("Stratum2 Black TF", "Chat / Input / TF", "Black TF presentation variant."),
            new FamilySpec("Stratum2 Thin TF", "Chat / Input / TF", "Thin TF presentation variant."),
            new FamilySpec("Stratum2 Light TF", "Chat / Input / TF", "Light TF presentation variant."),

            new FamilySpec("Stratum2 Mono", "Numbers / Technical", "Console, scoreboard time/data, stats, HUD progress and technical text."),
            new FamilySpec("Stratum2 Mono Light", "Numbers / Technical", "Light monospaced technical/data presentation."),
            new FamilySpec("Stratum2 Regular Monodigit", "Numbers / Technical", "Fixed-width ordinary numbers: money, timers, scoreboard/stat values."),
            new FamilySpec("Stratum2 Bold Monodigit", "Numbers / Technical", "Fixed-width prominent numbers: scores, important money/timer/stat displays."),
            new FamilySpec("notomono-regular", "Numbers / Technical", "Generic Panorama/core monospace and debugger/performance fallback."),

            new FamilySpec("notoserif", "Fallback / Core", "Generic serif family available to Panorama; rare in normal CS2 UI."),
            new FamilySpec("Arial Unicode MS", "Fallback / Core", "Multilingual fallback placed after many Valve families. Replacing this can reduce glyph coverage."),
            new FamilySpec("Arial", "Fallback / Core", "Generic Arial compatibility/fallback family used by core font configuration."),
        };

        private bool specificSettingsUiInitialized;
        private bool specificSettingsTabActive;
        private FormViews specificLastFormView = (FormViews)(-1);

        private Button generalSettingTabButton;
        private Button specificSettingTabButton;
        private Panel specificSettingsPanel;
        private FlowLayoutPanel specificFamilyFlow;
        private ComboBox specificViewCombo;
        private Label specificGeneralSelectionLabel;
        private Button specificApplyButton;
        private Button specificRestartButton;
        private ToolTip specificToolTip;

        private static bool RegisterSpecificFontUiBootstrap()
        {
            Application.Idle += BootstrapSpecificFontUiOnIdle;
            return true;
        }

        private static void BootstrapSpecificFontUiOnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.IsDisposed) continue;
                if (!form.fontScaleUiInitialized) continue;

                if (!form.specificSettingsUiInitialized)
                    form.InitializeSpecificFontSettingsUi();

                form.SyncSpecificSettingsHostState();
            }
        }

        private void InitializeSpecificFontSettingsUi()
        {
            if (specificSettingsUiInitialized) return;
            specificSettingsUiInitialized = true;

            if (Settings != null && Settings.SpecificFontAssignments == null)
                Settings.SpecificFontAssignments = new Dictionary<string, string>();

            generalSettingTabButton = CreateTopTabButton("General Setting");
            specificSettingTabButton = CreateTopTabButton("Specific Setting");
            generalSettingTabButton.Click += delegate { SetSpecificSettingsTab(false); };
            specificSettingTabButton.Click += delegate { SetSpecificSettingsTab(true); };

            specificSettingsPanel = new Panel
            {
                Name = "specificSettingsPanel",
                BackColor = BackColor,
                Visible = false
            };

            Label viewLabel = new Label
            {
                Text = "View:",
                AutoSize = true,
                ForeColor = Color.FromArgb(200, 235, 244),
                Location = new Point(0, 7)
            };

            specificViewCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                Location = new Point(42, 2),
                Width = 170
            };
            specificViewCombo.Items.Add("Group by UI role");
            specificViewCombo.Items.Add("All families");
            string savedView = Settings == null ? "Group by UI role" : Settings.SpecificFontViewMode;
            specificViewCombo.SelectedItem = specificViewCombo.Items.Contains(savedView) ? savedView : "Group by UI role";
            specificViewCombo.SelectedIndexChanged += specificViewCombo_SelectedIndexChanged;

            specificGeneralSelectionLabel = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.Gray,
                Font = new Font("Microsoft Sans Serif", 8.25f, FontStyle.Regular)
            };

            specificFamilyFlow = new FlowLayoutPanel
            {
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                BackColor = Color.FromArgb(27, 27, 29),
                Padding = new Padding(5)
            };

            specificApplyButton = new Button
            {
                Text = "Apply Specific Font Settings",
                FlatStyle = apply_button.FlatStyle,
                BackColor = apply_button.BackColor,
                ForeColor = apply_button.ForeColor,
                Font = apply_button.Font,
                UseVisualStyleBackColor = false
            };
            specificApplyButton.Click += specificApplyButton_Click;

            specificRestartButton = new Button
            {
                Text = "Restart CS2",
                FlatStyle = restartCs2Button == null ? FlatStyle.Popup : restartCs2Button.FlatStyle,
                BackColor = restartCs2Button == null ? Color.FromArgb(44, 106, 160) : restartCs2Button.BackColor,
                ForeColor = restartCs2Button == null ? Color.White : restartCs2Button.ForeColor,
                Font = restartCs2Button == null ? Font : restartCs2Button.Font,
                UseVisualStyleBackColor = false
            };
            specificRestartButton.Click += delegate
            {
                if (restartCs2Button != null) restartCs2Button.PerformClick();
            };

            specificToolTip = new ToolTip { ShowAlways = true };
            specificToolTip.SetToolTip(specificViewCombo,
                "Grouping changes only how families are displayed. It does not change the generated config.");

            specificSettingsPanel.Controls.Add(viewLabel);
            specificSettingsPanel.Controls.Add(specificViewCombo);
            specificSettingsPanel.Controls.Add(specificGeneralSelectionLabel);
            specificSettingsPanel.Controls.Add(specificFamilyFlow);
            specificSettingsPanel.Controls.Add(specificApplyButton);
            specificSettingsPanel.Controls.Add(specificRestartButton);

            Controls.Add(generalSettingTabButton);
            Controls.Add(specificSettingTabButton);
            Controls.Add(specificSettingsPanel);
            generalSettingTabButton.BringToFront();
            specificSettingTabButton.BringToFront();
            specificSettingsPanel.BringToFront();

            SizeChanged += specificSettings_SizeChanged;
            listBox1.SelectedIndexChanged += specificGeneralSelectionChanged;

            apply_button.Text = "Apply Selected to All Fonts";
            donate_button.Visible = false;
            MinimumSize = new Size(Math.Max(MinimumSize.Width, 600), Math.Max(MinimumSize.Height, 650));
            if (ClientSize.Width < 680 || ClientSize.Height < 700)
                ClientSize = new Size(Math.Max(ClientSize.Width, 680), Math.Max(ClientSize.Height, 700));

            RebuildSpecificFamilySections();
            LayoutSpecificSettingsUi();
            UpdateSpecificTabVisuals();
            AppLog.Info("Specific font settings UI initialized with " + SpecificFamilyRegistry.Length + " family entries.");
        }

        private Button CreateTopTabButton(string text)
        {
            return new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Popup,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(55, 55, 58),
                Font = new Font("Microsoft Sans Serif", 8.25f, FontStyle.Bold),
                UseVisualStyleBackColor = false,
                Height = 30
            };
        }

        private void SyncSpecificSettingsHostState()
        {
            if (!specificSettingsUiInitialized) return;

            if (specificLastFormView != CurrentFormView)
            {
                specificLastFormView = CurrentFormView;
                if (CurrentFormView != FormViews.Main)
                    specificSettingsTabActive = false;
                LayoutSpecificSettingsUi();
            }

            if (CurrentFormView == FormViews.Main)
            {
                apply_button.Text = "Apply Selected to All Fonts";
                donate_button.Visible = false;
            }
        }

        private void SetSpecificSettingsTab(bool specific)
        {
            if (CurrentFormView != FormViews.Main) return;
            specificSettingsTabActive = specific;
            if (specific)
            {
                RefreshSpecificGeneralSelectionLabel();
                RebuildSpecificFamilySections();
            }
            LayoutSpecificSettingsUi();
            UpdateSpecificTabVisuals();
            AppLog.Info("Switched to " + (specific ? "Specific Setting" : "General Setting") + " tab.");
        }

        private void UpdateSpecificTabVisuals()
        {
            if (!specificSettingsUiInitialized) return;
            Color active = apply_button.BackColor;
            Color inactive = Color.FromArgb(55, 55, 58);
            generalSettingTabButton.BackColor = specificSettingsTabActive ? inactive : active;
            specificSettingTabButton.BackColor = specificSettingsTabActive ? active : inactive;
        }

        private void specificSettings_SizeChanged(object sender, EventArgs e)
        {
            LayoutSpecificSettingsUi();
        }

        private void specificGeneralSelectionChanged(object sender, EventArgs e)
        {
            RefreshSpecificGeneralSelectionLabel();
        }

        private void specificViewCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!specificSettingsUiInitialized || specificViewCombo.SelectedItem == null) return;
            if (Settings != null) Settings.SpecificFontViewMode = specificViewCombo.SelectedItem.ToString();
            RebuildSpecificFamilySections();
        }

        private void RefreshSpecificGeneralSelectionLabel()
        {
            if (specificGeneralSelectionLabel == null) return;
            string general = listBox1.SelectedItem == null ? DefaultFontName : listBox1.SelectedItem.ToString();
            specificGeneralSelectionLabel.Text = "Use General Selection = " + general;
        }

        private void LayoutSpecificSettingsUi()
        {
            if (!specificSettingsUiInitialized || IsDisposed) return;

            bool main = CurrentFormView == FormViews.Main;
            generalSettingTabButton.Visible = main;
            specificSettingTabButton.Visible = main;
            if (!main)
            {
                specificSettingsPanel.Visible = false;
                return;
            }

            const int margin = 12;
            const int gap = 7;
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            int contentWidth = Math.Max(300, width - margin * 2);

            int tabY = 40;
            int tabWidth = Math.Max(130, (contentWidth - gap) / 2);
            generalSettingTabButton.SetBounds(margin, tabY, tabWidth, 30);
            specificSettingTabButton.SetBounds(margin + tabWidth + gap, tabY, contentWidth - tabWidth - gap, 30);

            int footerY = height - 23;
            version_label.Location = new Point(margin, footerY);
            linkLabel1.Location = new Point(width - margin - linkLabel1.Width, footerY);
            linkLabel2.Location = new Point(linkLabel1.Left - gap - linkLabel2.Width, footerY);
            linkLabel3.Location = new Point(linkLabel2.Left - gap - linkLabel3.Width, footerY);
            donate_button.Visible = false;

            if (specificSettingsTabActive)
                LayoutSpecificTab(margin, tabY + 30 + gap, contentWidth, footerY - (tabY + 30 + gap) - gap);
            else
                LayoutGeneralTabWithTabs(margin, tabY + 30 + gap, contentWidth, footerY);

            UpdateSpecificTabVisuals();
        }

        private void SetGeneralControlsVisible(bool visible)
        {
            listBox1.Visible = visible;
            search_textBox.Visible = visible;
            addFont_button.Visible = visible;
            remove_button.Visible = visible;
            trackBar1.Visible = visible;
            fontScaleValueLabel.Visible = visible;
            customFontScaleButton.Visible = visible;
            fontPreview_richTextBox.Visible = visible;
            fontPreviewInfoLabel.Visible = visible;
            apply_button.Visible = visible;
            if (restartCs2Button != null) restartCs2Button.Visible = visible;
        }

        private void LayoutGeneralTabWithTabs(int margin, int contentTop, int contentWidth, int footerY)
        {
            specificSettingsPanel.Visible = false;
            SetGeneralControlsVisible(true);

            const int gap = 7;
            int width = ClientSize.Width;

            int iconSize = 22;
            remove_button.Size = new Size(iconSize, iconSize);
            remove_button.Location = new Point(width - margin - iconSize, 8);
            addFont_button.Size = new Size(iconSize, iconSize);
            addFont_button.Location = new Point(remove_button.Left - gap - iconSize, 8);
            int searchRight = addFont_button.Left - gap;
            search_textBox.Location = new Point(Math.Max(margin + 120, searchRight - 190), 8);
            search_textBox.Size = new Size(Math.Max(100, searchRight - search_textBox.Left), 23);

            int restartHeight = 30;
            int restartY = footerY - gap - restartHeight;
            if (restartCs2Button != null)
                restartCs2Button.SetBounds(margin, restartY, contentWidth, restartHeight);

            int applyHeight = 42;
            int applyY = restartY - gap - applyHeight;
            apply_button.SetBounds(margin, applyY, contentWidth, applyHeight);
            apply_button.Text = "Apply Selected to All Fonts";

            int previewInfoHeight = 18;
            int previewInfoY = applyY - 3 - previewInfoHeight;
            fontPreviewInfoLabel.SetBounds(margin, previewInfoY, contentWidth, previewInfoHeight);

            int previewHeight = Math.Max(90, Math.Min(135, ClientSize.Height / 5));
            int previewY = previewInfoY - 3 - previewHeight;
            fontPreview_richTextBox.SetBounds(margin, previewY, contentWidth, previewHeight);

            int scaleRowHeight = 38;
            int scaleY = previewY - gap - scaleRowHeight;
            int cogWidth = 34;
            int readoutWidth = 74;
            int sliderWidth = Math.Max(100, contentWidth - cogWidth - readoutWidth - gap * 2);
            trackBar1.SetBounds(margin, scaleY, sliderWidth, scaleRowHeight);
            fontScaleValueLabel.SetBounds(trackBar1.Right + gap, scaleY + 1, readoutWidth, 29);
            customFontScaleButton.SetBounds(fontScaleValueLabel.Right + gap, scaleY + 1, cogWidth, 29);

            int listBottom = scaleY - gap;
            listBox1.SetBounds(margin, contentTop, contentWidth, Math.Max(100, listBottom - contentTop));
        }

        private void LayoutSpecificTab(int margin, int top, int contentWidth, int availableHeight)
        {
            SetGeneralControlsVisible(false);
            search_textBox.Visible = false;
            addFont_button.Visible = false;
            remove_button.Visible = false;

            specificSettingsPanel.Visible = true;
            specificSettingsPanel.SetBounds(margin, top, contentWidth, Math.Max(200, availableHeight));
            specificSettingsPanel.BringToFront();

            int panelWidth = specificSettingsPanel.ClientSize.Width;
            int panelHeight = specificSettingsPanel.ClientSize.Height;
            int viewRowHeight = 32;
            specificGeneralSelectionLabel.SetBounds(Math.Max(220, panelWidth - 360), 1, Math.Min(350, panelWidth - 220), 28);

            int applyHeight = 42;
            int restartHeight = 30;
            int buttonGap = 7;
            int restartY = panelHeight - restartHeight;
            int applyY = restartY - buttonGap - applyHeight;
            specificApplyButton.SetBounds(0, applyY, panelWidth, applyHeight);
            specificRestartButton.SetBounds(0, restartY, panelWidth, restartHeight);

            int flowY = viewRowHeight + 5;
            int flowHeight = Math.Max(100, applyY - buttonGap - flowY);
            specificFamilyFlow.SetBounds(0, flowY, panelWidth, flowHeight);
            ResizeSpecificFamilyRows();
        }

        private void ResizeSpecificFamilyRows()
        {
            if (specificFamilyFlow == null) return;
            int rowWidth = Math.Max(300, specificFamilyFlow.ClientSize.Width - 28);
            foreach (Control control in specificFamilyFlow.Controls)
            {
                control.Width = rowWidth;
            }
        }

        private void RebuildSpecificFamilySections()
        {
            if (specificFamilyFlow == null) return;

            string view = specificViewCombo == null || specificViewCombo.SelectedItem == null
                ? "Group by UI role"
                : specificViewCombo.SelectedItem.ToString();

            specificFamilyFlow.SuspendLayout();
            try
            {
                specificFamilyFlow.Controls.Clear();
                IEnumerable<FamilySpec> specs = view == "All families"
                    ? SpecificFamilyRegistry.OrderBy(x => x.Family, StringComparer.OrdinalIgnoreCase)
                    : SpecificFamilyRegistry;

                string previousGroup = null;
                foreach (FamilySpec spec in specs)
                {
                    if (view != "All families" && spec.Group != previousGroup)
                    {
                        previousGroup = spec.Group;
                        specificFamilyFlow.Controls.Add(CreateSpecificGroupHeader(previousGroup));
                    }
                    specificFamilyFlow.Controls.Add(CreateSpecificFamilyRow(spec));
                }
            }
            finally
            {
                specificFamilyFlow.ResumeLayout();
            }
            ResizeSpecificFamilyRows();
            RefreshSpecificGeneralSelectionLabel();
        }

        private Control CreateSpecificGroupHeader(string group)
        {
            Label label = new Label
            {
                Height = 28,
                Margin = new Padding(0, 8, 0, 2),
                Text = group.ToUpperInvariant(),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft Sans Serif", 8.25f, FontStyle.Bold),
                ForeColor = apply_button.BackColor,
                BackColor = Color.Transparent
            };
            return label;
        }

        private Control CreateSpecificFamilyRow(FamilySpec spec)
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
                AutoSize = false,
                Font = new Font("Microsoft Sans Serif", 9.0f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(10, 8),
                Size = new Size(230, 20)
            };

            Label role = new Label
            {
                Text = spec.Role,
                AutoSize = false,
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
            combo.DropDown += specificFamilyCombo_DropDown;
            combo.SelectedIndexChanged += specificFamilyCombo_SelectedIndexChanged;
            PopulateSpecificFamilyCombo(combo, GetSavedSpecificAssignment(spec.Family));

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

        private string GetSavedSpecificAssignment(string family)
        {
            if (Settings == null || Settings.SpecificFontAssignments == null) return SpecificUseGeneral;
            string value;
            return Settings.SpecificFontAssignments.TryGetValue(family, out value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : SpecificUseGeneral;
        }

        private void specificFamilyCombo_DropDown(object sender, EventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null) return;
            string selected = combo.SelectedItem == null ? SpecificUseGeneral : combo.SelectedItem.ToString();
            PopulateSpecificFamilyCombo(combo, selected);
        }

        private void PopulateSpecificFamilyCombo(ComboBox combo, string selected)
        {
            combo.SelectedIndexChanged -= specificFamilyCombo_SelectedIndexChanged;
            try
            {
                combo.Items.Clear();
                combo.Items.Add(SpecificUseGeneral);
                combo.Items.Add(SpecificValveDefault);
                foreach (string font in GetImportedFontNames())
                    combo.Items.Add(font);

                if (!string.IsNullOrWhiteSpace(selected) && combo.Items.Contains(selected))
                    combo.SelectedItem = selected;
                else
                    combo.SelectedItem = SpecificUseGeneral;
            }
            finally
            {
                combo.SelectedIndexChanged += specificFamilyCombo_SelectedIndexChanged;
            }
        }

        private void specificFamilyCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null || combo.Tag == null || combo.SelectedItem == null || Settings == null) return;
            if (Settings.SpecificFontAssignments == null)
                Settings.SpecificFontAssignments = new Dictionary<string, string>();
            Settings.SpecificFontAssignments[combo.Tag.ToString()] = combo.SelectedItem.ToString();
        }

        private IEnumerable<string> GetImportedFontNames()
        {
            SortedSet<string> names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(FontsFolder)) return names;

            foreach (string directory in Directory.GetDirectories(FontsFolder))
            {
                try
                {
                    string fontFile = Directory.GetFiles(directory)
                        .FirstOrDefault(path => IsFontExtension(Path.GetExtension(path)));
                    if (fontFile == null) continue;
                    string family = GetFontFamilyNameFromFile(fontFile);
                    if (string.IsNullOrWhiteSpace(family))
                        family = Path.GetFileName(directory);
                    if (!family.Equals(DefaultFontName, StringComparison.OrdinalIgnoreCase))
                        names.Add(family);
                }
                catch (Exception exception)
                {
                    AppLog.Error("Could not inspect imported font folder " + directory + ".", exception);
                }
            }
            return names;
        }

        private void specificApplyButton_Click(object sender, EventArgs e)
        {
            ApplySpecificFontSettings();
        }

        private void ApplySpecificFontSettings()
        {
            if (Settings == null || string.IsNullOrWhiteSpace(Settings.CsgoPath))
            {
                MessageBox.Show("The Counter-Strike 2 folder is unknown. Restart Font Manager and select your CS2 install folder.",
                    "No CS2 Folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            CsgoFontsFolder = Settings.CsgoPath + Cs2FontsRelativePath;
            string gameFontsConf = Path.Combine(CsgoFontsFolder, "fonts.conf");
            if (!File.Exists(gameFontsConf))
            {
                MessageBox.Show("Modern CS2 fonts.conf was not found at:\n\n" + gameFontsConf,
                    "fonts.conf Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string generalSelection = listBox1.SelectedItem == null ? DefaultFontName : listBox1.SelectedItem.ToString();
            Dictionary<string, string> effectiveAssignments = GetEffectiveSpecificAssignments(generalSelection);
            int mappedFamilies = effectiveAssignments.Count(x => x.Value != null);
            float scale = GetCurrentFontScale();

            string question = "Apply Specific Setting?\n\n" +
                              mappedFamilies + " CS2 families will use imported replacement fonts.\n" +
                              (SpecificFamilyRegistry.Length - mappedFamilies) + " families will remain on Valve/default resolution.\n" +
                              "Global size: " + scale.ToString("0.00", CultureInfo.InvariantCulture) + "x\n" +
                              "Use General Selection currently means: " + generalSelection;
            if (MessageBox.Show(question, "Apply Specific Font Settings", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                Directory.CreateDirectory(CsgoFontsFolder);
                RemoveManagedFontFiles();

                Dictionary<string, ReplacementFont> replacements = PrepareSpecificReplacementFonts(effectiveAssignments);
                string currentConfig = File.ReadAllText(gameFontsConf);
                string baseConfig = GetCurrentCs2BaseConfig(currentConfig);
                string generated = BuildSpecificFamilyConfig(baseConfig, effectiveAssignments, replacements, scale);

                if (!IsWellFormedXml(generated))
                    throw new InvalidDataException("The generated specific-family fonts.conf is not valid XML.");

                string tempConfig = gameFontsConf + ".fontmanager.tmp";
                File.WriteAllText(tempConfig, generated, new UTF8Encoding(false));
                File.Copy(tempConfig, gameFontsConf, true);
                File.Delete(tempConfig);
                File.WriteAllText(Cs2GeneratedConfigPath, generated, new UTF8Encoding(false));
                Settings.ActiveFont = "Specific Setting";

                AppLog.Info("Applied specific font settings. Families mapped=" + mappedFamilies +
                            ", unique replacement fonts=" + replacements.Count +
                            ", scale=" + scale.ToString("0.00", CultureInfo.InvariantCulture) + "x.");

                MessageBox.Show("Specific font settings applied successfully." +
                                (IsCs2Running() ? "\n\nCS2 is running. Use Restart CS2 to load the change." : ""),
                    "Specific Settings Applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to apply specific font settings.", exception);
                MessageBox.Show("Failed to apply specific font settings.\n\n" + exception.Message,
                    "Apply Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Dictionary<string, string> GetEffectiveSpecificAssignments(string generalSelection)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (FamilySpec spec in SpecificFamilyRegistry)
            {
                string saved = GetSavedSpecificAssignment(spec.Family);
                string selected;
                if (saved == SpecificValveDefault)
                    selected = null;
                else if (saved == SpecificUseGeneral)
                    selected = generalSelection == DefaultFontName ? null : generalSelection;
                else
                    selected = saved;
                result[spec.Family] = selected;
            }
            return result;
        }

        private Dictionary<string, ReplacementFont> PrepareSpecificReplacementFonts(Dictionary<string, string> assignments)
        {
            Dictionary<string, ReplacementFont> result = new Dictionary<string, ReplacementFont>(StringComparer.OrdinalIgnoreCase);
            int index = 0;

            foreach (string selection in assignments.Values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string sourcePath;
                string actualFamily;
                if (!TryFindImportedFont(selection, out sourcePath, out actualFamily))
                    throw new FileNotFoundException("The imported font '" + selection + "' could not be found. Import it again in General Setting.");

                string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                string managedName = ManagedFontBaseName + "." + index.ToString("00", CultureInfo.InvariantCulture) + extension;
                string managedPath = Path.Combine(CsgoFontsFolder, managedName);
                File.Copy(sourcePath, managedPath, true);

                result[selection] = new ReplacementFont
                {
                    SelectionName = selection,
                    FamilyName = actualFamily,
                    SourcePath = sourcePath,
                    ManagedPath = managedPath
                };
                index++;
            }
            return result;
        }

        private bool TryFindImportedFont(string selection, out string fontPath, out string familyName)
        {
            fontPath = null;
            familyName = null;
            if (!Directory.Exists(FontsFolder)) return false;

            foreach (string directory in Directory.GetDirectories(FontsFolder))
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(directory).Where(path => IsFontExtension(Path.GetExtension(path))).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (string file in files)
                {
                    string actual = GetFontFamilyNameFromFile(file);
                    if ((!string.IsNullOrWhiteSpace(actual) && actual.Equals(selection, StringComparison.OrdinalIgnoreCase)) ||
                        Path.GetFileName(directory).Equals(selection, StringComparison.OrdinalIgnoreCase))
                    {
                        fontPath = file;
                        familyName = string.IsNullOrWhiteSpace(actual) ? selection : actual;
                        return true;
                    }
                }
            }
            return false;
        }

        private static string BuildSpecificFamilyConfig(
            string baseConfig,
            Dictionary<string, string> assignments,
            Dictionary<string, ReplacementFont> replacements,
            float scale)
        {
            string config = StripSpecificFamilyOverrides(baseConfig);
            config = StripDefaultSizeOnlyOverride(config);
            config = StripManagedOverride(config);

            config = Regex.Replace(config,
                "<dir\\s+prefix=\"default\">\\.\\./\\.\\./csgo/panorama/fonts</dir>",
                "<dir prefix=\"cwd\">../../csgo/panorama/fonts</dir>");

            StringBuilder block = new StringBuilder();
            block.Append("\t").Append(SpecificOverrideBegin).Append("\n");
            foreach (FamilySpec spec in SpecificFamilyRegistry)
            {
                string selection;
                if (!assignments.TryGetValue(spec.Family, out selection) || string.IsNullOrWhiteSpace(selection))
                    continue;

                ReplacementFont replacement;
                if (!replacements.TryGetValue(selection, out replacement))
                    throw new InvalidDataException("No copied replacement font exists for " + selection + ".");

                block.Append("\t<match target=\"pattern\">\n")
                    .Append("\t\t<test name=\"family\" compare=\"eq\" qual=\"any\">\n")
                    .Append("\t\t\t<string>").Append(SecurityElement.Escape(spec.Family)).Append("</string>\n")
                    .Append("\t\t</test>\n")
                    .Append("\t\t<edit name=\"family\" mode=\"assign\" binding=\"strong\">\n")
                    .Append("\t\t\t<string>").Append(SecurityElement.Escape(replacement.FamilyName)).Append("</string>\n")
                    .Append("\t\t</edit>\n")
                    .Append("\t</match>\n\n");
            }
            block.Append("\t").Append(SpecificOverrideEnd).Append("\n\n");

            int includeIndex = config.IndexOf(Cs2CoreInclude, StringComparison.Ordinal);
            if (includeIndex < 0)
                throw new InvalidDataException("The modern CS2 core font include could not be found.");
            int lineStart = config.LastIndexOf('\n', includeIndex);
            int insertIndex = lineStart >= 0 ? lineStart + 1 : includeIndex;
            config = config.Insert(insertIndex, block.ToString());

            if (Math.Abs(scale - 1.0f) >= 0.0001f)
                config = BuildDefaultSizeOnlyConfig(config, scale);

            return config;
        }

        private static string StripSpecificFamilyOverrides(string config)
        {
            string pattern =
                "[ \\t]*" + Regex.Escape(SpecificOverrideBegin) + ".*?" +
                Regex.Escape(SpecificOverrideEnd) + "\\s*";
            return Regex.Replace(config, pattern, string.Empty, RegexOptions.Singleline);
        }
    }
}

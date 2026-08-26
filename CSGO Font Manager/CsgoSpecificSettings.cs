using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    /// <summary>
    /// Owns the legacy CS:GO Specific Settings registry, cached rows, view switching, and apply path.
    /// Shared saved-state synchronization remains in SpecificSettingsState.cs.
    /// </summary>
    public partial class Form1
    {
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
    }
}

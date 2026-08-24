using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private static readonly bool SpecificSettingsStateHotfixBootstrapRegistered = RegisterSpecificSettingsStateHotfixBootstrap();

        private bool specificSettingsStateHotfixInstalled;
        private string specificSettingsStateHotfixLastGame;

        private static bool RegisterSpecificSettingsStateHotfixBootstrap()
        {
            Application.Idle += SpecificSettingsStateHotfixOnIdle;
            return true;
        }

        private static void SpecificSettingsStateHotfixOnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.IsDisposed)
                    continue;
                if (!form.dualReady || !form.pigUiV2Initialized ||
                    form.generalSettingTabButton == null || form.specificSettingTabButton == null ||
                    form.specificApplyButton == null || form.specificSettingsPanel == null)
                    continue;

                if (!form.specificSettingsStateHotfixInstalled)
                    form.InstallSpecificSettingsStateHotfix();

                form.MaintainSpecificSettingsStateHotfix();
            }
        }

        private void InstallSpecificSettingsStateHotfix()
        {
            if (specificSettingsStateHotfixInstalled) return;
            specificSettingsStateHotfixInstalled = true;

            generalSettingTabButton.Click += specificSettingsStateHotfix_TabClicked;
            specificSettingTabButton.Click += specificSettingsStateHotfix_TabClicked;
            title_label.Click += specificSettingsStateHotfix_GameClicked;

            ReplaceSpecificApplyButtonWithStateSafeVersion();
            SyncActiveSpecificControlsFromSettings();
            specificSettingsStateHotfixLastGame = GameName();
            ApplySpecificTabContrast();

            AppLog.Info("Specific settings state hotfix installed: cached rows rehydrate from saved state, apply captures live controls, and active-tab contrast is enforced.");
        }

        private void MaintainSpecificSettingsStateHotfix()
        {
            string game = GameName();
            if (!string.Equals(game, specificSettingsStateHotfixLastGame, StringComparison.Ordinal))
            {
                specificSettingsStateHotfixLastGame = game;
                SyncActiveSpecificControlsFromSettings();
            }

            ApplySpecificTabContrast();
        }

        private void specificSettingsStateHotfix_TabClicked(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate
            {
                if (specificSettingsTabActive)
                    SyncActiveSpecificControlsFromSettings();
                ApplySpecificTabContrast();
            });
        }

        private void specificSettingsStateHotfix_GameClicked(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate
            {
                specificSettingsStateHotfixLastGame = GameName();
                SyncActiveSpecificControlsFromSettings();
                ApplySpecificTabContrast();
            });
        }

        private void ReplaceSpecificApplyButtonWithStateSafeVersion()
        {
            Button oldButton = specificApplyButton;
            Button replacement = new Button
            {
                Name = oldButton.Name,
                Text = oldButton.Text,
                Bounds = oldButton.Bounds,
                Anchor = oldButton.Anchor,
                Dock = oldButton.Dock,
                FlatStyle = oldButton.FlatStyle,
                BackColor = oldButton.BackColor,
                ForeColor = oldButton.ForeColor,
                Font = oldButton.Font,
                UseVisualStyleBackColor = false,
                Enabled = oldButton.Enabled,
                Visible = oldButton.Visible,
                TabIndex = oldButton.TabIndex,
                TabStop = oldButton.TabStop
            };

            replacement.Click += specificSettingsStateHotfix_ApplyClicked;

            specificSettingsPanel.Controls.Remove(oldButton);
            oldButton.Dispose();
            specificApplyButton = replacement;
            specificSettingsPanel.Controls.Add(specificApplyButton);
            specificApplyButton.BringToFront();
        }

        private void specificSettingsStateHotfix_ApplyClicked(object sender, EventArgs e)
        {
            CaptureActiveSpecificControlsToSettings();
            SaveNow();

            if (gameTarget == GameTarget.CS2)
                ApplySpecificFontSettings();
            else
                ApplyCsgoSpecific();
        }

        private void SyncActiveSpecificControlsFromSettings()
        {
            if (Settings == null) return;

            if (gameTarget == GameTarget.CS2)
            {
                FlowLayoutPanel flow = cs2SpecificFlow ?? specificFamilyFlow;
                if (flow == null) return;

                if (Settings.SpecificFontAssignments == null)
                    Settings.SpecificFontAssignments = new Dictionary<string, string>();

                foreach (Control row in flow.Controls)
                {
                    FamilySpec spec = row.Tag as FamilySpec;
                    if (spec == null) continue;

                    ComboBox combo = FindSpecificCombo(row, spec.Family);
                    if (combo == null) continue;

                    PopulateSpecificFamilyCombo(combo, GetSavedSpecificAssignment(spec.Family));
                }
            }
            else
            {
                if (csgoSpecificFlow == null)
                    MakeCsgoFlow();
                if (csgoSpecificFlow == null) return;

                if (Settings.CsgoSpecificFontAssignments == null)
                    Settings.CsgoSpecificFontAssignments = new Dictionary<string, string>();

                foreach (Control row in csgoSpecificFlow.Controls)
                {
                    FamilySpec spec = row.Tag as FamilySpec;
                    if (spec == null) continue;

                    ComboBox combo = FindSpecificCombo(row, spec.Family);
                    if (combo == null) continue;

                    // FillCsgoCombo normally preserves the current selection first. Clear it so
                    // the saved dictionary, rather than a stale cached control, is authoritative.
                    combo.SelectedItem = null;
                    FillCsgoCombo(combo);
                }
            }

            if (specificSettingsTabActive)
            {
                SwitchSpecificFlow();
                EnsureAllSpecificRowsVisible();
                ForceSpecificFlowLayout();
            }

            AppLog.Info("Specific setting controls rehydrated from " + GameName() + " saved assignments.");
        }

        private void CaptureActiveSpecificControlsToSettings()
        {
            if (Settings == null) return;

            FlowLayoutPanel flow;
            Dictionary<string, string> assignments;

            if (gameTarget == GameTarget.CS2)
            {
                flow = cs2SpecificFlow ?? specificFamilyFlow;
                if (Settings.SpecificFontAssignments == null)
                    Settings.SpecificFontAssignments = new Dictionary<string, string>();
                assignments = Settings.SpecificFontAssignments;
            }
            else
            {
                if (csgoSpecificFlow == null)
                    MakeCsgoFlow();
                flow = csgoSpecificFlow;
                if (Settings.CsgoSpecificFontAssignments == null)
                    Settings.CsgoSpecificFontAssignments = new Dictionary<string, string>();
                assignments = Settings.CsgoSpecificFontAssignments;
            }

            if (flow == null) return;

            int captured = 0;
            foreach (Control row in flow.Controls)
            {
                FamilySpec spec = row.Tag as FamilySpec;
                if (spec == null) continue;

                ComboBox combo = FindSpecificCombo(row, spec.Family);
                if (combo == null || combo.SelectedItem == null) continue;

                assignments[spec.Family] = combo.SelectedItem.ToString();
                captured++;
            }

            AppLog.Info("Captured " + captured + " live " + GameName() + " specific assignments before apply.");
        }

        private static ComboBox FindSpecificCombo(Control row, string family)
        {
            if (row == null) return null;
            foreach (Control child in row.Controls)
            {
                ComboBox combo = child as ComboBox;
                if (combo == null) continue;
                if (combo.Tag != null && string.Equals(combo.Tag.ToString(), family, StringComparison.OrdinalIgnoreCase))
                    return combo;
            }
            return null;
        }

        private void ApplySpecificTabContrast()
        {
            if (generalSettingTabButton == null || specificSettingTabButton == null) return;

            generalSettingTabButton.ForeColor = GetReadableTextColor(generalSettingTabButton.BackColor);
            specificSettingTabButton.ForeColor = GetReadableTextColor(specificSettingTabButton.BackColor);
        }

        private static Color GetReadableTextColor(Color background)
        {
            // ITU-R BT.601 luma is sufficient for choosing black/white UI text.
            int luma = (background.R * 299 + background.G * 587 + background.B * 114) / 1000;
            return luma >= 145 ? Color.Black : Color.White;
        }
    }
}

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

            // Keep the original button instance. Replacing it while the Specific panel is hidden
            // copies Visible=false and can leave the Apply button permanently invisible.
            specificApplyButton.Visible = true;

            FastSyncActiveSpecificControlsFromSettings();
            specificSettingsStateHotfixLastGame = GameName();
            ApplySpecificTabContrast();

            AppLog.Info("Specific settings state hotfix v2 installed: cached rows use fast selection sync, apply button preserved, and active-tab contrast enforced.");
        }

        private void MaintainSpecificSettingsStateHotfix()
        {
            string game = GameName();
            if (!string.Equals(game, specificSettingsStateHotfixLastGame, StringComparison.Ordinal))
            {
                specificSettingsStateHotfixLastGame = game;
                FastSyncActiveSpecificControlsFromSettings();
            }

            // LayoutSpecificTab positions this control but does not set Visible=true.
            // Keep the child visible; its parent panel decides whether Specific Setting is shown.
            if (specificApplyButton != null && !specificApplyButton.IsDisposed)
                specificApplyButton.Visible = true;

            ApplySpecificTabContrast();
        }

        private void specificSettingsStateHotfix_TabClicked(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate
            {
                if (specificSettingsTabActive)
                    FastSyncActiveSpecificControlsFromSettings();
                ApplySpecificTabContrast();
            });
        }

        private void specificSettingsStateHotfix_GameClicked(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate
            {
                specificSettingsStateHotfixLastGame = GameName();
                FastSyncActiveSpecificControlsFromSettings();
                ApplySpecificTabContrast();
            });
        }

        private void FastSyncActiveSpecificControlsFromSettings()
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

            int changed = 0;
            foreach (Control row in flow.Controls)
            {
                FamilySpec spec = row.Tag as FamilySpec;
                if (spec == null) continue;

                ComboBox combo = FindSpecificCombo(row, spec.Family);
                if (combo == null) continue;

                string desired;
                if (!assignments.TryGetValue(spec.Family, out desired) || string.IsNullOrWhiteSpace(desired))
                    desired = SpecificUseGeneral;

                string current = combo.SelectedItem == null ? null : combo.SelectedItem.ToString();
                if (string.Equals(current, desired, StringComparison.Ordinal))
                    continue;

                // Cached controls already contain their dropdown items from creation time.
                // Do not call PopulateSpecificFamilyCombo/FillCsgoCombo here: those rescan every
                // imported font on disk and make a cached render slower than rebuilding it.
                if (combo.Items.Contains(desired))
                {
                    combo.SelectedItem = desired;
                    changed++;
                }
            }

            if (specificSettingsTabActive)
            {
                SwitchSpecificFlow();
                EnsureAllSpecificRowsVisible();
                ForceSpecificFlowLayout();
            }

            AppLog.Info("Fast-synced cached " + GameName() + " specific controls from saved assignments; changed=" + changed + ".");
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
            int luma = (background.R * 299 + background.G * 587 + background.B * 114) / 1000;
            return luma >= 145 ? Color.Black : Color.White;
        }
    }
}

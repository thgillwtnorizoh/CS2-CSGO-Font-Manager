using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private bool specificSettingsStateInitialized;

        private void InitializeSpecificSettingsState()
        {
            if (specificSettingsStateInitialized)
                return;
            specificSettingsStateInitialized = true;

            // Keep the designer/runtime-created button. The old hotfix replaced it while its parent
            // was hidden, which copied Visible=false and produced the disappearing Apply button bug.
            specificApplyButton.Visible = true;

            generalSettingTabButton.Click += SpecificSettingsTab_Click;
            specificSettingTabButton.Click += SpecificSettingsTab_Click;

            SyncActiveSpecificControlsFromSettingsFast();
            ApplySpecificTabContrast();

            AppLog.Info("Specific settings state initialized: saved assignments are authoritative and cached controls are view-only.");
        }

        private void SpecificSettingsTab_Click(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            BeginInvoke((MethodInvoker)delegate
            {
                if (specificSettingsTabActive)
                    SyncActiveSpecificControlsFromSettingsFast();
                specificApplyButton.Visible = true;
                ApplySpecificTabContrast();
            });
        }

        private void SyncActiveSpecificControlsFromSettingsFast()
        {
            if (Settings == null)
                return;

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

            if (flow == null)
                return;

            int changed = 0;
            foreach (Control row in flow.Controls)
            {
                FamilySpec spec = row.Tag as FamilySpec;
                if (spec == null)
                    continue;

                ComboBox combo = FindSpecificCombo(row, spec.Family);
                if (combo == null)
                    continue;

                string desired;
                if (!assignments.TryGetValue(spec.Family, out desired) || string.IsNullOrWhiteSpace(desired))
                    desired = SpecificUseGeneral;

                string current = combo.SelectedItem == null ? null : combo.SelectedItem.ToString();
                if (string.Equals(current, desired, StringComparison.Ordinal))
                    continue;

                // Cached controls already own a populated item list. Changing only SelectedItem keeps
                // the cache cheap; rebuilding the combo here would rescan the font library for every row.
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
            if (Settings == null)
                return;

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

            if (flow == null)
                return;

            int captured = 0;
            foreach (Control row in flow.Controls)
            {
                FamilySpec spec = row.Tag as FamilySpec;
                if (spec == null)
                    continue;

                ComboBox combo = FindSpecificCombo(row, spec.Family);
                if (combo == null || combo.SelectedItem == null)
                    continue;

                assignments[spec.Family] = combo.SelectedItem.ToString();
                captured++;
            }

            AppLog.Info("Captured " + captured + " live " + GameName() + " specific assignments before apply.");
        }

        private static ComboBox FindSpecificCombo(Control row, string family)
        {
            if (row == null)
                return null;

            foreach (Control child in row.Controls)
            {
                ComboBox combo = child as ComboBox;
                if (combo == null)
                    continue;
                if (combo.Tag != null && string.Equals(combo.Tag.ToString(), family, StringComparison.OrdinalIgnoreCase))
                    return combo;
            }
            return null;
        }

        private void ApplySpecificTabContrast()
        {
            if (generalSettingTabButton == null || specificSettingTabButton == null)
                return;

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

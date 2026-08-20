using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private static readonly bool SpecificPreviewBridgeRegistered = RegisterSpecificPreviewBridge();

        private bool specificPreviewBridgeLastState;
        private bool pigUiFixInitialized;
        private bool pigUiFinalLayoutQueued;
        private bool pigSearchBusy;

        private Button systemFontCancelButton;
        private TextBox specificSearchTextBox;
        private Timer specificSearchTimer;
        private List<string> pigSystemFontNames;

        private readonly Dictionary<string, Control> pigSpecificRows =
            new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Control> pigSpecificHeaders =
            new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

        private static bool RegisterSpecificPreviewBridge()
        {
            Application.Idle += SpecificPreviewBridgeOnIdle;
            return true;
        }

        private static void SpecificPreviewBridgeOnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.IsDisposed || !form.specificSettingsUiInitialized) continue;

                if (!form.pigUiFixInitialized && form.fontScaleUiInitialized &&
                    form.defaultPreviewPolishInitialized && form.restartCs2Button != null)
                {
                    form.InitializePigUiFixes();
                }

                if (!form.pigUiFixInitialized) continue;
                form.SyncSpecificPreviewBridge();
            }
        }

        private void InitializePigUiFixes()
        {
            if (pigUiFixInitialized) return;
            pigUiFixInitialized = true;

            // The designer still has a 400x500 MaximumSize from the original tiny app.
            // The modern UI has a 600x650 minimum, so leaving both constraints active can
            // produce a badly stretched first frame until Windows sends a real resize.
            MaximumSize = Size.Empty;
            MinimumSize = new Size(Math.Max(MinimumSize.Width, 600), Math.Max(MinimumSize.Height, 650));

            search_textBox.TextChanged -= search_textBox_TextChanged;
            search_textBox.TextChanged += pigSearchTextBox_TextChanged;

            CreateSystemFontCancelButton();
            CreateSpecificSearchBox();
            CacheSpecificFamilyControls();
            ReplaceSpecificTabButtonWithCachedVersion();

            specificViewCombo.SelectedIndexChanged -= specificViewCombo_SelectedIndexChanged;
            specificViewCombo.SelectedIndexChanged += pigSpecificViewCombo_SelectedIndexChanged;

            addFont_button.Click += pigViewTransitionLater;
            apply_button.Click += pigViewTransitionLater;
            generalSettingTabButton.Click += pigGeneralTabClickedLater;
            SizeChanged += pigUi_SizeChanged;

            if (cs2ProcessTimer != null)
                cs2ProcessTimer.Tick += pigRestartState_Tick;

            TryEnableDoubleBuffering(specificFamilyFlow);
            RefreshSpecificRestartButtonState();
            QueuePigFinalLayout();

            AppLog.Info("Pig UI fixes initialized: cached specific rows, dual search, system-font cancel and startup layout stabilization.");
        }

        private static void TryEnableDoubleBuffering(Control control)
        {
            if (control == null) return;
            try
            {
                PropertyInfo property = typeof(Control).GetProperty(
                    "DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
                if (property != null) property.SetValue(control, true, null);
            }
            catch
            {
                // Rendering still works without this optimization.
            }
        }

        private void CreateSystemFontCancelButton()
        {
            if (systemFontCancelButton != null) return;

            systemFontCancelButton = new Button
            {
                Name = "systemFontCancelButton",
                Text = "Cancel",
                FlatStyle = FlatStyle.Popup,
                BackColor = Color.FromArgb(196, 104, 92),
                ForeColor = SystemColors.ControlText,
                Font = apply_button.Font,
                UseVisualStyleBackColor = false,
                Visible = false
            };
            systemFontCancelButton.Click += systemFontCancelButton_Click;
            Controls.Add(systemFontCancelButton);
            systemFontCancelButton.BringToFront();
        }

        private void systemFontCancelButton_Click(object sender, EventArgs e)
        {
            switchView(FormViews.Main);
            pigSearchBusy = true;
            try
            {
                search_textBox.Text = string.Empty;
            }
            finally
            {
                pigSearchBusy = false;
            }
            PopulateGeneralFontSearch(string.Empty);
            QueuePigFinalLayout();
            AppLog.Info("System font chooser cancelled; returned to General Setting.");
        }

        private void CreateSpecificSearchBox()
        {
            if (specificSearchTextBox != null) return;

            specificSearchTextBox = new TextBox
            {
                Name = "specificSearchTextBox",
                BackColor = search_textBox.BackColor,
                BorderStyle = search_textBox.BorderStyle,
                Font = search_textBox.Font,
                ForeColor = search_textBox.ForeColor,
                Visible = false
            };
            specificSearchTextBox.TextChanged += specificSearchTextBox_TextChanged;
            specificSettingsPanel.Controls.Add(specificSearchTextBox);
            specificSearchTextBox.BringToFront();

            specificSearchTimer = new Timer { Interval = 120 };
            specificSearchTimer.Tick += specificSearchTimer_Tick;

            if (specificToolTip != null)
            {
                specificToolTip.SetToolTip(specificSearchTextBox,
                    "Search family names and predicted CS2 usage. Try: console, mission, scoreboard, chat, rating.");
            }
        }

        private void specificSearchTextBox_TextChanged(object sender, EventArgs e)
        {
            if (specificSearchTimer == null) return;
            specificSearchTimer.Stop();
            specificSearchTimer.Start();
        }

        private void specificSearchTimer_Tick(object sender, EventArgs e)
        {
            specificSearchTimer.Stop();
            ApplySpecificSearchFilter();
        }

        private void CacheSpecificFamilyControls()
        {
            pigSpecificRows.Clear();
            pigSpecificHeaders.Clear();
            if (specificFamilyFlow == null) return;

            foreach (Control control in specificFamilyFlow.Controls)
            {
                FamilySpec spec = control.Tag as FamilySpec;
                if (spec != null)
                {
                    pigSpecificRows[spec.Family] = control;
                    continue;
                }

                Label header = control as Label;
                if (header != null && !string.IsNullOrWhiteSpace(header.Text))
                    pigSpecificHeaders[header.Text] = header;
            }
        }

        private void ReplaceSpecificTabButtonWithCachedVersion()
        {
            if (specificSettingTabButton == null) return;

            Button oldButton = specificSettingTabButton;
            Rectangle bounds = oldButton.Bounds;
            int tabIndex = oldButton.TabIndex;

            Button replacement = CreateTopTabButton("Specific Setting");
            replacement.Name = "specificSettingTabButton";
            replacement.Bounds = bounds;
            replacement.TabIndex = tabIndex;
            replacement.Click += pigSpecificSettingTabButton_Click;

            Controls.Add(replacement);
            replacement.BringToFront();
            specificSettingTabButton = replacement;
            oldButton.Dispose();
            UpdateSpecificTabVisuals();
        }

        private void pigSpecificSettingTabButton_Click(object sender, EventArgs e)
        {
            if (CurrentFormView != FormViews.Main) return;

            specificSettingsTabActive = true;
            if (pigSpecificRows.Count == 0)
            {
                // Fallback only. Normal startup already created the controls once.
                RebuildSpecificFamilySections();
                CacheSpecificFamilyControls();
            }

            RefreshSpecificGeneralSelectionLabel();
            LayoutSpecificSettingsUi();
            LayoutPigSpecificTopRow();
            ApplySpecificSearchFilter();
            RefreshSpecificRestartButtonState();
            UpdateSpecificTabVisuals();
            SyncSpecificPreviewBridge();
            AppLog.Info("Switched to Specific Setting tab (cached render path).");
        }

        private void pigGeneralTabClickedLater(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate
            {
                PigSyncViewState();
                SyncSpecificPreviewBridge();
            });
        }

        private void pigSpecificViewCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!pigUiFixInitialized || specificViewCombo.SelectedItem == null) return;
            if (Settings != null)
                Settings.SpecificFontViewMode = specificViewCombo.SelectedItem.ToString();

            ReorderCachedSpecificRows();
            ApplySpecificSearchFilter();
        }

        private void ReorderCachedSpecificRows()
        {
            if (specificFamilyFlow == null || pigSpecificRows.Count == 0) return;

            bool allFamilies = specificViewCombo != null && specificViewCombo.SelectedItem != null &&
                               specificViewCombo.SelectedItem.ToString() == "All families";

            specificFamilyFlow.SuspendLayout();
            try
            {
                specificFamilyFlow.Controls.Clear();
                if (allFamilies)
                {
                    foreach (FamilySpec spec in SpecificFamilyRegistry.OrderBy(
                        x => x.Family, StringComparer.OrdinalIgnoreCase))
                    {
                        Control row;
                        if (pigSpecificRows.TryGetValue(spec.Family, out row))
                            specificFamilyFlow.Controls.Add(row);
                    }
                }
                else
                {
                    string previousGroup = null;
                    foreach (FamilySpec spec in SpecificFamilyRegistry)
                    {
                        if (!string.Equals(previousGroup, spec.Group, StringComparison.Ordinal))
                        {
                            previousGroup = spec.Group;
                            Control header;
                            if (pigSpecificHeaders.TryGetValue(spec.Group.ToUpperInvariant(), out header))
                                specificFamilyFlow.Controls.Add(header);
                        }

                        Control row;
                        if (pigSpecificRows.TryGetValue(spec.Family, out row))
                            specificFamilyFlow.Controls.Add(row);
                    }
                }
            }
            finally
            {
                specificFamilyFlow.ResumeLayout(false);
            }

            ResizeSpecificFamilyRows();
            specificFamilyFlow.AutoScrollPosition = new Point(0, 0);
        }

        private void ApplySpecificSearchFilter()
        {
            if (specificFamilyFlow == null || pigSpecificRows.Count == 0) return;

            string query = specificSearchTextBox == null
                ? string.Empty
                : (specificSearchTextBox.Text ?? string.Empty).Trim();

            bool allFamilies = specificViewCombo != null && specificViewCombo.SelectedItem != null &&
                               specificViewCombo.SelectedItem.ToString() == "All families";

            specificFamilyFlow.SuspendLayout();
            try
            {
                foreach (FamilySpec spec in SpecificFamilyRegistry)
                {
                    Control row;
                    if (!pigSpecificRows.TryGetValue(spec.Family, out row)) continue;
                    row.Visible = SpecificSpecMatchesQuery(spec, query);
                }

                if (!allFamilies)
                {
                    foreach (KeyValuePair<string, Control> pair in pigSpecificHeaders)
                    {
                        string group = pair.Key;
                        bool anyVisible = SpecificFamilyRegistry.Any(spec =>
                            spec.Group.ToUpperInvariant() == group &&
                            SpecificSpecMatchesQuery(spec, query));
                        pair.Value.Visible = anyVisible;
                    }
                }
            }
            finally
            {
                specificFamilyFlow.ResumeLayout(false);
            }
        }

        private static bool SpecificSpecMatchesQuery(FamilySpec spec, string query)
        {
            if (spec == null || string.IsNullOrWhiteSpace(query)) return true;
            return ContainsIgnoreCase(spec.Family, query) ||
                   ContainsIgnoreCase(spec.Group, query) ||
                   ContainsIgnoreCase(spec.Role, query);
        }

        private static bool ContainsIgnoreCase(string value, string query)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void pigSearchTextBox_TextChanged(object sender, EventArgs e)
        {
            if (pigSearchBusy || !pigUiFixInitialized) return;

            if (CurrentFormView == FormViews.AddSystemFont)
                PopulateSystemFontSearch(search_textBox.Text);
            else if (CurrentFormView == FormViews.Main && !specificSettingsTabActive)
                PopulateGeneralFontSearch(search_textBox.Text);
        }

        private void PopulateGeneralFontSearch(string query)
        {
            if (CurrentFormView != FormViews.Main) return;

            string previousSelection = listBox1.SelectedItem == null
                ? null
                : listBox1.SelectedItem.ToString();
            string filter = (query ?? string.Empty).Trim();

            List<string> names = new List<string> { DefaultFontName };
            if (Directory.Exists(FontsFolder))
            {
                foreach (string directory in Directory.GetDirectories(FontsFolder))
                {
                    string name = Path.GetFileName(directory);
                    if (File.Exists(Path.Combine(directory, "fonts.conf")))
                        names.Add(name);
                }
            }

            names = names
                .Where(name => string.IsNullOrEmpty(filter) || ContainsIgnoreCase(name, filter))
                .OrderBy(name => name == DefaultFontName ? string.Empty : name,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

            ReplaceFontListItems(names, previousSelection);
        }

        private void EnsureSystemFontSearchCache()
        {
            if (pigSystemFontNames != null) return;
            InstalledFontCollection collection = new InstalledFontCollection();
            pigSystemFontNames = collection.Families
                .Select(family => family.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void PopulateSystemFontSearch(string query)
        {
            if (CurrentFormView != FormViews.AddSystemFont) return;
            EnsureSystemFontSearchCache();

            string previousSelection = listBox1.SelectedItem == null
                ? null
                : listBox1.SelectedItem.ToString();
            string filter = (query ?? string.Empty).Trim();

            List<string> names = pigSystemFontNames
                .Where(name => string.IsNullOrEmpty(filter) || ContainsIgnoreCase(name, filter))
                .ToList();
            ReplaceFontListItems(names, previousSelection);
        }

        private void ReplaceFontListItems(IEnumerable<string> names, string preferredSelection)
        {
            List<string> materialized = names == null ? new List<string>() : names.ToList();

            pigSearchBusy = true;
            listBox1.BeginUpdate();
            try
            {
                listBox1.Items.Clear();
                foreach (string name in materialized)
                    listBox1.Items.Add(name);

                if (!string.IsNullOrWhiteSpace(preferredSelection) &&
                    listBox1.Items.Contains(preferredSelection))
                {
                    listBox1.SelectedItem = preferredSelection;
                }
                else if (CurrentFormView == FormViews.Main && Settings != null &&
                         !string.IsNullOrWhiteSpace(Settings.ActiveFont) &&
                         listBox1.Items.Contains(Settings.ActiveFont))
                {
                    listBox1.SelectedItem = Settings.ActiveFont;
                }
                else if (listBox1.Items.Count > 0)
                {
                    listBox1.SelectedIndex = 0;
                }
            }
            finally
            {
                listBox1.EndUpdate();
                pigSearchBusy = false;
            }
        }

        private void pigViewTransitionLater(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)PigSyncViewState);
        }

        private void pigUi_SizeChanged(object sender, EventArgs e)
        {
            if (!pigUiFixInitialized) return;
            PigSyncViewState();
        }

        private void PigSyncViewState()
        {
            if (!pigUiFixInitialized || IsDisposed) return;

            if (CurrentFormView == FormViews.AddSystemFont)
            {
                specificSettingsTabActive = false;
                systemFontCancelButton.Visible = true;
                specificSearchTextBox.Visible = false;
                search_textBox.Visible = true;
                donate_button.Visible = false;
                LayoutSystemFontChooser();
            }
            else
            {
                systemFontCancelButton.Visible = false;
                donate_button.Visible = false;
                LayoutSpecificSettingsUi();

                if (specificSettingsTabActive)
                {
                    search_textBox.Visible = false;
                    specificSearchTextBox.Visible = true;
                    LayoutPigSpecificTopRow();
                    ApplySpecificSearchFilter();
                }
                else
                {
                    search_textBox.Visible = true;
                    specificSearchTextBox.Visible = false;
                }
            }

            RefreshSpecificRestartButtonState();
            SyncSpecificPreviewBridge();
        }

        private void LayoutSystemFontChooser()
        {
            const int margin = 12;
            const int gap = 7;
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            int contentWidth = Math.Max(250, width - margin * 2);

            if (generalSettingTabButton != null) generalSettingTabButton.Visible = false;
            if (specificSettingTabButton != null) specificSettingTabButton.Visible = false;
            if (specificSettingsPanel != null) specificSettingsPanel.Visible = false;
            if (defaultPreviewScrollPanel != null) defaultPreviewScrollPanel.Visible = false;

            addFont_button.Visible = false;
            remove_button.Visible = false;
            trackBar1.Visible = false;
            fontScaleValueLabel.Visible = false;
            customFontScaleButton.Visible = false;
            if (restartCs2Button != null) restartCs2Button.Visible = false;

            title_label.Location = new Point(margin, 8);
            int searchWidth = Math.Max(160, Math.Min(300, contentWidth / 2));
            search_textBox.SetBounds(width - margin - searchWidth, 8, searchWidth, 23);
            search_textBox.Visible = true;

            int footerY = height - 23;
            version_label.Location = new Point(margin, footerY);
            linkLabel1.Location = new Point(width - margin - linkLabel1.Width, footerY);
            linkLabel2.Location = new Point(linkLabel1.Left - gap - linkLabel2.Width, footerY);
            linkLabel3.Location = new Point(linkLabel2.Left - gap - linkLabel3.Width, footerY);

            int cancelHeight = 30;
            int cancelY = footerY - gap - cancelHeight;
            systemFontCancelButton.SetBounds(margin, cancelY, contentWidth, cancelHeight);
            systemFontCancelButton.Visible = true;

            int applyHeight = 42;
            int applyY = cancelY - gap - applyHeight;
            apply_button.SetBounds(margin, applyY, contentWidth, applyHeight);
            apply_button.Visible = true;
            apply_button.Text = "Add Selected Font";

            int previewInfoHeight = 18;
            int previewInfoY = applyY - 3 - previewInfoHeight;
            fontPreviewInfoLabel.SetBounds(margin, previewInfoY, contentWidth, previewInfoHeight);
            fontPreviewInfoLabel.Visible = true;

            int previewHeight = Math.Max(90, Math.Min(135, height / 5));
            int previewY = previewInfoY - 3 - previewHeight;
            fontPreview_richTextBox.SetBounds(margin, previewY, contentWidth, previewHeight);
            fontPreview_richTextBox.Visible = listBox1.SelectedItem != null;

            int listTop = 42;
            int listBottom = previewY - gap;
            listBox1.SetBounds(margin, listTop, contentWidth, Math.Max(120, listBottom - listTop));
            listBox1.Visible = true;
            listBox1.BringToFront();
            search_textBox.BringToFront();
            systemFontCancelButton.BringToFront();
        }

        private void LayoutPigSpecificTopRow()
        {
            if (specificSettingsPanel == null || specificSearchTextBox == null ||
                !specificSettingsTabActive) return;

            int panelWidth = specificSettingsPanel.ClientSize.Width;
            if (panelWidth < 1) return;

            int searchLeft = specificViewCombo.Right + 8;
            int generalWidth = Math.Max(180, Math.Min(250, panelWidth / 3));
            int generalLeft = panelWidth - generalWidth;
            int searchWidth = Math.Max(110, generalLeft - searchLeft - 8);

            specificSearchTextBox.SetBounds(searchLeft, 2, searchWidth, 24);
            specificSearchTextBox.Visible = true;
            specificSearchTextBox.BringToFront();

            specificGeneralSelectionLabel.SetBounds(generalLeft, 1, generalWidth, 28);
            specificGeneralSelectionLabel.AutoEllipsis = true;
        }

        private void pigRestartState_Tick(object sender, EventArgs e)
        {
            RefreshSpecificRestartButtonState();
        }

        private void RefreshSpecificRestartButtonState()
        {
            if (specificRestartButton == null || restartCs2Button == null) return;
            specificRestartButton.Enabled = restartCs2Button.Enabled;
            specificRestartButton.Text = restartCs2Button.Text;
        }

        private void QueuePigFinalLayout()
        {
            if (pigUiFinalLayoutQueued || IsDisposed || !IsHandleCreated) return;
            pigUiFinalLayoutQueued = true;

            BeginInvoke((MethodInvoker)delegate
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    pigUiFinalLayoutQueued = false;
                    if (IsDisposed) return;

                    MaximumSize = Size.Empty;
                    PigSyncViewState();
                    if (defaultPreviewPolishInitialized)
                        LayoutDefaultPreviewSurface();
                    PerformLayout();
                    Invalidate(true);
                    Update();
                    AppLog.Info("Pig startup layout finalized at " + ClientSize.Width + "x" + ClientSize.Height + ".");
                });
            });
        }

        private void SyncSpecificPreviewBridge()
        {
            bool specific = CurrentFormView == FormViews.Main && specificSettingsTabActive;
            if (specific == specificPreviewBridgeLastState) return;
            specificPreviewBridgeLastState = specific;

            if (specific)
            {
                if (defaultPreviewScrollPanel != null)
                    defaultPreviewScrollPanel.Visible = false;
            }
            else if (defaultPreviewPolishInitialized && CurrentFormView == FormViews.Main)
            {
                RefreshDefaultPreviewPolish();
            }
        }
    }
}

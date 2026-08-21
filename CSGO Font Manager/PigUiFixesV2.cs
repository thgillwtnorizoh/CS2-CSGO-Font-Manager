using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private static readonly bool PigUiV2BootstrapRegistered = RegisterPigUiV2Bootstrap();

        private bool pigUiV2Initialized;
        private bool pigUiV2LayoutBusy;
        private string pigUiV2SearchQuery = string.Empty;
        private int pigUiV2SearchIndex;
        private Control pigUiV2HighlightedRow;

        private static bool RegisterPigUiV2Bootstrap()
        {
            Application.Idle += BootstrapPigUiV2OnIdle;
            return true;
        }

        private static void BootstrapPigUiV2OnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.IsDisposed || form.pigUiV2Initialized) continue;
                if (!form.pigUiFixInitialized || !form.defaultPreviewPolishInitialized ||
                    form.specificSearchTextBox == null) continue;

                form.InitializePigUiV2();
            }
        }

        private void InitializePigUiV2()
        {
            if (pigUiV2Initialized) return;
            pigUiV2Initialized = true;

            // Once the tabbed UI exists, it is the only component allowed to position the main controls.
            // The scalable UI still owns scaling/preview state, but no longer owns window geometry.
            SizeChanged -= scalableFontUi_SizeChanged;
            SizeChanged -= specificSettings_SizeChanged;
            SizeChanged -= pigUi_SizeChanged;
            SizeChanged += pigUiV2_SizeChanged;

            listBox1.SelectedIndexChanged -= scalableFontUi_SelectedIndexChanged;
            listBox1.SelectedIndexChanged += pigUiV2_FontSelectionChanged;
            addFont_button.Click -= scalableFontUi_ViewMayHaveChanged;
            donate_button.Click -= scalableFontUi_ViewMayHaveChanged;

            // Replace the old filter search with finder-style navigation.
            if (specificSearchTimer != null)
            {
                specificSearchTimer.Tick -= specificSearchTimer_Tick;
                specificSearchTimer.Tick += pigUiV2_SearchTimerTick;
            }
            specificSearchTextBox.KeyDown += pigUiV2_SearchKeyDown;

            specificViewCombo.SelectedIndexChanged -= pigSpecificViewCombo_SelectedIndexChanged;
            specificViewCombo.SelectedIndexChanged += pigUiV2_ViewChanged;

            // Route the green-plus System Font path through the same cmap inspection/conversion
            // used by drag/drop imports. Main-view apply still delegates to the proven CS2 wrapper.
            apply_button.Click -= apply_button_cs2_enhanced_Click;
            apply_button.Click += pigUiV2_ApplyButtonClick;

            EnsureAllSpecificRowsVisible();
            ApplyAuthoritativePigLayout();
            Shown += pigUiV2_Shown;

            AppLog.Info("Pig UI v2 initialized: single layout owner, finder search, immediate view refresh and system-font encoding pipeline.");
        }

        private void pigUiV2_Shown(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate
            {
                ApplyAuthoritativePigLayout();
                AppLog.Info("Pig UI v2 first-frame layout finalized at " + ClientSize.Width + "x" + ClientSize.Height + ".");
            });
        }

        private void pigUiV2_SizeChanged(object sender, EventArgs e)
        {
            ApplyAuthoritativePigLayout();
        }

        private void pigUiV2_FontSelectionChanged(object sender, EventArgs e)
        {
            if (!pigUiV2Initialized || IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate
            {
                UpdateScaledFontPreview();
                ApplyAuthoritativePigLayout();
            });
        }

        private void ApplyAuthoritativePigLayout()
        {
            if (!pigUiV2Initialized || pigUiV2LayoutBusy || IsDisposed || ClientSize.Width < 1 || ClientSize.Height < 1)
                return;

            pigUiV2LayoutBusy = true;
            SuspendLayout();
            try
            {
                MaximumSize = Size.Empty;
                PigSyncViewState();

                if (CurrentFormView == FormViews.Main && !specificSettingsTabActive)
                {
                    // Default preview is a separate overlay panel and must follow the final RichTextBox bounds.
                    if (defaultPreviewPolishInitialized)
                    {
                        LayoutDefaultPreviewSurface();
                        RefreshDefaultPreviewPolish();
                    }
                }
                else if (defaultPreviewScrollPanel != null)
                {
                    defaultPreviewScrollPanel.Visible = false;
                }

                if (specificSettingsTabActive)
                {
                    LayoutPigSpecificTopRow();
                    ForceSpecificFlowLayout();
                }
            }
            finally
            {
                ResumeLayout(true);
                pigUiV2LayoutBusy = false;
            }

            Invalidate(true);
        }

        private void pigUiV2_ViewChanged(object sender, EventArgs e)
        {
            if (!pigUiV2Initialized || specificViewCombo.SelectedItem == null) return;
            if (Settings != null)
                Settings.SpecificFontViewMode = specificViewCombo.SelectedItem.ToString();

            ReorderCachedSpecificRows();
            EnsureAllSpecificRowsVisible();
            ForceSpecificFlowLayout();
            NavigateSpecificSearch(false);
            AppLog.Info("Specific view changed immediately to '" + specificViewCombo.SelectedItem + "'.");
        }

        private void pigUiV2_SearchTimerTick(object sender, EventArgs e)
        {
            if (specificSearchTimer != null) specificSearchTimer.Stop();
            NavigateSpecificSearch(false);
        }

        private void pigUiV2_SearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            e.Handled = true;
            NavigateSpecificSearch(true);
        }

        private void NavigateSpecificSearch(bool next)
        {
            if (!pigUiV2Initialized || specificFamilyFlow == null || specificSearchTextBox == null) return;

            EnsureAllSpecificRowsVisible();
            ResetSpecificSearchHighlight();

            string query = (specificSearchTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                pigUiV2SearchQuery = string.Empty;
                pigUiV2SearchIndex = 0;
                if (specificToolTip != null)
                    specificToolTip.SetToolTip(specificSearchTextBox,
                        "Find family names and predicted CS2 usage. Enter jumps to the next match.");
                ForceSpecificFlowLayout();
                return;
            }

            List<Control> matches = new List<Control>();
            foreach (Control control in specificFamilyFlow.Controls)
            {
                FamilySpec spec = control.Tag as FamilySpec;
                if (spec != null && SpecificSpecMatchesQuery(spec, query))
                    matches.Add(control);
            }

            if (matches.Count == 0)
            {
                pigUiV2SearchQuery = query;
                pigUiV2SearchIndex = 0;
                if (specificToolTip != null)
                    specificToolTip.SetToolTip(specificSearchTextBox, "No family/role matches '" + query + "'.");
                ForceSpecificFlowLayout();
                return;
            }

            if (!string.Equals(query, pigUiV2SearchQuery, StringComparison.OrdinalIgnoreCase))
            {
                pigUiV2SearchQuery = query;
                pigUiV2SearchIndex = 0;
            }
            else if (next)
            {
                pigUiV2SearchIndex = (pigUiV2SearchIndex + 1) % matches.Count;
            }
            else if (pigUiV2SearchIndex >= matches.Count)
            {
                pigUiV2SearchIndex = 0;
            }

            Control row = matches[pigUiV2SearchIndex];
            HighlightSpecificSearchRow(row, query);
            ForceSpecificFlowLayout();
            specificFamilyFlow.ScrollControlIntoView(row);
            row.Focus();
            specificSearchTextBox.Focus();

            if (specificToolTip != null)
                specificToolTip.SetToolTip(specificSearchTextBox,
                    "Match " + (pigUiV2SearchIndex + 1) + "/" + matches.Count + ". Press Enter for next.");
        }

        private void EnsureAllSpecificRowsVisible()
        {
            foreach (Control row in pigSpecificRows.Values)
                row.Visible = true;

            bool allFamilies = specificViewCombo != null && specificViewCombo.SelectedItem != null &&
                               specificViewCombo.SelectedItem.ToString() == "All families";
            foreach (Control header in pigSpecificHeaders.Values)
                header.Visible = !allFamilies;
        }

        private void ResetSpecificSearchHighlight()
        {
            if (pigUiV2HighlightedRow == null) return;
            pigUiV2HighlightedRow.BackColor = Color.FromArgb(37, 37, 40);
            foreach (Control child in pigUiV2HighlightedRow.Controls)
            {
                Label label = child as Label;
                if (label == null) continue;
                FamilySpec spec = pigUiV2HighlightedRow.Tag as FamilySpec;
                if (spec != null && string.Equals(label.Text, spec.Family, StringComparison.Ordinal))
                    label.ForeColor = Color.White;
                else
                    label.ForeColor = Color.FromArgb(175, 185, 192);
            }
            pigUiV2HighlightedRow = null;
        }

        private void HighlightSpecificSearchRow(Control row, string query)
        {
            pigUiV2HighlightedRow = row;
            row.BackColor = Color.FromArgb(45, 70, 58);
            foreach (Control child in row.Controls)
            {
                Label label = child as Label;
                if (label == null) continue;
                if (ContainsIgnoreCase(label.Text, query))
                    label.ForeColor = Color.MediumSpringGreen;
            }
        }

        private void ForceSpecificFlowLayout()
        {
            if (specificFamilyFlow == null) return;
            ResizeSpecificFamilyRows();
            specificFamilyFlow.ResumeLayout(true);
            specificFamilyFlow.PerformLayout();
            specificFamilyFlow.Invalidate(true);
            specificSettingsPanel?.PerformLayout();
        }

        private void pigUiV2_ApplyButtonClick(object sender, EventArgs e)
        {
            if (CurrentFormView != FormViews.AddSystemFont)
            {
                apply_button_cs2_enhanced_Click(sender, e);
                return;
            }

            ImportSelectedSystemFontWithEncodingCheck();
        }

        private void ImportSelectedSystemFontWithEncodingCheck()
        {
            if (listBox1.SelectedItem == null)
            {
                MessageBox.Show("Please select a system font first.", "No Font Selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string selectedFamily = listBox1.SelectedItem.ToString();
            string sourcePath = null;
            Font selectedFont = null;
            List<string> temporaryDirectories = new List<string>();

            try
            {
                FontFamily systemFamily = new FontFamily(selectedFamily);
                selectedFont = new Font(systemFamily, 14);

                string systemFileName = GetSystemFontFileName(selectedFont);
                if (!string.IsNullOrWhiteSpace(systemFileName))
                {
                    string candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), systemFileName);
                    if (File.Exists(candidate)) sourcePath = candidate;
                }

                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    sourcePath = GetFilesForFont(selectedFont.Name).FirstOrDefault(File.Exists);
                }

                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    MessageBox.Show("The font file for '" + selectedFamily + "' could not be found.",
                        "Font Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    AppLog.Warn("System-font import could not resolve a source file for: " + selectedFamily);
                    return;
                }

                AppLog.Info("System-font import resolved '" + selectedFamily + "' to " + sourcePath + ".");
                string preparedPath = PrepareFontEncodingForCs2(sourcePath, temporaryDirectories);
                if (string.IsNullOrWhiteSpace(preparedPath)) return;

                string actualFamily = GetFontFamilyNameFromFile(preparedPath);
                if (string.IsNullOrWhiteSpace(actualFamily)) actualFamily = selectedFamily;
                string libraryName = sanitizeFilename(actualFamily);
                string libraryDirectory = Path.Combine(FontsFolder, libraryName);

                if (Directory.Exists(libraryDirectory))
                {
                    if (MessageBox.Show("The font '" + libraryName +
                            "' is already in your library. Overwrite it?",
                            "Overwrite Font?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                    Directory.Delete(libraryDirectory, true);
                }

                Directory.CreateDirectory(libraryDirectory);
                string targetName = Path.GetFileName(preparedPath);
                string targetPath = Path.Combine(libraryDirectory, targetName);
                File.Copy(preparedPath, targetPath, true);

                AddFont(libraryName, targetPath);
                setupFontsDirectory(Path.Combine(libraryDirectory, "fonts.conf"), actualFamily, targetName);

                FontEncodingInfo info = FontEncodingInspector.Inspect(targetPath);
                AppLog.Info("System font imported through encoding pipeline: family='" + actualFamily +
                            "', file='" + targetName + "', encoding='" + info.EncodingDescription + "', " + info.Detail);

                MessageBox.Show("Success! The following font has been added to your library!\n---\n" + actualFamily,
                    "Font Added!", MessageBoxButtons.OK, MessageBoxIcon.Information);

                switchView(FormViews.Main);
                refreshFontList();
                PopulateGeneralFontSearch(string.Empty);
                ApplyAuthoritativePigLayout();
            }
            catch (Exception exception)
            {
                AppLog.Error("System-font import failed for " + selectedFamily + ".", exception);
                MessageBox.Show("Failed to import the system font.\n\n" + exception.Message,
                    "Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (selectedFont != null) selectedFont.Dispose();
                foreach (string directory in temporaryDirectories.Distinct())
                {
                    try
                    {
                        if (Directory.Exists(directory)) Directory.Delete(directory, true);
                    }
                    catch (Exception cleanupException)
                    {
                        AppLog.Warn("Could not clean temporary system-font import directory " + directory +
                                    ": " + cleanupException.Message);
                    }
                }
            }
        }
    }
}

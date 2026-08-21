using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private static readonly float[] FontScalePresets =
        {
            0.10f, 0.25f, 0.50f, 0.75f, 1.00f,
            1.25f, 1.50f, 2.00f, 3.00f, 5.00f
        };

        private const float PreviewBasePointSize = 14.0f;
        private const float MinimumCustomScale = 0.01f;
        private const float MaximumCustomScale = 20.00f;

        private static readonly bool FontScaleUiBootstrapRegistered = RegisterFontScaleUiBootstrap();

        private bool fontScaleUiInitialized;
        private bool fontScaleLayoutBusy;
        private float currentFontScale = 1.00f;
        private bool currentFontScaleIsCustom;

        private Label fontScaleValueLabel;
        private Label fontPreviewInfoLabel;
        private Button customFontScaleButton;
        private ToolTip fontScaleToolTip;
        private Font currentPreviewFont;
        private PrivateFontCollection defaultPreviewFontCollection;
        private FontFamily defaultPreviewFontFamily;
        private string defaultPreviewRoot;

        private static bool RegisterFontScaleUiBootstrap()
        {
            Application.Idle += BootstrapFontScaleUiOnIdle;
            return true;
        }

        private static void BootstrapFontScaleUiOnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.fontScaleUiInitialized) continue;
                if (!form.cs2EnhancementsInitialized || form.restartCs2Button == null) continue;
                form.InitializeScalableFontUi();
            }
        }

        private void InitializeScalableFontUi()
        {
            if (fontScaleUiInitialized) return;
            fontScaleUiInitialized = true;

            float savedScale = Settings == null ? 1.00f : Settings.FontScale;
            if (!IsValidFontScale(savedScale)) savedScale = 1.00f;
            currentFontScale = savedScale;
            currentFontScaleIsCustom = FindPresetIndex(savedScale) < 0;

            trackBar1.Scroll -= trackBar1_Scroll;
            trackBar1.Minimum = 0;
            trackBar1.Maximum = FontScalePresets.Length - 1;
            trackBar1.SmallChange = 1;
            trackBar1.LargeChange = 1;
            trackBar1.TickFrequency = 1;
            trackBar1.Value = FindNearestPresetIndex(currentFontScale);
            trackBar1.Scroll += fontScaleSlider_Scroll;

            fontScaleValueLabel = new Label
            {
                Name = "fontScaleValueLabel",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Microsoft Sans Serif", 10.0f, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 235, 244),
                BackColor = Color.Transparent
            };

            customFontScaleButton = new Button
            {
                Name = "customFontScaleButton",
                Text = "\u2699",
                Font = new Font("Segoe UI Symbol", 11.0f, FontStyle.Regular),
                FlatStyle = FlatStyle.Popup,
                BackColor = Color.FromArgb(64, 64, 64),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                TabStop = false
            };
            customFontScaleButton.Click += customFontScaleButton_Click;

            fontPreviewInfoLabel = new Label
            {
                Name = "fontPreviewInfoLabel",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft Sans Serif", 8.25f, FontStyle.Regular),
                ForeColor = Color.Gray,
                BackColor = Color.Transparent
            };

            Controls.Add(fontScaleValueLabel);
            Controls.Add(customFontScaleButton);
            Controls.Add(fontPreviewInfoLabel);
            fontScaleValueLabel.BringToFront();
            customFontScaleButton.BringToFront();
            fontPreviewInfoLabel.BringToFront();

            fontScaleToolTip = new ToolTip { ShowAlways = true };
            fontScaleToolTip.SetToolTip(customFontScaleButton,
                "Set a custom font scale from 0.01x to 20.00x.");
            fontScaleToolTip.SetToolTip(trackBar1,
                "Preset font scale. Moving the slider leaves custom mode.");

            fontPreview_richTextBox.ZoomFactor = 1.0f;
            fontPreview_richTextBox.WordWrap = true;
            fontPreview_richTextBox.ScrollBars = RichTextBoxScrollBars.Vertical;

            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimumSize = new Size(410, 560);
            ClientSize = new Size(Math.Max(ClientSize.Width, 470), Math.Max(ClientSize.Height, 590));

            SizeChanged += scalableFontUi_SizeChanged;
            listBox1.SelectedIndexChanged += scalableFontUi_SelectedIndexChanged;
            addFont_button.Click += scalableFontUi_ViewMayHaveChanged;
            donate_button.Click += scalableFontUi_ViewMayHaveChanged;

            UpdateFontScaleReadout();
            ReflowScalableFontUi();
            UpdateScaledFontPreview();

            AppLog.Info("Scalable font UI initialized. Scale=" + currentFontScale.ToString("0.00") +
                        "x, custom=" + currentFontScaleIsCustom + ".");
        }

        private static bool IsValidFontScale(float scale)
        {
            return !float.IsNaN(scale) && !float.IsInfinity(scale) &&
                   scale >= MinimumCustomScale && scale <= MaximumCustomScale;
        }

        private int FindPresetIndex(float scale)
        {
            for (int i = 0; i < FontScalePresets.Length; i++)
            {
                if (Math.Abs(FontScalePresets[i] - scale) < 0.0001f) return i;
            }
            return -1;
        }

        private int FindNearestPresetIndex(float scale)
        {
            int bestIndex = 0;
            float bestDistance = Math.Abs(FontScalePresets[0] - scale);
            for (int i = 1; i < FontScalePresets.Length; i++)
            {
                float distance = Math.Abs(FontScalePresets[i] - scale);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        private void fontScaleSlider_Scroll(object sender, EventArgs e)
        {
            int index = Math.Max(0, Math.Min(FontScalePresets.Length - 1, trackBar1.Value));
            SetCurrentFontScale(FontScalePresets[index], false, false);
        }

        private void customFontScaleButton_Click(object sender, EventArgs e)
        {
            using (Form dialog = new Form())
            using (Label prompt = new Label())
            using (NumericUpDown scaleInput = new NumericUpDown())
            using (Button apply = new Button())
            using (Button cancel = new Button())
            {
                dialog.Text = "Custom Font Scale";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(300, 138);
                dialog.BackColor = BackColor;
                dialog.ForeColor = ForeColor;

                prompt.AutoSize = false;
                prompt.Location = new Point(12, 12);
                prompt.Size = new Size(276, 38);
                prompt.Text = "Set any scale from 0.01x to 20.00x.\nTouching the slider later returns to presets.";
                prompt.ForeColor = Color.FromArgb(200, 235, 244);

                scaleInput.DecimalPlaces = 2;
                scaleInput.Minimum = (decimal)MinimumCustomScale;
                scaleInput.Maximum = (decimal)MaximumCustomScale;
                scaleInput.Increment = 0.05m;
                scaleInput.Location = new Point(12, 58);
                scaleInput.Size = new Size(178, 24);
                decimal starting = (decimal)Math.Max(MinimumCustomScale,
                    Math.Min(MaximumCustomScale, currentFontScale));
                scaleInput.Value = starting;

                Label suffix = new Label
                {
                    AutoSize = true,
                    Text = "x",
                    ForeColor = Color.FromArgb(200, 235, 244),
                    Location = new Point(196, 62)
                };
                dialog.Controls.Add(suffix);

                apply.Text = "Apply";
                apply.DialogResult = DialogResult.OK;
                apply.Location = new Point(132, 98);
                apply.Size = new Size(75, 28);

                cancel.Text = "Cancel";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Location = new Point(213, 98);
                cancel.Size = new Size(75, 28);

                dialog.Controls.Add(prompt);
                dialog.Controls.Add(scaleInput);
                dialog.Controls.Add(apply);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = apply;
                dialog.CancelButton = cancel;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    SetCurrentFontScale((float)scaleInput.Value, true, true);
            }
        }

        private void SetCurrentFontScale(float scale, bool custom, bool synchronizeSlider)
        {
            if (!IsValidFontScale(scale)) return;

            currentFontScale = scale;
            currentFontScaleIsCustom = custom;
            if (Settings != null) Settings.FontScale = scale;

            if (synchronizeSlider)
                trackBar1.Value = FindNearestPresetIndex(scale);

            UpdateFontScaleReadout();
            UpdateScaledFontPreview();
            AppLog.Info("Font scale changed to " + scale.ToString("0.00") +
                        "x" + (custom ? " (custom)." : " (preset)."));
        }

        private float GetCurrentFontScale()
        {
            if (fontScaleUiInitialized && IsValidFontScale(currentFontScale))
                return currentFontScale;

            float saved = Settings == null ? 1.00f : Settings.FontScale;
            return IsValidFontScale(saved) ? saved : 1.00f;
        }

        private void UpdateFontScaleReadout()
        {
            if (fontScaleValueLabel == null) return;

            fontScaleValueLabel.Text = currentFontScale.ToString("0.00") + "x";
            string tip;
            if (currentFontScaleIsCustom)
                tip = "Custom scale. Touch the slider to return to presets.";
            else if (Math.Abs(currentFontScale - 1.00f) < 0.0001f)
                tip = "1.00x = original CS2 size.";
            else if (Math.Abs(currentFontScale - 5.00f) < 0.0001f)
                tip = "5.00x = this was a deliberate decision.";
            else
                tip = "Current CS2 font scale.";
            fontScaleToolTip?.SetToolTip(fontScaleValueLabel, tip);
        }

        private void scalableFontUi_SizeChanged(object sender, EventArgs e)
        {
            ReflowScalableFontUi();
        }

        private void scalableFontUi_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!fontScaleUiInitialized) return;
            BeginInvoke((MethodInvoker)delegate
            {
                ReflowScalableFontUi();
                UpdateScaledFontPreview();
            });
        }

        private void scalableFontUi_ViewMayHaveChanged(object sender, EventArgs e)
        {
            if (!fontScaleUiInitialized) return;
            BeginInvoke((MethodInvoker)delegate
            {
                ReflowScalableFontUi();
                UpdateScaledFontPreview();
            });
        }

        private void ReflowScalableFontUi()
        {
            if (!fontScaleUiInitialized || fontScaleLayoutBusy || IsDisposed) return;
            if (ClientSize.Width < 1 || ClientSize.Height < 1) return;

            fontScaleLayoutBusy = true;
            try
            {
                const int margin = 12;
                const int gap = 7;
                int width = ClientSize.Width;
                int height = ClientSize.Height;
                int contentWidth = Math.Max(180, width - margin * 2);
                bool mainView = CurrentFormView == FormViews.Main;

                title_label.Location = new Point(margin, 8);

                int iconSize = 22;
                remove_button.Size = new Size(iconSize, iconSize);
                remove_button.Location = new Point(width - margin - iconSize, 8);
                addFont_button.Size = new Size(iconSize, iconSize);
                addFont_button.Location = new Point(remove_button.Left - gap - iconSize, 8);

                int searchRight = width - margin;
                if (CurrentFormView == FormViews.Main)
                    searchRight = addFont_button.Left - gap;
                search_textBox.Location = new Point(Math.Max(margin + 120, searchRight - 190), 8);
                search_textBox.Size = new Size(Math.Max(100, searchRight - search_textBox.Left), 23);

                int footerY = height - 23;
                version_label.Location = new Point(margin, footerY);
                linkLabel1.Location = new Point(width - margin - linkLabel1.Width, footerY);
                linkLabel2.Location = new Point(linkLabel1.Left - gap - linkLabel2.Width, footerY);
                linkLabel3.Location = new Point(linkLabel2.Left - gap - linkLabel3.Width, footerY);

                int donateHeight = 30;
                int donateY = footerY - gap - donateHeight;
                donate_button.SetBounds(margin, donateY, contentWidth, donateHeight);

                int restartHeight = mainView ? 30 : 0;
                int restartY = mainView ? donateY - gap - restartHeight : donateY;
                if (restartCs2Button != null)
                {
                    restartCs2Button.Visible = mainView;
                    if (mainView)
                        restartCs2Button.SetBounds(margin, restartY, contentWidth, restartHeight);
                }

                int applyHeight = 42;
                int applyY = (mainView ? restartY : donateY) - gap - applyHeight;
                apply_button.SetBounds(margin, applyY, contentWidth, applyHeight);

                int previewInfoHeight = 18;
                int previewInfoY = applyY - 3 - previewInfoHeight;
                fontPreviewInfoLabel.SetBounds(margin, previewInfoY, contentWidth, previewInfoHeight);

                int previewHeight = Math.Max(82, Math.Min(122, height / 5));
                int previewY = previewInfoY - 3 - previewHeight;
                fontPreview_richTextBox.SetBounds(margin, previewY, contentWidth, previewHeight);

                int listTop = 42;
                int listBottom;

                if (mainView)
                {
                    int scaleRowHeight = 38;
                    int scaleY = previewY - gap - scaleRowHeight;
                    int cogWidth = 34;
                    int readoutWidth = 74;
                    int sliderWidth = Math.Max(100, contentWidth - cogWidth - readoutWidth - gap * 2);

                    trackBar1.SetBounds(margin, scaleY, sliderWidth, scaleRowHeight);
                    fontScaleValueLabel.SetBounds(trackBar1.Right + gap, scaleY + 1, readoutWidth, 29);
                    customFontScaleButton.SetBounds(fontScaleValueLabel.Right + gap, scaleY + 1, cogWidth, 29);

                    trackBar1.Visible = true;
                    fontScaleValueLabel.Visible = true;
                    customFontScaleButton.Visible = true;
                    listBottom = scaleY - gap;
                }
                else
                {
                    trackBar1.Visible = false;
                    fontScaleValueLabel.Visible = false;
                    customFontScaleButton.Visible = false;
                    listBottom = previewY - gap;
                }

                int listHeight = Math.Max(90, listBottom - listTop);
                listBox1.SetBounds(margin, listTop, contentWidth, listHeight);
            }
            finally
            {
                fontScaleLayoutBusy = false;
            }
        }

        private void UpdateScaledFontPreview()
        {
            if (!fontScaleUiInitialized || fontPreviewInfoLabel == null) return;

            UpdateFontScaleReadout();

            if (CurrentFormView != FormViews.Main)
            {
                string systemSelection = listBox1.SelectedItem == null ? "System font" : listBox1.SelectedItem.ToString();
                fontPreviewInfoLabel.Text = systemSelection + " \u2022 System font preview";
                return;
            }

            trackBar1.Visible = true;
            fontScaleValueLabel.Visible = true;
            customFontScaleButton.Visible = true;

            if (listBox1.SelectedItem == null)
            {
                fontPreview_richTextBox.Visible = false;
                fontPreviewInfoLabel.Text = "No font selected";
                return;
            }

            string selected = listBox1.SelectedItem.ToString();
            if (selected == DefaultFontName)
            {
                FontFamily family;
                string familyName;
                if (TryGetDefaultCs2PreviewFamily(out family, out familyName))
                {
                    ApplyScaledPreviewFont(family, "CS2 Default (" + familyName + ")");
                }
                else
                {
                    ReplaceCurrentPreviewFont(new Font(SystemFonts.MessageBoxFont.FontFamily, 10.0f, FontStyle.Regular));
                    fontPreview_richTextBox.ZoomFactor = 1.0f;
                    fontPreview_richTextBox.Text =
                        "Default CS2 font preview unavailable.\nVerify the CS2 path and bundled panorama fonts.";
                    fontPreview_richTextBox.Visible = true;
                    fontPreviewInfoLabel.Text = "CS2 Default \u2022 preview unavailable \u2022 " +
                                                currentFontScale.ToString("0.00") + "x";
                }
                return;
            }

            FontFamily customFamily = GetFontFamilyByName(selected);
            if (customFamily == null)
            {
                fontPreview_richTextBox.Visible = false;
                fontPreviewInfoLabel.Text = selected + " \u2022 preview unavailable \u2022 " +
                                            currentFontScale.ToString("0.00") + "x";
                return;
            }

            ApplyScaledPreviewFont(customFamily, customFamily.Name);
        }

        private void ApplyScaledPreviewFont(FontFamily family, string descriptor)
        {
            float pointSize = PreviewBasePointSize * currentFontScale;
            if (pointSize <= 0.0f) pointSize = 0.1f;

            Font previewFont = CreateUsableFont(family, pointSize);
            if (previewFont == null)
            {
                fontPreview_richTextBox.Visible = false;
                fontPreviewInfoLabel.Text = descriptor + " \u2022 preview unavailable";
                return;
            }

            ReplaceCurrentPreviewFont(previewFont);
            fontPreview_richTextBox.ZoomFactor = 1.0f;
            fontPreview_richTextBox.Text = FontPreviewText;
            fontPreview_richTextBox.Visible = true;
            fontPreviewInfoLabel.Text = descriptor + " \u2022 " + currentFontScale.ToString("0.00") + "x" +
                                        (currentFontScaleIsCustom ? " \u2022 Custom" : string.Empty);
        }

        private static Font CreateUsableFont(FontFamily family, float pointSize)
        {
            try
            {
                if (family.IsStyleAvailable(FontStyle.Regular))
                    return new Font(family, pointSize, FontStyle.Regular, GraphicsUnit.Point);
                if (family.IsStyleAvailable(FontStyle.Bold))
                    return new Font(family, pointSize, FontStyle.Bold, GraphicsUnit.Point);
                if (family.IsStyleAvailable(FontStyle.Italic))
                    return new Font(family, pointSize, FontStyle.Italic, GraphicsUnit.Point);
                if (family.IsStyleAvailable(FontStyle.Bold | FontStyle.Italic))
                    return new Font(family, pointSize, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Point);
            }
            catch
            {
            }
            return null;
        }

        private void ReplaceCurrentPreviewFont(Font next)
        {
            Font previous = currentPreviewFont;
            currentPreviewFont = next;
            fontPreview_richTextBox.Font = currentPreviewFont;
            if (previous != null && !ReferenceEquals(previous, currentPreviewFont))
                previous.Dispose();
        }

        private bool TryGetDefaultCs2PreviewFamily(out FontFamily family, out string familyName)
        {
            family = null;
            familyName = null;

            if (Settings == null || string.IsNullOrWhiteSpace(Settings.CsgoPath)) return false;
            string root = Settings.CsgoPath + Cs2FontsRelativePath;
            if (!Directory.Exists(root)) return false;

            if (!string.Equals(defaultPreviewRoot, root, StringComparison.OrdinalIgnoreCase) ||
                defaultPreviewFontCollection == null)
            {
                ResetDefaultPreviewCollection();
                defaultPreviewRoot = root;
                defaultPreviewFontCollection = new PrivateFontCollection();

                string[] files = Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly)
                    .Where(file =>
                    {
                        string extension = Path.GetExtension(file).ToLowerInvariant();
                        return extension == ".ttf" || extension == ".otf" || extension == ".ttc";
                    })
                    .OrderByDescending(file => Path.GetFileName(file).IndexOf("stratum", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToArray();

                foreach (string file in files)
                {
                    try
                    {
                        defaultPreviewFontCollection.AddFontFile(file);
                    }
                    catch
                    {
                    }
                }

                defaultPreviewFontFamily = defaultPreviewFontCollection.Families
                    .FirstOrDefault(item => string.Equals(item.Name, "Stratum2", StringComparison.OrdinalIgnoreCase))
                    ?? defaultPreviewFontCollection.Families
                        .FirstOrDefault(item => item.Name.IndexOf("Stratum2", StringComparison.OrdinalIgnoreCase) >= 0);

                if (defaultPreviewFontFamily != null)
                    AppLog.Info("Resolved CS2 default preview family: " + defaultPreviewFontFamily.Name +
                                " from " + root);
                else
                    AppLog.Warn("Could not resolve a Stratum2 family for the default CS2 preview in " + root);
            }

            if (defaultPreviewFontFamily == null) return false;
            family = defaultPreviewFontFamily;
            familyName = defaultPreviewFontFamily.Name;
            return true;
        }

        private void ResetDefaultPreviewCollection()
        {
            defaultPreviewFontFamily = null;
            if (defaultPreviewFontCollection != null)
            {
                defaultPreviewFontCollection.Dispose();
                defaultPreviewFontCollection = null;
            }
        }
    }
}

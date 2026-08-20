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
        private sealed class Cs2PreviewChoice : IDisposable
        {
            public string SourceLabel;
            public string SourcePath;
            public string SystemFamilyName;
            public byte[] EmbeddedData;
            public string EmbeddedFileName;
            public PrivateFontCollection Collection;
            public FontFamily Family;
            public string TempFilePath;

            public string CandidateName
            {
                get
                {
                    if (!string.IsNullOrWhiteSpace(EmbeddedFileName)) return EmbeddedFileName;
                    if (!string.IsNullOrWhiteSpace(SourcePath)) return Path.GetFileName(SourcePath);
                    return SystemFamilyName ?? SourceLabel ?? "CS2 font";
                }
            }

            public bool EnsureLoaded(string tempDirectory, out string error)
            {
                error = null;
                if (Family != null) return true;

                try
                {
                    if (!string.IsNullOrWhiteSpace(SystemFamilyName))
                    {
                        Family = new FontFamily(SystemFamilyName);
                        return true;
                    }

                    Collection = new PrivateFontCollection();
                    if (EmbeddedData != null)
                    {
                        Directory.CreateDirectory(tempDirectory);
                        string fileName = string.IsNullOrWhiteSpace(EmbeddedFileName)
                            ? Guid.NewGuid().ToString("N") + ".ttf"
                            : Path.GetFileName(EmbeddedFileName);
                        TempFilePath = Path.Combine(tempDirectory,
                            Guid.NewGuid().ToString("N") + "_" + fileName);
                        File.WriteAllBytes(TempFilePath, EmbeddedData);
                        Collection.AddFontFile(TempFilePath);
                    }
                    else
                    {
                        Collection.AddFontFile(SourcePath);
                    }

                    if (Collection.Families.Length == 0)
                        throw new InvalidDataException("The font file did not expose a GDI+ font family.");

                    Family = Collection.Families[0];
                    return true;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    Dispose();
                    return false;
                }
            }

            public void Dispose()
            {
                if (Collection != null)
                {
                    Collection.Dispose();
                    Collection = null;
                    Family = null;
                }
                else if (Family != null)
                {
                    Family.Dispose();
                    Family = null;
                }
            }
        }

        private static readonly bool DefaultPreviewPolishBootstrapRegistered = RegisterDefaultPreviewPolishBootstrap();

        private bool defaultPreviewPolishInitialized;
        private Panel defaultPreviewScrollPanel;
        private Label defaultPreviewTextLabel;
        private Font defaultPreviewRenderedFont;
        private readonly List<Cs2PreviewChoice> cs2PreviewChoices = new List<Cs2PreviewChoice>();
        private string cs2PreviewChoicesRoot;
        private string cs2PreviewTempDirectory;
        private int cs2PreviewChoiceIndex;

        private static bool RegisterDefaultPreviewPolishBootstrap()
        {
            Application.Idle += BootstrapDefaultPreviewPolishOnIdle;
            return true;
        }

        private static void BootstrapDefaultPreviewPolishOnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.defaultPreviewPolishInitialized) continue;
                if (!form.fontScaleUiInitialized || form.customFontScaleButton == null ||
                    form.fontPreviewInfoLabel == null) continue;

                form.InitializeDefaultPreviewPolish();
            }
        }

        private void InitializeDefaultPreviewPolish()
        {
            if (defaultPreviewPolishInitialized) return;
            defaultPreviewPolishInitialized = true;

            // The original packaged preview rendered a private font through RichEdit.
            // RichEdit can silently substitute an installed system font, so retire that
            // preview path and render private CS2 fonts through GDI+ instead.
            trackBar1.Scroll -= packagedDefaultPreview_RefreshLater;
            customFontScaleButton.Click -= packagedDefaultPreview_RefreshLater;
            listBox1.SelectedIndexChanged -= packagedDefaultPreview_RefreshLater;
            addFont_button.Click -= packagedDefaultPreview_RefreshLater;
            donate_button.Click -= packagedDefaultPreview_RefreshLater;
            ResetPackagedDefaultPreview();

            // Make the scale button look like a small sibling of Apply Selected Font.
            customFontScaleButton.FlatStyle = apply_button.FlatStyle;
            customFontScaleButton.BackColor = apply_button.BackColor;
            customFontScaleButton.ForeColor = apply_button.ForeColor;
            customFontScaleButton.UseVisualStyleBackColor = false;
            fontScaleValueLabel.ForeColor = Color.WhiteSmoke;

            defaultPreviewScrollPanel = new Panel
            {
                Name = "defaultPreviewScrollPanel",
                AutoScroll = true,
                BackColor = fontPreview_richTextBox.BackColor,
                Visible = false,
                TabStop = false
            };

            defaultPreviewTextLabel = new Label
            {
                Name = "defaultPreviewTextLabel",
                AutoSize = true,
                UseCompatibleTextRendering = true,
                BackColor = fontPreview_richTextBox.BackColor,
                ForeColor = Color.WhiteSmoke,
                Text = FontPreviewText,
                Location = new Point(0, 0),
                Padding = new Padding(0)
            };

            defaultPreviewScrollPanel.Controls.Add(defaultPreviewTextLabel);
            Controls.Add(defaultPreviewScrollPanel);
            defaultPreviewScrollPanel.BringToFront();

            fontPreviewInfoLabel.Click += fontPreviewInfoLabel_CycleCs2Preview;
            trackBar1.Scroll += defaultPreviewPolish_RefreshLater;
            customFontScaleButton.Click += defaultPreviewPolish_RefreshLater;
            listBox1.SelectedIndexChanged += defaultPreviewPolish_RefreshLater;
            addFont_button.Click += defaultPreviewPolish_RefreshLater;
            donate_button.Click += defaultPreviewPolish_RefreshLater;
            SizeChanged += defaultPreviewPolish_LayoutLater;
            FormClosed += defaultPreviewPolish_FormClosed;

            fontScaleToolTip.SetToolTip(fontPreviewInfoLabel,
                "When Default Font is selected, click here to preview the next CS2 font. Shift+click goes backward.");

            BeginInvoke((MethodInvoker)RefreshDefaultPreviewPolish);
            AppLog.Info("Default preview polish initialized: GDI+ private-font rendering and click-to-cycle enabled.");
        }

        private void defaultPreviewPolish_RefreshLater(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)RefreshDefaultPreviewPolish);
        }

        private void defaultPreviewPolish_LayoutLater(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)LayoutDefaultPreviewSurface);
        }

        private void LayoutDefaultPreviewSurface()
        {
            if (defaultPreviewScrollPanel == null || fontPreview_richTextBox == null) return;

            defaultPreviewScrollPanel.Bounds = fontPreview_richTextBox.Bounds;
            int textWidth = Math.Max(40, defaultPreviewScrollPanel.ClientSize.Width - 20);
            defaultPreviewTextLabel.MaximumSize = new Size(textWidth, 0);
        }

        private void RefreshDefaultPreviewPolish()
        {
            if (!defaultPreviewPolishInitialized || defaultPreviewScrollPanel == null) return;
            LayoutDefaultPreviewSurface();

            bool defaultSelected = CurrentFormView == FormViews.Main &&
                                   listBox1.SelectedItem != null &&
                                   listBox1.SelectedItem.ToString() == DefaultFontName;

            if (!defaultSelected)
            {
                defaultPreviewScrollPanel.Visible = false;
                fontPreviewInfoLabel.Cursor = Cursors.Default;
                fontPreviewInfoLabel.ForeColor = Color.Gray;
                return;
            }

            EnsureCs2PreviewChoices();
            if (cs2PreviewChoices.Count == 0)
            {
                defaultPreviewScrollPanel.Visible = false;
                fontPreview_richTextBox.Visible = true;
                fontPreviewInfoLabel.Cursor = Cursors.Default;
                fontPreviewInfoLabel.ForeColor = Color.Gray;
                fontPreviewInfoLabel.Text = "CS2 Default \u2022 no bundled preview fonts found \u2022 " +
                                            currentFontScale.ToString("0.00") + "x";
                return;
            }

            if (cs2PreviewChoiceIndex < 0 || cs2PreviewChoiceIndex >= cs2PreviewChoices.Count)
                cs2PreviewChoiceIndex = 0;

            Cs2PreviewChoice choice = cs2PreviewChoices[cs2PreviewChoiceIndex];
            string error;
            if (!choice.EnsureLoaded(cs2PreviewTempDirectory, out error))
            {
                AppLog.Warn("Could not load CS2 preview candidate '" + choice.CandidateName + "': " + error);
                if (cs2PreviewChoices.Count > 1)
                {
                    cs2PreviewChoiceIndex = (cs2PreviewChoiceIndex + 1) % cs2PreviewChoices.Count;
                    BeginInvoke((MethodInvoker)RefreshDefaultPreviewPolish);
                }
                return;
            }

            float pointSize = PreviewBasePointSize * currentFontScale;
            if (pointSize < 0.1f) pointSize = 0.1f;
            Font rendered = CreateUsableFont(choice.Family, pointSize);
            if (rendered == null)
            {
                AppLog.Warn("No usable GDI+ style for CS2 preview candidate '" + choice.CandidateName + "'.");
                return;
            }

            Font previous = defaultPreviewRenderedFont;
            defaultPreviewRenderedFont = rendered;
            defaultPreviewTextLabel.Font = defaultPreviewRenderedFont;
            defaultPreviewTextLabel.Text = FontPreviewText;
            if (previous != null && !ReferenceEquals(previous, defaultPreviewRenderedFont)) previous.Dispose();

            string faceName = choice.Family.Name;
            string fileName = Path.GetFileNameWithoutExtension(choice.CandidateName);
            if (!string.Equals(faceName, fileName, StringComparison.OrdinalIgnoreCase))
                faceName += " / " + fileName;

            fontPreview_richTextBox.Visible = false;
            defaultPreviewScrollPanel.Visible = true;
            defaultPreviewScrollPanel.BringToFront();
            defaultPreviewScrollPanel.AutoScrollPosition = new Point(0, 0);

            fontPreviewInfoLabel.Cursor = Cursors.Hand;
            fontPreviewInfoLabel.ForeColor = Color.MediumSpringGreen;
            fontPreviewInfoLabel.Text = "CS2 Preview (" + faceName + ") \u2022 " +
                                        currentFontScale.ToString("0.00") + "x \u2022 " +
                                        (cs2PreviewChoiceIndex + 1) + "/" + cs2PreviewChoices.Count +
                                        (currentFontScaleIsCustom ? " \u2022 Custom" : string.Empty);
        }

        private void fontPreviewInfoLabel_CycleCs2Preview(object sender, EventArgs e)
        {
            if (CurrentFormView != FormViews.Main || listBox1.SelectedItem == null ||
                listBox1.SelectedItem.ToString() != DefaultFontName) return;

            EnsureCs2PreviewChoices();
            if (cs2PreviewChoices.Count <= 1) return;

            int direction = (ModifierKeys & Keys.Shift) == Keys.Shift ? -1 : 1;
            cs2PreviewChoiceIndex = (cs2PreviewChoiceIndex + direction + cs2PreviewChoices.Count) %
                                    cs2PreviewChoices.Count;
            AppLog.Info("CS2 default preview cycled to " + (cs2PreviewChoiceIndex + 1) + "/" +
                        cs2PreviewChoices.Count + ": " + cs2PreviewChoices[cs2PreviewChoiceIndex].CandidateName);
            RefreshDefaultPreviewPolish();
        }

        private void EnsureCs2PreviewChoices()
        {
            if (Settings == null || string.IsNullOrWhiteSpace(Settings.CsgoPath)) return;
            string root = Settings.CsgoPath + Cs2FontsRelativePath;
            if (!Directory.Exists(root)) return;

            if (string.Equals(cs2PreviewChoicesRoot, root, StringComparison.OrdinalIgnoreCase) &&
                cs2PreviewChoices.Count > 0) return;

            ResetCs2PreviewChoices();
            cs2PreviewChoicesRoot = root;
            cs2PreviewTempDirectory = Path.Combine(Path.GetTempPath(),
                "FontManagerCs2Preview_" + Guid.NewGuid().ToString("N"));

            foreach (string package in Directory.GetFiles(root, "*.uifont", SearchOption.TopDirectoryOnly))
            {
                List<UiFontEmbeddedFile> embeddedFonts;
                string error;
                if (!UiFontPackageReader.TryRead(package, out embeddedFonts, out error))
                {
                    AppLog.Warn("Could not decode preview package '" + Path.GetFileName(package) + "': " + error);
                    continue;
                }

                foreach (UiFontEmbeddedFile embedded in embeddedFonts)
                {
                    cs2PreviewChoices.Add(new Cs2PreviewChoice
                    {
                        SourceLabel = Path.GetFileName(package),
                        EmbeddedFileName = embedded.FileName,
                        EmbeddedData = embedded.OpenTypeData
                    });
                }
            }

            foreach (string file in Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly))
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension != ".ttf" && extension != ".otf" && extension != ".ttc") continue;

                cs2PreviewChoices.Add(new Cs2PreviewChoice
                {
                    SourcePath = file,
                    SourceLabel = Path.GetFileName(file)
                });
            }

            try
            {
                using (FontFamily arialProbe = new FontFamily("Arial"))
                {
                    cs2PreviewChoices.Add(new Cs2PreviewChoice
                    {
                        SystemFamilyName = "Arial",
                        SourceLabel = "Windows Arial fallback"
                    });
                }
            }
            catch
            {
            }

            cs2PreviewChoices.Sort(delegate(Cs2PreviewChoice left, Cs2PreviewChoice right)
            {
                int leftPriority = GetCs2PreviewPriority(left.CandidateName);
                int rightPriority = GetCs2PreviewPriority(right.CandidateName);
                int priorityCompare = leftPriority.CompareTo(rightPriority);
                if (priorityCompare != 0) return priorityCompare;
                return string.Compare(left.CandidateName, right.CandidateName, StringComparison.OrdinalIgnoreCase);
            });

            cs2PreviewChoiceIndex = 0;
            AppLog.Info("Built CS2 preview rotation with " + cs2PreviewChoices.Count +
                        " font file/face candidates from " + root + ".");
        }

        private static int GetCs2PreviewPriority(string name)
        {
            string value = (name ?? string.Empty).ToLowerInvariant();
            bool condensed = value.Contains("condensed") || value.Contains("narrow");

            if (value.Contains("stratum2") && value.Contains("regular") && !condensed) return 0;
            if (value.Contains("stratum2") && value.Contains("medium") && !condensed) return 1;
            if (value.Contains("stratum2") && !condensed) return 2;
            if (value.Contains("stratum2")) return 3;

            if (value.Contains("notosans") && value.Contains("regular")) return 10;
            if (value.Contains("notosans")) return 11;
            if (value.Contains("notoserif") && value.Contains("regular")) return 20;
            if (value.Contains("notoserif")) return 21;
            if (value.Contains("notomono")) return 30;
            if (value.Contains("arial")) return 40;
            return 50;
        }

        private void defaultPreviewPolish_FormClosed(object sender, FormClosedEventArgs e)
        {
            ResetCs2PreviewChoices();
            if (defaultPreviewRenderedFont != null)
            {
                defaultPreviewRenderedFont.Dispose();
                defaultPreviewRenderedFont = null;
            }
        }

        private void ResetCs2PreviewChoices()
        {
            foreach (Cs2PreviewChoice choice in cs2PreviewChoices) choice.Dispose();
            cs2PreviewChoices.Clear();
            cs2PreviewChoicesRoot = null;
            cs2PreviewChoiceIndex = 0;

            if (!string.IsNullOrWhiteSpace(cs2PreviewTempDirectory))
            {
                try
                {
                    if (Directory.Exists(cs2PreviewTempDirectory))
                        Directory.Delete(cs2PreviewTempDirectory, true);
                }
                catch
                {
                }
                cs2PreviewTempDirectory = null;
            }
        }
    }
}

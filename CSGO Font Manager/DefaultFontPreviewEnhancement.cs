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
        private static readonly bool PackagedDefaultPreviewBootstrapRegistered = RegisterPackagedDefaultPreviewBootstrap();

        private bool packagedDefaultPreviewInitialized;
        private string packagedDefaultPreviewSource;
        private string packagedDefaultPreviewTempDirectory;
        private PrivateFontCollection packagedDefaultPreviewCollection;
        private FontFamily packagedDefaultPreviewFamily;

        private static bool RegisterPackagedDefaultPreviewBootstrap()
        {
            Application.Idle += BootstrapPackagedDefaultPreviewOnIdle;
            return true;
        }

        private static void BootstrapPackagedDefaultPreviewOnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.packagedDefaultPreviewInitialized) continue;
                if (!form.fontScaleUiInitialized || form.customFontScaleButton == null) continue;
                form.InitializePackagedDefaultPreview();
            }
        }

        private void InitializePackagedDefaultPreview()
        {
            if (packagedDefaultPreviewInitialized) return;
            packagedDefaultPreviewInitialized = true;

            trackBar1.Scroll += packagedDefaultPreview_RefreshLater;
            customFontScaleButton.Click += packagedDefaultPreview_RefreshLater;
            listBox1.SelectedIndexChanged += packagedDefaultPreview_RefreshLater;
            addFont_button.Click += packagedDefaultPreview_RefreshLater;
            donate_button.Click += packagedDefaultPreview_RefreshLater;
            FormClosed += packagedDefaultPreview_FormClosed;

            BeginInvoke((MethodInvoker)TryApplyPackagedDefaultPreview);
        }

        private void packagedDefaultPreview_RefreshLater(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)TryApplyPackagedDefaultPreview);
        }

        private void TryApplyPackagedDefaultPreview()
        {
            if (!fontScaleUiInitialized || CurrentFormView != FormViews.Main) return;
            if (listBox1.SelectedItem == null || listBox1.SelectedItem.ToString() != DefaultFontName) return;

            FontFamily family;
            if (!TryGetPackagedDefaultPreviewFamily(out family)) return;

            ApplyScaledPreviewFont(family, "CS2 Default (" + family.Name + ")");
        }

        private bool TryGetPackagedDefaultPreviewFamily(out FontFamily family)
        {
            family = null;
            if (Settings == null || string.IsNullOrWhiteSpace(Settings.CsgoPath)) return false;

            string root = Settings.CsgoPath + Cs2FontsRelativePath;
            if (!Directory.Exists(root)) return false;

            string source = Path.Combine(root, "stratum2.uifont");
            if (!File.Exists(source))
            {
                source = Directory.GetFiles(root, "*.uifont", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(file => Path.GetFileName(file).IndexOf("stratum", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return false;

            if (!string.Equals(packagedDefaultPreviewSource, source, StringComparison.OrdinalIgnoreCase) ||
                packagedDefaultPreviewCollection == null || packagedDefaultPreviewFamily == null)
            {
                ResetPackagedDefaultPreview();
                packagedDefaultPreviewSource = source;

                List<UiFontEmbeddedFile> embeddedFonts;
                string error;
                if (!UiFontPackageReader.TryRead(source, out embeddedFonts, out error))
                {
                    AppLog.Warn("Could not read CS2 default UI font package for preview: " + error);
                    return false;
                }

                packagedDefaultPreviewTempDirectory = Path.Combine(
                    Path.GetTempPath(), "FontManagerStratumPreview_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(packagedDefaultPreviewTempDirectory);
                packagedDefaultPreviewCollection = new PrivateFontCollection();

                int loadedFiles = 0;
                for (int i = 0; i < embeddedFonts.Count; i++)
                {
                    UiFontEmbeddedFile embedded = embeddedFonts[i];
                    string rawName = Path.GetFileName(embedded.FileName);
                    if (string.IsNullOrWhiteSpace(rawName)) rawName = "stratum2_" + i + ".ttf";

                    string safeName = i.ToString("D2") + "_" + rawName;
                    string extractedPath = Path.Combine(packagedDefaultPreviewTempDirectory, safeName);

                    try
                    {
                        File.WriteAllBytes(extractedPath, embedded.OpenTypeData);
                        packagedDefaultPreviewCollection.AddFontFile(extractedPath);
                        loadedFiles++;
                    }
                    catch (Exception exception)
                    {
                        AppLog.Warn("Could not load embedded Stratum2 preview font '" + rawName + "': " + exception.Message);
                    }
                }

                packagedDefaultPreviewFamily = packagedDefaultPreviewCollection.Families
                    .FirstOrDefault(item => string.Equals(item.Name, "Stratum2", StringComparison.OrdinalIgnoreCase))
                    ?? packagedDefaultPreviewCollection.Families
                        .FirstOrDefault(item => item.Name.IndexOf("Stratum2", StringComparison.OrdinalIgnoreCase) >= 0);

                if (packagedDefaultPreviewFamily == null)
                {
                    AppLog.Warn("CS2 UI font package was decoded, but no Stratum2 family could be loaded. Embedded files loaded: " + loadedFiles + ".");
                    ResetPackagedDefaultPreview();
                    return false;
                }

                AppLog.Info("Loaded real CS2 default preview family from " + Path.GetFileName(source) +
                            ": " + packagedDefaultPreviewFamily.Name + " (" + loadedFiles + " embedded font files)." );
            }

            family = packagedDefaultPreviewFamily;
            return family != null;
        }

        private void packagedDefaultPreview_FormClosed(object sender, FormClosedEventArgs e)
        {
            ResetPackagedDefaultPreview();
        }

        private void ResetPackagedDefaultPreview()
        {
            packagedDefaultPreviewFamily = null;

            if (packagedDefaultPreviewCollection != null)
            {
                packagedDefaultPreviewCollection.Dispose();
                packagedDefaultPreviewCollection = null;
            }

            if (!string.IsNullOrWhiteSpace(packagedDefaultPreviewTempDirectory))
            {
                try
                {
                    if (Directory.Exists(packagedDefaultPreviewTempDirectory))
                        Directory.Delete(packagedDefaultPreviewTempDirectory, true);
                }
                catch
                {
                }
                packagedDefaultPreviewTempDirectory = null;
            }
        }
    }
}

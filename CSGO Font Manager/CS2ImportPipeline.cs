using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private void fontLibrary_DragDrop_cs2Modern(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            string[] incoming = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (incoming == null || incoming.Length == 0) return;

            listBox1.Enabled = false;
            List<string> temporaryDirectories = new List<string>();
            List<string> addedFamilies = new List<string>();

            try
            {
                foreach (string incomingPath in incoming)
                {
                    try
                    {
                        string sourceFont = ResolveImportFont(incomingPath, temporaryDirectories);
                        if (string.IsNullOrWhiteSpace(sourceFont)) continue;

                        sourceFont = PrepareFontEncodingForCs2(sourceFont, temporaryDirectories);
                        if (string.IsNullOrWhiteSpace(sourceFont)) continue;

                        string familyName = GetFontFamilyNameFromFile(sourceFont);
                        if (string.IsNullOrWhiteSpace(familyName))
                        {
                            MessageBox.Show(
                                "Font Manager could not determine the internal family name for:\n\n" + Path.GetFileName(sourceFont),
                                "Font Metadata Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            AppLog.Error("Import rejected because the internal family name could not be read: " + sourceFont);
                            continue;
                        }

                        string libraryName = sanitizeFilename(familyName);
                        string libraryDirectory = Path.Combine(FontsFolder, libraryName);

                        if (Directory.Exists(libraryDirectory))
                        {
                            DialogResult overwrite = MessageBox.Show(
                                "The font '" + libraryName + "' is already in your library. Overwrite it?",
                                "Overwrite Font?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                            if (overwrite != DialogResult.Yes)
                            {
                                AppLog.Info("Import skipped because library entry already exists: " + libraryName);
                                continue;
                            }
                            Directory.Delete(libraryDirectory, true);
                        }

                        Directory.CreateDirectory(libraryDirectory);
                        string targetFileName = Path.GetFileName(sourceFont);
                        string targetFont = Path.Combine(libraryDirectory, targetFileName);
                        File.Copy(sourceFont, targetFont, true);

                        if (!InstallFont(targetFont))
                        {
                            AppLog.Error("Windows font registration failed for imported font: " + targetFont);
                            Directory.Delete(libraryDirectory, true);
                            MessageBox.Show(
                                "Windows could not register the font:\n\n" + targetFileName,
                                "Font Install Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            continue;
                        }

                        AddFont(libraryName, targetFont);
                        string fontsFile = Path.Combine(libraryDirectory, "fonts.conf");
                        setupFontsDirectory(fontsFile, familyName, targetFileName);

                        FontEncodingInfo encodingInfo = FontEncodingInspector.Inspect(targetFont);
                        AppLog.Info("Imported font: family='" + familyName + "', file='" + targetFileName +
                                    "', encoding='" + encodingInfo.EncodingDescription + "', " + encodingInfo.Detail);
                        addedFamilies.Add(familyName);
                    }
                    catch (Exception fontException)
                    {
                        AppLog.Error("One font import failed: " + incomingPath, fontException);
                        MessageBox.Show(
                            "Failed to import:\n\n" + Path.GetFileName(incomingPath) + "\n\n" + fontException.Message,
                            "Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }

                if (addedFamilies.Count > 0)
                {
                    MessageBox.Show(
                        "Success! The following font(s) were added to your library:\n---\n" +
                        string.Join(", ", addedFamilies.ToArray()),
                        "Font(s) Added!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    refreshFontList();
                }
                else
                {
                    AppLog.Info("Import completed without adding any fonts.");
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Modern font import pipeline failed.", exception);
                MessageBox.Show("Font import failed.\n\n" + exception.Message,
                    "Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                listBox1.Enabled = true;
                foreach (string directory in temporaryDirectories.Distinct())
                {
                    try
                    {
                        if (Directory.Exists(directory)) Directory.Delete(directory, true);
                    }
                    catch (Exception cleanupException)
                    {
                        AppLog.Warn("Could not clean temporary import directory " + directory + ": " + cleanupException.Message);
                    }
                }
            }
        }

        private string ResolveImportFont(string incomingPath, List<string> temporaryDirectories)
        {
            string extension = Path.GetExtension(incomingPath).ToLowerInvariant();
            if (IsFontExtension(extension)) return incomingPath;

            if (extension != ".zip")
            {
                MessageBox.Show("Unsupported file type: " + Path.GetFileName(incomingPath),
                    "Unsupported", MessageBoxButtons.OK, MessageBoxIcon.Error);
                AppLog.Warn("Unsupported import file type: " + incomingPath);
                return null;
            }

            string tempDirectory = Path.Combine(Path.GetTempPath(), "FontManagerImport_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            temporaryDirectories.Add(tempDirectory);
            ZipFile.ExtractToDirectory(Path.GetFullPath(incomingPath), tempDirectory);

            List<string> fontFiles = Directory.GetFiles(tempDirectory, "*", SearchOption.AllDirectories)
                .Where(file => IsFontExtension(Path.GetExtension(file)))
                .ToList();

            if (fontFiles.Count == 0)
            {
                MessageBox.Show("The ZIP file did not contain a supported font.",
                    "No Font Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                AppLog.Warn("ZIP import contained no supported font: " + incomingPath);
                return null;
            }

            if (fontFiles.Count == 1) return fontFiles[0];

            string list = string.Empty;
            for (int i = 0; i < fontFiles.Count; i++)
            {
                string family = GetFontFamilyNameFromFile(fontFiles[i]);
                string label = string.IsNullOrWhiteSpace(family)
                    ? Path.GetFileName(fontFiles[i])
                    : family + "  [" + Path.GetFileName(fontFiles[i]) + "]";
                list += (i + 1) + " - " + label + "\n";
            }

            string message =
                "This ZIP contains multiple fonts. Select ONE to import:\n\n" + list;
            string selected = Microsoft.VisualBasic.Interaction.InputBox(
                message, "Select Font", "1");

            int index;
            while (!(int.TryParse(selected, out index) && index >= 1 && index <= fontFiles.Count))
            {
                if (string.IsNullOrEmpty(selected)) return null;
                selected = Microsoft.VisualBasic.Interaction.InputBox(
                    message, "Select Font", "1");
            }

            return fontFiles[index - 1];
        }

        private string PrepareFontEncodingForCs2(string sourceFont, List<string> temporaryDirectories)
        {
            FontEncodingInfo encoding = FontEncodingInspector.Inspect(sourceFont);
            AppLog.Info("Encoding inspection: " + Path.GetFileName(sourceFont) + " => " +
                        encoding.EncodingDescription + "; " + encoding.Detail);

            if (!encoding.IsSupportedContainer)
            {
                AppLog.Warn("Encoding inspection unavailable for " + sourceFont + ". Import will continue using legacy compatibility behavior.");
                return sourceFont;
            }

            if (encoding.IsUnicode)
            {
                if (encoding.BasicLatinCoverage < 20)
                {
                    AppLog.Warn("Unicode font has very low Basic Latin coverage (" + encoding.BasicLatinCoverage + "/95): " + sourceFont);
                }
                return sourceFont;
            }

            if (encoding.CanAutoConvertSymbolToUnicode)
            {
                DialogResult choice = MessageBox.Show(
                    "Font: " + Path.GetFileName(sourceFont) + "\n" +
                    "Encoding: " + encoding.EncodingDescription + "\n\n" +
                    "Counter-Strike 2 needs a Unicode-encoded font and may revert a non-Unicode font.\n\n" +
                    "Do you want to re-encode a copy to Unicode BMP?\n\n" +
                    "Yes = Re-encode copy\nNo = Import anyway\nCancel = Skip",
                    "Non-Unicode Font Detected", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

                if (choice == DialogResult.Cancel)
                {
                    AppLog.Info("Non-Unicode import cancelled by user: " + sourceFont);
                    return null;
                }
                if (choice == DialogResult.No)
                {
                    AppLog.Warn("Non-Unicode font imported without conversion by user choice: " + sourceFont);
                    return sourceFont;
                }

                string tempDirectory = Path.Combine(Path.GetTempPath(), "FontManagerUnicode_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);
                temporaryDirectories.Add(tempDirectory);
                string converted = Path.Combine(tempDirectory,
                    Path.GetFileNameWithoutExtension(sourceFont) + "_Unicode" + Path.GetExtension(sourceFont));
                string error;
                if (!FontEncodingInspector.TryCreateUnicodeBmpCopy(sourceFont, converted, out error))
                {
                    AppLog.Error("Unicode conversion failed for " + sourceFont + ": " + error);
                    MessageBox.Show("The Unicode copy could not be created.\n\n" + error,
                        "Re-encode Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }

                FontEncodingInfo convertedInfo = FontEncodingInspector.Inspect(converted);
                AppLog.Info("Unicode copy created: " + converted + " => " +
                            convertedInfo.EncodingDescription + "; " + convertedInfo.Detail);
                return converted;
            }

            DialogResult importAnyway = MessageBox.Show(
                "Font: " + Path.GetFileName(sourceFont) + "\n" +
                "Encoding: " + encoding.EncodingDescription + "\n\n" +
                "Counter-Strike 2 needs a Unicode-encoded font and may revert this font.\n" +
                "Automatic conversion is not available for this encoding yet.\n\n" +
                "Import it anyway?",
                "Non-Unicode Font Detected", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (importAnyway == DialogResult.Yes)
            {
                AppLog.Warn("Unsupported legacy encoding imported by user choice: " + sourceFont);
                return sourceFont;
            }

            AppLog.Info("Legacy font import skipped: " + sourceFont);
            return null;
        }
    }
}

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private const string Cs2FontsRelativePath = @"\game\csgo\panorama\fonts";
        private const string Cs2CoreInclude = "<include>../../../core/panorama/fonts/conf.d</include>";
        private const string ManagedFontBaseName = "fontmanager_custom";

        private static string Cs2BackupConfigPath => DataPath + "fonts.conf.cs2.original";
        private static string Cs2GeneratedConfigPath => DataPath + "fonts.conf.cs2.generated";

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (Settings != null && !string.IsNullOrWhiteSpace(Settings.CsgoPath))
                CsgoFontsFolder = Settings.CsgoPath + Cs2FontsRelativePath;

            apply_button.Click -= apply_button_Click;
            apply_button.Click += apply_button_cs2_Click;

            if (CurrentFormView == FormViews.Main)
                title_label.Text = "CS2 Fonts";
        }

        private void apply_button_cs2_Click(object sender, EventArgs e)
        {
            if (CurrentFormView != FormViews.Main)
            {
                apply_button_Click(sender, e);
                return;
            }

            if (listBox1.SelectedItem == null)
            {
                MessageBox.Show("Please select a font first.", "No Font Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (Settings == null || string.IsNullOrWhiteSpace(Settings.CsgoPath))
            {
                MessageBox.Show("The Counter-Strike 2 folder is unknown. Restart Font Manager and select your CS2 install folder.",
                    "No CS2 Folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            CsgoFontsFolder = Settings.CsgoPath + Cs2FontsRelativePath;
            string gameFontsConf = Path.Combine(CsgoFontsFolder, "fonts.conf");

            if (!File.Exists(gameFontsConf))
            {
                MessageBox.Show("Modern CS2 fonts.conf was not found at:\n\n" + gameFontsConf +
                                "\n\nMake sure Font Manager is pointed at the Counter-Strike Global Offensive install root.",
                    "fonts.conf Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string selectedName = listBox1.SelectedItem.ToString();
            bool resetToDefault = selectedName.Equals(DefaultFontName);
            string question = resetToDefault
                ? "Do you want to reset to the default font for CS2?"
                : "Do you want to apply " + selectedName + " to CS2?";

            if (MessageBox.Show(question, "Are you sure?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                Directory.CreateDirectory(CsgoFontsFolder);
                RemoveManagedFontFiles();

                if (resetToDefault)
                {
                    RestoreDefaultCs2Config(gameFontsConf);
                    Settings.ActiveFont = DefaultFontName;
                    MessageBox.Show("Successfully reset to the default CS2 font!", "Default Font Applied!",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string libraryFolder = Path.Combine(FontsFolder, selectedName);
                string fontFilePath = Directory.GetFiles(libraryFolder).FirstOrDefault(file => IsFontExtension(Path.GetExtension(file)));
                if (fontFilePath == null)
                    throw new FileNotFoundException("The selected font file could not be found.");

                FontFamily fontFamily = GetFontFamilyByName(selectedName);
                if (fontFamily == null)
                    throw new InvalidOperationException("The font family name could not be determined for " + selectedName + ".");

                string currentConfig = File.ReadAllText(gameFontsConf);
                string baseConfig = GetCurrentCs2BaseConfig(currentConfig);
                float pixelSize = getCSGOPixelSize();
                string generatedConfig = BuildModernCs2Config(baseConfig, fontFamily.Name, pixelSize);

                if (!IsWellFormedXml(generatedConfig))
                    throw new InvalidDataException("The generated fonts.conf is not valid XML. No game files were changed.");

                string managedFontPath = Path.Combine(CsgoFontsFolder, ManagedFontBaseName + Path.GetExtension(fontFilePath).ToLowerInvariant());
                File.Copy(fontFilePath, managedFontPath, true);
                File.WriteAllText(gameFontsConf, generatedConfig, new UTF8Encoding(false));
                File.WriteAllText(Cs2GeneratedConfigPath, generatedConfig, new UTF8Encoding(false));

                Settings.ActiveFont = selectedName;

                bool cs2IsRunning = System.Diagnostics.Process.GetProcessesByName("cs2").Length != 0;
                MessageBox.Show("Successfully applied " + selectedName + "!" +
                                (cs2IsRunning ? "\n\nRestart CS2 for the font to take effect." : ""),
                    "Font Applied!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show("Failed to apply the font.\n\n" + exception.Message,
                    "Failed to Apply", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string GetCurrentCs2BaseConfig(string currentConfig)
        {
            if (!currentConfig.Contains(Cs2CoreInclude))
                throw new InvalidDataException(
                    "This fonts.conf does not look like the modern CS2 layout. Restore/verify the game's fonts.conf once, then try again.");

            if (File.Exists(Cs2GeneratedConfigPath) && File.Exists(Cs2BackupConfigPath))
            {
                string previousGenerated = File.ReadAllText(Cs2GeneratedConfigPath);
                if (currentConfig == previousGenerated)
                    return File.ReadAllText(Cs2BackupConfigPath);
            }

            string cleanCurrent = StripManagedOverride(currentConfig);
            File.WriteAllText(Cs2BackupConfigPath, cleanCurrent, new UTF8Encoding(false));
            return cleanCurrent;
        }

        private static string BuildModernCs2Config(string baseConfig, string fontFamily, float pixelSize)
        {
            string config = StripManagedOverride(baseConfig);

            config = Regex.Replace(config,
                "<dir\\s+prefix=\"default\">\\.\\./\\.\\./csgo/panorama/fonts</dir>",
                "<dir prefix=\"cwd\">../../csgo/panorama/fonts</dir>");

            config = Regex.Replace(config,
                "^[ \\t]*<fontpattern>.*?</fontpattern>[ \\t]*(?:\\r?\\n)?",
                string.Empty,
                RegexOptions.Multiline);

            string escapedFamily = SecurityElement.Escape(fontFamily);
            string scale = pixelSize.ToString("0.00", CultureInfo.InvariantCulture);
            string overrideBlock =
                "\t<match target=\"pattern\">\n" +
                "\t\t<edit name=\"family\" mode=\"assign\" binding=\"strong\">\n" +
                "\t\t\t<string>" + escapedFamily + "</string>\n" +
                "\t\t</edit>\n" +
                "\t\t<edit name=\"pixelsize\" mode=\"assign\">\n" +
                "\t\t\t<times>\n" +
                "\t\t\t\t<name>pixelsize</name>\n" +
                "\t\t\t\t<double>" + scale + "</double>\n" +
                "\t\t\t</times>\n" +
                "\t\t</edit>\n" +
                "\t</match>\n\n";

            int includeIndex = config.IndexOf(Cs2CoreInclude, StringComparison.Ordinal);
            if (includeIndex < 0)
                throw new InvalidDataException("The modern CS2 core font include could not be found.");

            int lineStart = config.LastIndexOf('\n', includeIndex);
            int insertIndex = lineStart >= 0 ? lineStart + 1 : includeIndex;
            return config.Insert(insertIndex, overrideBlock);
        }

        private static string StripManagedOverride(string config)
        {
            string pattern =
                "[ \\t]*<match\\s+target=\"pattern\">\\s*" +
                "<edit\\s+name=\"family\"\\s+mode=\"assign\"\\s+binding=\"strong\">\\s*" +
                "<string>[^<]+</string>\\s*</edit>\\s*" +
                "(?:<edit\\s+name=\"pixelsize\"\\s+mode=\"assign\">.*?</edit>\\s*)?" +
                "</match>\\s*(?=[ \\t]*<include>\\.\\./\\.\\./\\.\\./core/panorama/fonts/conf\\.d</include>)";

            return Regex.Replace(config, pattern, string.Empty, RegexOptions.Singleline);
        }

        private static void RestoreDefaultCs2Config(string gameFontsConf)
        {
            string currentConfig = File.ReadAllText(gameFontsConf);

            if (File.Exists(Cs2GeneratedConfigPath) && File.Exists(Cs2BackupConfigPath) &&
                currentConfig == File.ReadAllText(Cs2GeneratedConfigPath))
            {
                File.Copy(Cs2BackupConfigPath, gameFontsConf, true);
            }
            else
            {
                string cleaned = StripManagedOverride(currentConfig);
                if (cleaned != currentConfig)
                    File.WriteAllText(gameFontsConf, cleaned, new UTF8Encoding(false));
            }

            if (File.Exists(Cs2GeneratedConfigPath)) File.Delete(Cs2GeneratedConfigPath);
            if (File.Exists(Cs2BackupConfigPath)) File.Delete(Cs2BackupConfigPath);
            RemoveManagedFontFiles();
        }

        private static void RemoveManagedFontFiles()
        {
            if (string.IsNullOrWhiteSpace(CsgoFontsFolder) || !Directory.Exists(CsgoFontsFolder))
                return;

            foreach (string file in Directory.GetFiles(CsgoFontsFolder, ManagedFontBaseName + ".*"))
            {
                if (IsFontExtension(Path.GetExtension(file)))
                    File.Delete(file);
            }
        }

        private static bool IsWellFormedXml(string xml)
        {
            XmlReaderSettings settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            };

            using (StringReader stringReader = new StringReader(xml))
            using (XmlReader reader = XmlReader.Create(stringReader, settings))
            {
                while (reader.Read()) { }
            }

            return true;
        }
    }
}

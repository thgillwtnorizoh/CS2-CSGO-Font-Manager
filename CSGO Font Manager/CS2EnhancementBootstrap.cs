using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private const string DefaultSizeOverrideMarker = "<!-- Font Manager default-size override -->";
        private static readonly bool Cs2EnhancementBootstrapRegistered = RegisterCs2EnhancementBootstrap();

        private static bool RegisterCs2EnhancementBootstrap()
        {
            Application.Idle += BootstrapCs2EnhancementsOnIdle;
            return true;
        }

        private static void BootstrapCs2EnhancementsOnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.cs2EnhancementsInitialized) continue;

                form.InitializeCs2Enhancements();

                // OnLoad has already installed the proven CS2 apply handler. Wrap it only after that point.
                form.apply_button.Click -= form.apply_button_cs2_Click;
                form.apply_button.Click += form.apply_button_cs2_enhanced_Click;

                // Replace the compatibility guard with the metadata-first runtime importer.
                form.listBox1.DragDrop -= form.fontLibrary_DragDrop_cs2Guard;
                form.listBox1.DragDrop += form.fontLibrary_DragDrop_cs2Modern;

                AppLog.Info("Enhanced CS2 apply wrapper and modern import pipeline installed.");
            }
        }

        private void apply_button_cs2_enhanced_Click(object sender, EventArgs e)
        {
            string selection = listBox1.SelectedItem == null ? "<none>" : listBox1.SelectedItem.ToString();
            AppLog.Info("Apply button pressed. View=" + CurrentFormView + ", selection=" + selection +
                        ", size=" + getCSGOPixelSize().ToString("0.00", CultureInfo.InvariantCulture));

            if (CurrentFormView != FormViews.Main || listBox1.SelectedItem == null ||
                listBox1.SelectedItem.ToString() != DefaultFontName)
            {
                apply_button_cs2_Click(sender, e);
                return;
            }

            ApplyDefaultFontWithSize();
        }

        private void ApplyDefaultFontWithSize()
        {
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
                AppLog.Error("Default-size apply could not find fonts.conf: " + gameFontsConf);
                MessageBox.Show("Modern CS2 fonts.conf was not found at:\n\n" + gameFontsConf,
                    "fonts.conf Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            float scale = getCSGOPixelSize();
            bool stockScale = Math.Abs(scale - 1.0f) < 0.0001f;
            string question = stockScale
                ? "Restore the stock CS2 font and stock font size?"
                : "Apply the default CS2 font at " + scale.ToString("0.00", CultureInfo.InvariantCulture) + "x size?";

            if (MessageBox.Show(question, "Apply Default Font", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                if (stockScale)
                {
                    RestoreDefaultFontAndSize(gameFontsConf);
                    Settings.ActiveFont = DefaultFontName;
                    AppLog.Info("Restored stock CS2 font and size.");
                    ShowDefaultSizeResult("Stock CS2 font and size restored.");
                    return;
                }

                string currentConfig = File.ReadAllText(gameFontsConf);
                string baseConfig = GetCurrentCs2BaseConfig(currentConfig);
                baseConfig = StripDefaultSizeOnlyOverride(baseConfig);
                string generated = BuildDefaultSizeOnlyConfig(baseConfig, scale);

                if (!IsWellFormedXml(generated))
                    throw new InvalidDataException("The generated size-only fonts.conf is not valid XML.");

                string tempConfig = gameFontsConf + ".fontmanager.tmp";
                File.WriteAllText(tempConfig, generated, new UTF8Encoding(false));
                File.Copy(tempConfig, gameFontsConf, true);
                File.Delete(tempConfig);
                File.WriteAllText(Cs2GeneratedConfigPath, generated, new UTF8Encoding(false));
                RemoveManagedFontFiles();

                Settings.ActiveFont = DefaultFontName;
                AppLog.Info("Applied default CS2 font size multiplier " +
                            scale.ToString("0.00", CultureInfo.InvariantCulture) + ".");
                ShowDefaultSizeResult("Default CS2 font size applied at " +
                                      scale.ToString("0.00", CultureInfo.InvariantCulture) + "x.");
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to apply default CS2 font size.", exception);
                MessageBox.Show("Failed to apply the default font size.\n\n" + exception.Message,
                    "Apply Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string BuildDefaultSizeOnlyConfig(string baseConfig, float scale)
        {
            string config = StripDefaultSizeOnlyOverride(baseConfig);
            string scaleText = scale.ToString("0.00", CultureInfo.InvariantCulture);
            string block =
                "\t" + DefaultSizeOverrideMarker + "\n" +
                "\t<match target=\"pattern\">\n" +
                "\t\t<edit name=\"pixelsize\" mode=\"assign\">\n" +
                "\t\t\t<times>\n" +
                "\t\t\t\t<name>pixelsize</name>\n" +
                "\t\t\t\t<double>" + scaleText + "</double>\n" +
                "\t\t\t</times>\n" +
                "\t\t</edit>\n" +
                "\t</match>\n\n";

            int includeIndex = config.IndexOf(Cs2CoreInclude, StringComparison.Ordinal);
            if (includeIndex < 0)
                throw new InvalidDataException("The modern CS2 core font include could not be found.");

            int lineStart = config.LastIndexOf('\n', includeIndex);
            int insertIndex = lineStart >= 0 ? lineStart + 1 : includeIndex;
            return config.Insert(insertIndex, block);
        }

        private static string StripDefaultSizeOnlyOverride(string config)
        {
            string pattern =
                "[ \\t]*" + Regex.Escape(DefaultSizeOverrideMarker) + "\\s*" +
                "<match\\s+target=\"pattern\">.*?</match>\\s*" +
                "(?=[ \\t]*<include>\\.\\./\\.\\./\\.\\./core/panorama/fonts/conf\\.d</include>)";
            return Regex.Replace(config, pattern, string.Empty, RegexOptions.Singleline);
        }

        private static void RestoreDefaultFontAndSize(string gameFontsConf)
        {
            string currentConfig = File.ReadAllText(gameFontsConf);

            if (File.Exists(Cs2GeneratedConfigPath) && File.Exists(Cs2BackupConfigPath) &&
                currentConfig == File.ReadAllText(Cs2GeneratedConfigPath))
            {
                File.Copy(Cs2BackupConfigPath, gameFontsConf, true);
            }
            else
            {
                string cleaned = StripDefaultSizeOnlyOverride(currentConfig);
                cleaned = StripManagedOverride(cleaned);
                if (cleaned != currentConfig)
                    File.WriteAllText(gameFontsConf, cleaned, new UTF8Encoding(false));
            }

            if (File.Exists(Cs2GeneratedConfigPath)) File.Delete(Cs2GeneratedConfigPath);
            if (File.Exists(Cs2BackupConfigPath)) File.Delete(Cs2BackupConfigPath);
            RemoveManagedFontFiles();
        }

        private static void ShowDefaultSizeResult(string message)
        {
            bool running = IsCs2Running();
            MessageBox.Show(message +
                            (running ? "\n\nCS2 is running. Use the Restart CS2 button to load this change." : ""),
                "Default Font", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}

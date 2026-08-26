using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    /// <summary>
    /// Owns legacy CS:GO fonts.conf generation, backup/restore, and managed font files.
    /// CS2 deliberately does not use this backend or manage a local conf.d directory.
    /// </summary>
    public partial class Form1
    {
        // Persistent ownership markers, not release-version strings. Keeping 4.0 allows all 4.0.x
        // builds to recognize and clean blocks written by earlier releases in the same config format.
        private const string CSGO_BEGIN = "<!-- Font Manager 4.0 CS:GO begin -->";
        private const string CSGO_END = "<!-- Font Manager 4.0 CS:GO end -->";
        private const string CSGO_PATTERN_BEGIN = "<!-- Font Manager 4.0 CS:GO patterns begin -->";
        private const string CSGO_PATTERN_END = "<!-- Font Manager 4.0 CS:GO patterns end -->";

        private static string CsgoBackup => DataPath + "fonts.conf.csgo.original";
        private static string CsgoGenerated => DataPath + "fonts.conf.csgo.generated";

        private void ApplyCsgoGeneral()
        {
            if (listBox1.SelectedItem == null)
            {
                MessageBox.Show("Please select a font first.");
                return;
            }
            if (Settings == null || !ValidCsgo(Settings.LegacyCsgoPath))
            {
                MessageBox.Show("CS:GO path is unknown. Use Setting first.");
                return;
            }

            string directory = Path.Combine(Settings.LegacyCsgoPath, "csgo", "panorama", "fonts");
            string configPath = Path.Combine(directory, "fonts.conf");
            string selection = listBox1.SelectedItem.ToString();
            float scale = GetCurrentFontScale();
            bool useDefault = selection == DefaultFontName;

            string question = (useDefault ? "Apply the CS:GO default font" : "Apply " + selection + " to all CS:GO fonts") +
                              " at " + scale.ToString("0.00", CultureInfo.InvariantCulture) + "x?";
            if (MessageBox.Show(question, "Apply to CS:GO", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;

            try
            {
                if (useDefault && Math.Abs(scale - 1f) < .0001f)
                {
                    RestoreCsgo(configPath, directory);
                    Settings.ActiveFont = DefaultFontName;
                    SaveNow();
                    CsgoDone("Stock CS:GO font configuration restored.");
                    return;
                }

                string family = null;
                string pattern = null;
                CleanCsgoFonts(directory);
                string baseConfig = CsgoBase(File.ReadAllText(configPath));

                if (!useDefault)
                {
                    string sourcePath;
                    string actualFamily;
                    if (!TryFindImportedFont(selection, out sourcePath, out actualFamily))
                        throw new FileNotFoundException("Imported font not found: " + selection);

                    string managedName = "fontmanager_csgo_custom" + Path.GetExtension(sourcePath).ToLowerInvariant();
                    File.Copy(sourcePath, Path.Combine(directory, managedName), true);
                    family = actualFamily;
                    pattern = Path.GetFileNameWithoutExtension(managedName);
                }

                string generated = BuildCsgo(baseConfig, family,
                    pattern == null ? new string[0] : new[] { pattern }, null, scale);
                if (!IsWellFormedXml(generated))
                    throw new InvalidDataException("Generated CS:GO fonts.conf is invalid XML.");

                WriteCsgo(configPath, generated);
                Settings.ActiveFont = selection;
                SaveNow();
                CsgoDone("CS:GO font setting applied successfully.");
            }
            catch (Exception exception)
            {
                AppLog.Error("CS:GO general apply failed.", exception);
                MessageBox.Show("CS:GO apply failed.\n\n" + exception.Message);
            }
        }

        private void CsgoDone(string message)
        {
            MessageBox.Show(message +
                            (IsCsgoRunning() ? "\n\nCS:GO is running. Use Restart CS:GO to load the change." : ""),
                "CS:GO Font");
        }

        private static string CsgoBase(string current)
        {
            if (File.Exists(CsgoGenerated) && File.Exists(CsgoBackup) && current == File.ReadAllText(CsgoGenerated))
                return File.ReadAllText(CsgoBackup);

            string clean = StripCsgo(current);
            File.WriteAllText(CsgoBackup, clean, new UTF8Encoding(false));
            return clean;
        }

        private static string StripCsgo(string config)
        {
            config = Regex.Replace(config,
                "[ \\t]*" + Regex.Escape(CSGO_PATTERN_BEGIN) + ".*?" + Regex.Escape(CSGO_PATTERN_END) + "\\s*",
                "", RegexOptions.Singleline);
            return Regex.Replace(config,
                "[ \\t]*" + Regex.Escape(CSGO_BEGIN) + ".*?" + Regex.Escape(CSGO_END) + "\\s*",
                "", RegexOptions.Singleline);
        }

        private static string BuildCsgo(string baseConfig, string globalFamily, IEnumerable<string> patterns,
            Dictionary<string, string> familyMap, float scale)
        {
            string config = StripCsgo(baseConfig);
            List<string> patternList = patterns.Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (patternList.Count > 0)
            {
                StringBuilder patternBlock = new StringBuilder("\t" + CSGO_PATTERN_BEGIN + "\n");
                foreach (string pattern in patternList)
                    patternBlock.Append("\t<fontpattern>").Append(SecurityElement.Escape(pattern)).Append("</fontpattern>\n");
                patternBlock.Append("\t" + CSGO_PATTERN_END + "\n");

                int cacheIndex = config.IndexOf("<cachedir>", StringComparison.OrdinalIgnoreCase);
                int insertIndex = cacheIndex < 0
                    ? config.LastIndexOf("</fontconfig>", StringComparison.OrdinalIgnoreCase)
                    : config.LastIndexOf('\n', cacheIndex) + 1;
                config = config.Insert(insertIndex, patternBlock.ToString());
            }

            StringBuilder block = new StringBuilder("\t" + CSGO_BEGIN + "\n");
            if (globalFamily != null)
            {
                block.Append("\t<match target=\"pattern\"><edit name=\"family\" mode=\"assign\" binding=\"strong\"><string>")
                    .Append(SecurityElement.Escape(globalFamily)).Append("</string></edit>");
                AddScale(block, scale);
                block.Append("</match>\n");
            }

            if (familyMap != null)
            {
                foreach (KeyValuePair<string, string> mapping in familyMap)
                {
                    if (mapping.Value == null) continue;
                    block.Append("\t<match target=\"pattern\"><test name=\"family\" compare=\"eq\" qual=\"any\"><string>")
                        .Append(SecurityElement.Escape(mapping.Key))
                        .Append("</string></test><edit name=\"family\" mode=\"assign\" binding=\"strong\"><string>")
                        .Append(SecurityElement.Escape(mapping.Value))
                        .Append("</string></edit></match>\n");
                }
            }

            if (globalFamily == null && Math.Abs(scale - 1f) >= .0001f)
            {
                block.Append("\t<match target=\"pattern\">");
                AddScale(block, scale);
                block.Append("</match>\n");
            }

            block.Append("\t" + CSGO_END + "\n");
            int end = config.LastIndexOf("</fontconfig>", StringComparison.OrdinalIgnoreCase);
            if (end < 0)
                throw new InvalidDataException("Legacy fonts.conf has no </fontconfig>.");
            return config.Insert(end, block.ToString());
        }

        private static void AddScale(StringBuilder builder, float scale)
        {
            if (Math.Abs(scale - 1f) < .0001f) return;
            builder.Append("<edit name=\"pixelsize\" mode=\"assign\"><times><name>pixelsize</name><double>")
                .Append(scale.ToString("0.00", CultureInfo.InvariantCulture))
                .Append("</double></times></edit>");
        }

        private static void WriteCsgo(string path, string generated)
        {
            string temporary = path + ".fontmanager.tmp";
            File.WriteAllText(temporary, generated, new UTF8Encoding(false));
            File.Copy(temporary, path, true);
            File.Delete(temporary);
            File.WriteAllText(CsgoGenerated, generated, new UTF8Encoding(false));
        }

        private static void RestoreCsgo(string configPath, string directory)
        {
            string current = File.ReadAllText(configPath);
            if (File.Exists(CsgoGenerated) && File.Exists(CsgoBackup) && current == File.ReadAllText(CsgoGenerated))
                File.Copy(CsgoBackup, configPath, true);
            else
            {
                string clean = StripCsgo(current);
                if (clean != current)
                    File.WriteAllText(configPath, clean, new UTF8Encoding(false));
            }

            if (File.Exists(CsgoGenerated)) File.Delete(CsgoGenerated);
            if (File.Exists(CsgoBackup)) File.Delete(CsgoBackup);
            CleanCsgoFonts(directory);
        }

        private static void CleanCsgoFonts(string directory)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory, "fontmanager_csgo_*.*"))
            {
                if (!IsFontExtension(Path.GetExtension(file))) continue;
                try { File.Delete(file); } catch { }
            }
        }
    }
}

using System;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private const string ReleaseVersion401 = "4.0.1";
        private static readonly bool Version401BootstrapRegistered = RegisterVersion401Bootstrap();
        private bool version401Installed;

        private static bool RegisterVersion401Bootstrap()
        {
            Application.Idle += Version401OnIdle;
            return true;
        }

        private static void Version401OnIdle(object sender, EventArgs e)
        {
            VersionNumber = ReleaseVersion401;

            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.IsDisposed || form.version_label == null)
                    continue;

                if (!form.version401Installed)
                    form.InstallVersion401Identity();

                form.ApplyVersion401Identity();
            }
        }

        private void InstallVersion401Identity()
        {
            if (version401Installed) return;
            version401Installed = true;

            // Some older dual-game code still writes the 4.0 prototype label from a
            // process timer. Keep the release identity authoritative without changing
            // the 4.0 config ownership markers used to clean existing CS:GO blocks.
            version_label.TextChanged += Version401Label_TextChanged;
            ApplyVersion401Identity();
            AppLog.Info("Font Manager release identity set to 4.0.1.");
        }

        private void Version401Label_TextChanged(object sender, EventArgs e)
        {
            ApplyVersion401Identity();
        }

        private void ApplyVersion401Identity()
        {
            VersionNumber = ReleaseVersion401;
            string expected = "Version " + ReleaseVersion401;
            if (!string.Equals(version_label.Text, expected, StringComparison.Ordinal))
                version_label.Text = expected;
        }
    }
}

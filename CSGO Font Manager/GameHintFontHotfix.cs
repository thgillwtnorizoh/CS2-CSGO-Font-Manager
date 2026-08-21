using System;
using System.Drawing;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private static readonly bool GameHintFontHotfixBootstrapRegistered = RegisterGameHintFontHotfixBootstrap();
        private bool gameHintFontHotfixApplied;

        private static bool RegisterGameHintFontHotfixBootstrap()
        {
            Application.Idle += GameHintFontHotfixOnIdle;
            return true;
        }

        private static void GameHintFontHotfixOnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.IsDisposed || form.gameHintFontHotfixApplied)
                    continue;
                if (!form.dualReady || form.gameHint == null)
                    continue;

                form.gameHintFontHotfixApplied = true;
                Font learningCurve = new Font("Learning Curve", 9f, FontStyle.Regular);
                if (string.Equals(learningCurve.Name, "Learning Curve", StringComparison.OrdinalIgnoreCase))
                {
                    Font oldFont = form.gameHint.Font;
                    form.gameHint.Font = learningCurve;
                    if (oldFont != null) oldFont.Dispose();
                    AppLog.Info("Game-switch hint font set to Learning Curve.");
                }
                else
                {
                    learningCurve.Dispose();
                    AppLog.Warn("Learning Curve is not installed; game-switch hint kept its fallback font.");
                }

                form.LayoutDualBits();
            }
        }
    }
}

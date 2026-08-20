using System;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private static readonly bool SpecificPreviewBridgeRegistered = RegisterSpecificPreviewBridge();
        private bool specificPreviewBridgeLastState;

        private static bool RegisterSpecificPreviewBridge()
        {
            Application.Idle += SpecificPreviewBridgeOnIdle;
            return true;
        }

        private static void SpecificPreviewBridgeOnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.IsDisposed || !form.specificSettingsUiInitialized) continue;

                bool specific = form.CurrentFormView == FormViews.Main && form.specificSettingsTabActive;
                if (specific == form.specificPreviewBridgeLastState) continue;
                form.specificPreviewBridgeLastState = specific;

                if (specific)
                {
                    if (form.defaultPreviewScrollPanel != null)
                        form.defaultPreviewScrollPanel.Visible = false;
                }
                else if (form.defaultPreviewPolishInitialized)
                {
                    form.RefreshDefaultPreviewPolish();
                }
            }
        }
    }
}

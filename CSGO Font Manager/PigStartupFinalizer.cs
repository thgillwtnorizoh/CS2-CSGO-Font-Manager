using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private static readonly bool PigStartupFinalizerBootstrapRegistered = RegisterPigStartupFinalizerBootstrap();

        private bool pigStartupFinalizerQueued;
        private bool pigStartupFinalized;

        private static bool RegisterPigStartupFinalizerBootstrap()
        {
            Application.Idle += BootstrapPigStartupFinalizerOnIdle;
            return true;
        }

        private static void BootstrapPigStartupFinalizerOnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.IsDisposed || form.pigStartupFinalized || form.pigStartupFinalizerQueued)
                    continue;
                if (!form.pigUiV2Initialized || !form.IsHandleCreated)
                    continue;

                form.QueuePigStartupFinalizer();
            }
        }

        private void QueuePigStartupFinalizer()
        {
            if (pigStartupFinalizerQueued || pigStartupFinalized || IsDisposed || !IsHandleCreated)
                return;

            pigStartupFinalizerQueued = true;
            InstallCurrentRepositoryLinks();

            // The older startup modules use nested BeginInvoke calls. Queue two layers of our own
            // so their pending first-frame geometry work drains before the authoritative layout runs.
            BeginInvoke((MethodInvoker)delegate
            {
                if (IsDisposed) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed) return;
                    BeginInvoke((MethodInvoker)FinalizePigStartupUi);
                });
            });
        }

        private void FinalizePigStartupUi()
        {
            if (pigStartupFinalized || IsDisposed || !IsHandleCreated) return;
            pigStartupFinalized = true;
            pigStartupFinalizerQueued = false;

            // Reproduce the resize handshake that previously happened only after the user touched
            // the window. The +1 pixel step is not painted; it simply makes WinForms/Windows commit
            // the final client geometry, then the V2 layout writes the intended bounds once more.
            Size intendedClientSize = ClientSize;
            SuspendLayout();
            try
            {
                SetClientSizeCore(intendedClientSize.Width + 1, intendedClientSize.Height);
                SetClientSizeCore(intendedClientSize.Width, intendedClientSize.Height);
                ApplyAuthoritativePigLayout();
                PerformLayout();
            }
            finally
            {
                ResumeLayout(true);
            }

            Invalidate(true);
            Update();

            AppLog.Info("Pig startup finalizer completed at " + ClientSize.Width + "x" +
                        ClientSize.Height + "; synthetic resize handshake applied.");
        }

        private void InstallCurrentRepositoryLinks()
        {
            linkLabel1.LinkClicked -= linkLabel1_LinkClicked;
            linkLabel3.LinkClicked -= linkLabel3_LinkClicked;
            linkLabel1.LinkClicked -= pigAboutLink_LinkClicked;
            linkLabel3.LinkClicked -= pigFeedbackLink_LinkClicked;
            linkLabel1.LinkClicked += pigAboutLink_LinkClicked;
            linkLabel3.LinkClicked += pigFeedbackLink_LinkClicked;
        }

        private void pigAboutLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            OpenPigRepositoryUrl("https://github.com/thgillwtnorizoh/Font-Manager/blob/master/README.md#introduction");
        }

        private void pigFeedbackLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            OpenPigRepositoryUrl("https://github.com/thgillwtnorizoh/Font-Manager/issues");
        }

        private void OpenPigRepositoryUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                AppLog.Error("Could not open repository link: " + url, exception);
                MessageBox.Show("Could not open the link.\n\n" + url,
                    "Open Link Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

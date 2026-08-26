using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private bool startupLayoutFinalizerQueued;
        private bool startupLayoutFinalized;

        private void QueueStartupLayoutFinalization()
        {
            if (startupLayoutFinalizerQueued || startupLayoutFinalized || IsDisposed || !IsHandleCreated)
                return;

            startupLayoutFinalizerQueued = true;
            InstallCurrentRepositoryLinks();

            // Earlier UI modules may still have BeginInvoke work queued from their own initialization.
            // Queue behind them, then apply one authoritative final geometry pass.
            BeginInvoke((MethodInvoker)delegate
            {
                if (IsDisposed) return;
                BeginInvoke((MethodInvoker)FinalizeStartupLayout);
            });
        }

        private void FinalizeStartupLayout()
        {
            if (startupLayoutFinalized || IsDisposed || !IsHandleCreated)
                return;

            startupLayoutFinalized = true;
            startupLayoutFinalizerQueued = false;

            Size intendedClientSize = ClientSize;
            SuspendLayout();
            try
            {
                // WinForms sometimes commits the first client geometry only after a real resize.
                // A one-pixel invisible handshake reproduces that commit, then restores the exact size.
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

            AppLog.Info("Startup layout finalized at " + ClientSize.Width + "x" + ClientSize.Height +
                        "; synthetic resize handshake applied once.");
        }

        private void InstallCurrentRepositoryLinks()
        {
            linkLabel1.LinkClicked -= linkLabel1_LinkClicked;
            linkLabel3.LinkClicked -= linkLabel3_LinkClicked;
            linkLabel1.LinkClicked -= CurrentRepositoryAbout_LinkClicked;
            linkLabel3.LinkClicked -= CurrentRepositoryFeedback_LinkClicked;
            linkLabel1.LinkClicked += CurrentRepositoryAbout_LinkClicked;
            linkLabel3.LinkClicked += CurrentRepositoryFeedback_LinkClicked;
        }

        private void CurrentRepositoryAbout_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            OpenRepositoryUrl(ReleaseInfo.RepositoryUrl + "/blob/master/README.md#introduction");
        }

        private void CurrentRepositoryFeedback_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            OpenRepositoryUrl(ReleaseInfo.RepositoryUrl + "/issues");
        }

        private void OpenRepositoryUrl(string url)
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

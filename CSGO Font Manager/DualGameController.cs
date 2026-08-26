using System;
using System.Drawing;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    /// <summary>
    /// Coordinates the shared dual-game UI and routes actions to the game-specific subsystems.
    /// Path detection, CS:GO config generation, Specific Settings, and restart behavior live in
    /// their own partials so this class remains an orchestration layer rather than a god-object.
    /// </summary>
    public partial class Form1
    {
        private enum GameTarget { CS2, CSGO }

        private GameTarget gameTarget;
        private bool dualReady;
        private Label gameHint;
        private LinkLabel settingLink;
        private FlowLayoutPanel cs2SpecificFlow;
        private FlowLayoutPanel csgoSpecificFlow;

        private static readonly bool DualGameBootstrapRegistered = RegisterDualGameBootstrap();

        static Form1()
        {
            VersionNumber = ReleaseInfo.Version;
        }

        private static bool RegisterDualGameBootstrap()
        {
            Application.Idle += DualGameBootstrapOnIdle;
            return true;
        }

        private static void DualGameBootstrapOnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.IsDisposed || form.dualReady)
                    continue;
                if (!form.pigUiV2Initialized || form.restartCs2Button == null || form.specificSearchTextBox == null)
                    continue;

                form.InitializeDualGameController();

                // Font Manager is single-instance with one main Form1. Once the controller is wired,
                // the bootstrap has no reason to run on every future idle cycle.
                Application.Idle -= DualGameBootstrapOnIdle;
                break;
            }
        }

        private void InitializeDualGameController()
        {
            if (dualReady) return;
            dualReady = true;

            InitializeGamePaths();
            gameTarget = Settings != null && string.Equals(Settings.ActiveGame, "CSGO", StringComparison.OrdinalIgnoreCase)
                ? GameTarget.CSGO
                : GameTarget.CS2;
            cs2SpecificFlow = specificFamilyFlow;

            version_label.Text = "Version " + VersionNumber;
            title_label.Cursor = Cursors.Hand;
            title_label.Click += GameTitle_Click;

            gameHint = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe Script", 9f),
                ForeColor = Color.FromArgb(130, 145, 150),
                BackColor = Color.Transparent
            };
            Controls.Add(gameHint);
            InitializeGameHintFont();

            settingLink = new LinkLabel
            {
                AutoSize = true,
                Text = "Setting",
                LinkColor = linkLabel3.LinkColor,
                ActiveLinkColor = linkLabel3.ActiveLinkColor,
                VisitedLinkColor = linkLabel3.VisitedLinkColor,
                BackColor = Color.Transparent
            };
            settingLink.LinkClicked += SettingLink_LinkClicked;
            Controls.Add(settingLink);

            apply_button.Click -= pigUiV2_ApplyButtonClick;
            apply_button.Click += DualApply;

            specificApplyButton.Click -= specificApplyButton_Click;
            specificApplyButton.Click += SpecificApplyButton_Click;

            restartCs2Button.Click -= restartCs2Button_Click;
            restartCs2Button.Click += DualRestart;

            specificSettingTabButton.Click -= pigSpecificSettingTabButton_Click;
            specificSettingTabButton.Click += DualSpecificTab;

            specificViewCombo.SelectedIndexChanged -= pigUiV2_ViewChanged;
            specificViewCombo.SelectedIndexChanged += DualSpecificView;

            SizeChanged += DualGame_SizeChanged;
            if (cs2ProcessTimer != null)
                cs2ProcessTimer.Tick += DualGameProcessTimer_Tick;

            InitializeSpecificSettingsState();
            ApplyGameMode(false);
            QueueStartupLayoutFinalization();

            AppLog.Info("Font Manager " + VersionNumber + " dual-game controller initialized; active=" + GameName() + ".");
        }

        private void GameTitle_Click(object sender, EventArgs e)
        {
            gameTarget = gameTarget == GameTarget.CS2 ? GameTarget.CSGO : GameTarget.CS2;
            ApplyGameMode(true);
        }

        private void SettingLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ShowPathSettings();
        }

        private void DualGame_SizeChanged(object sender, EventArgs e)
        {
            LayoutDualBits();
        }

        private void DualGameProcessTimer_Tick(object sender, EventArgs e)
        {
            SyncGameUi();
        }

        private string GameName()
        {
            return gameTarget == GameTarget.CS2 ? "CS2" : "CS:GO";
        }

        private void SaveNow()
        {
            try
            {
                if (SettingsManager != null && Settings != null)
                    SettingsManager.Save(Settings);
            }
            catch (Exception exception)
            {
                AppLog.Warn("Could not save settings: " + exception.Message);
            }
        }

        private void ApplyGameMode(bool clicked)
        {
            if (Settings != null)
            {
                Settings.ActiveGame = gameTarget == GameTarget.CS2 ? "CS2" : "CSGO";
                if (!string.IsNullOrWhiteSpace(Settings.Cs2Path))
                    Settings.CsgoPath = Settings.Cs2Path;
                SaveNow();
            }

            SwitchSpecificFlow();
            SyncActiveSpecificControlsFromSettingsFast();
            SyncGameUi();
            ApplyAuthoritativePigLayout();
            LayoutDualBits();
            ApplySpecificTabContrast();

            bool missingPath = gameTarget == GameTarget.CS2
                ? !ValidCs2(Settings == null ? null : Settings.Cs2Path)
                : !ValidCsgo(Settings == null ? null : Settings.LegacyCsgoPath);

            if (clicked && missingPath)
            {
                MessageBox.Show(GameName() + " is not detected. Use Setting to detect or manually select its install path.",
                    GameName() + " Path", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void SyncGameUi()
        {
            if (!dualReady || IsDisposed) return;

            title_label.Text = GameName() + " Fonts";
            version_label.Text = "Version " + VersionNumber;
            gameHint.Text = gameTarget == GameTarget.CS2
                ? "← click here to change to cs:go"
                : "← click here to change to cs2";

            specificApplyButton.Text = "Apply Specific Font Settings to " + GameName();
            specificApplyButton.Visible = true;

            bool running = gameTarget == GameTarget.CS2 ? IsCs2Running() : IsCsgoRunning();
            restartCs2Button.Enabled = CurrentFormView == FormViews.Main && running;
            restartCs2Button.Text = running ? "Restart " + GameName() : "Restart " + GameName() + " (not running)";

            if (specificRestartButton != null)
            {
                specificRestartButton.Enabled = restartCs2Button.Enabled;
                specificRestartButton.Text = restartCs2Button.Text;
            }

            if (specificToolTip != null)
                specificToolTip.SetToolTip(specificSearchTextBox,
                    "Find family names and predicted " + GameName() + " usage. Enter jumps to the next match.");

            LayoutDualBits();
        }

        private void LayoutDualBits()
        {
            if (!dualReady || gameHint == null || settingLink == null) return;

            gameHint.Location = new Point(title_label.Right + 10, 9);
            settingLink.Location = new Point(Math.Max(70, linkLabel3.Left - 7 - settingLink.Width), ClientSize.Height - 23);
            settingLink.Visible = CurrentFormView == FormViews.Main;
            gameHint.BringToFront();
            settingLink.BringToFront();

            if (gameTarget == GameTarget.CSGO && specificSettingsTabActive && csgoSpecificFlow != null)
                LayoutCsgoFlow();
        }

        private void DualApply(object sender, EventArgs e)
        {
            if (CurrentFormView == FormViews.AddSystemFont)
            {
                ImportSelectedSystemFontWithEncodingCheck();
                return;
            }

            if (gameTarget == GameTarget.CS2)
            {
                apply_button_cs2_enhanced_Click(sender, e);
                return;
            }

            ApplyCsgoGeneral();
        }

        private void SpecificApplyButton_Click(object sender, EventArgs e)
        {
            CaptureActiveSpecificControlsToSettings();
            SaveNow();

            if (gameTarget == GameTarget.CS2)
                ApplySpecificFontSettings();
            else
                ApplyCsgoSpecific();
        }
    }
}

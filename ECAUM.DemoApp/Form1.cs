using System;
using System.Reflection;
using System.Windows.Forms;
using ECAUM;

namespace ECAUM.DemoApp
{
    public partial class Form1 : Form
    {
        private UpdateManager UpdateManager;

        public Form1()
        {
            InitializeComponent();
            VersionLabel.Text += Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }

        private void UpdateButton_Click(object sender, EventArgs e)
        {
            textBox1.Enabled = false;
            UpdateButton.Enabled = false;
            if (UpdateManager == null)
            {
                try
                {
                    Uri updateUri = new Uri(textBox1.Text);
                    UpdateManager = new UpdateManager(updateUri);
                }
                catch (UriFormatException)
                {
                    MessageBox.Show("You entered an invalid Uri!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    UpdateButton.Enabled = true;
                    return;
                }
            }

            switch (UpdateManager.State)
            {
                case UpdateState.NoUpdateAvailable:
                    CheckUpdate();
                    break;
                case UpdateState.UpdateAvailable:
                    DownloadUpdate();
                    break;
                case UpdateState.DownloadInProgress:
                    // button should be disabled
                    break;
                case UpdateState.UpdateReady:
                    InstallUpdate();
                    break;
                case UpdateState.UpdaterStarted:
                    // button should be disabled
                    break;
                case UpdateState.Error:
                    MessageBox.Show("Something went wrong :(", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;
                case UpdateState.UnknownPlatform:
                    MessageBox.Show("UnknownPlatform - This platform is not supported!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;
                default:
                    throw new NotImplementedException("Not implemented UpdateState");
            }

            UpdateButton.Enabled = true;
        }

        private void CheckUpdate()
        {
            var state = UpdateManager.CheckUpdate();
            if (state == UpdateState.UpdateAvailable)
            {
                var result = MessageBox.Show("A new update is available!\n\nDownload now?", "Update Available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result == DialogResult.Yes)
                    DownloadUpdate();
                UpdateButton.Text = "Download Update";
            }
            else
            {
                MessageBox.Show($"No update available\nCurrent update state: {state.ToString()}", "Information", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        private void DownloadUpdate()
        {
            UpdateButton.Enabled = false;

            // Handle events
            // Events are called by another thread => make sure to invoke were necessary
            UpdateManager.DownloadProgressChanged += (percentage) => {
                progressBar1.Invoke(() => progressBar1.Value = percentage);
            };

            UpdateManager.DownloadUpdateFinished += (state) => {
                if (state == UpdateState.UpdateReady)
                {
                    var result = MessageBox.Show("The update is ready to be installed\nInstall now?", "Update Ready", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (result == DialogResult.Yes)
                        this.BeginInvoke(() => InstallUpdate()); // invoke asynchronously to prevent deadlock
                    UpdateButton.BeginInvoke(() => UpdateButton.Text = "Install Update");
                }
                else
                {
                    MessageBox.Show($"Update could not be prepared!\nCurrent update state: {state.ToString()}", "Error.", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                progressBar1.BeginInvoke(() => progressBar1.Visible = false);
                UpdateButton.BeginInvoke(() => UpdateButton.Enabled = true);
            };

            progressBar1.Visible = true;
            if (UpdateManager.DownloadUpdateAsync() != UpdateState.DownloadInProgress)
            {
                MessageBox.Show($"Update could not be prepared!\nCurrent update state: {UpdateManager.State.ToString()}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                progressBar1.Visible = false;
                UpdateButton.Enabled = true;
            }
        }

        private void InstallUpdate()
        {
            UpdateButton.Enabled = false;
            var result = MessageBox.Show("To install the update the app must be restarted.\nRestart now?", "Install Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                // We specify the app name to automatically restart it
                if (UpdateManager.InstallUpdate("DemoApp.exe") == UpdateState.UpdaterStarted)
                { // Now we have 10 seconds to exit
                    Application.Exit();
                }
                else
                {
                    MessageBox.Show($"Updater could not be startet!\nCurrent update state: {UpdateManager.State.ToString()}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            UpdateButton.Enabled = true;
        }

    }
}

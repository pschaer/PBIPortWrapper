using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper
{
    public partial class MainForm : Form
    {
        private PowerBIDetector _detector;
        private TcpProxyService _proxyService;
        private ConfigurationManager _configManager;
        private ProxyConfiguration _config;
        private PowerBIInstance _selectedInstance;

        public MainForm()
        {
            InitializeComponent();

            this.Text = "Power BI Port Wrapper v0.1";

            InitializeServices();
            InitializeEventHandlers();
            LoadConfiguration();

            LogMessage("Power BI Port Wrapper v0.1");
            LogMessage("=".PadRight(50, '='));
            LogMessage("Features:");
            LogMessage("  ✓ Stable port forwarding for Power BI Desktop");
            LogMessage("  ✓ Local connections fully supported");
            LogMessage("  ✓ Remote connections with explicit credentials");
            LogMessage($"Log file: {_configManager.GetLogFilePath()}");
            LogMessage("");

            RefreshInstances();
        }

        private void InitializeServices()
        {
            _detector = new PowerBIDetector();
            _proxyService = new TcpProxyService();
            _configManager = new ConfigurationManager();

            _proxyService.OnLog += (sender, message) =>
            {
                LogMessage(message);
            };

            _proxyService.OnError += (sender, message) =>
            {
                LogMessage(message);
            };
        }

        private void InitializeEventHandlers()
        {
            buttonStart.Click += ButtonStart_Click;
            buttonStop.Click += ButtonStop_Click;
            buttonRefresh.Click += ButtonRefresh_Click;
            buttonCopy.Click += ButtonCopy_Click;
            buttonOpenLogs.Click += ButtonOpenLogs_Click;
            checkBoxNetworkAccess.CheckedChanged += CheckBoxNetworkAccess_CheckedChanged;
            listBoxInstances.SelectedIndexChanged += ListBoxInstances_SelectedIndexChanged;
            this.FormClosing += MainForm_FormClosing;

            UpdateStatus("Not Running", false);
        }

        private void LoadConfiguration()
        {
            _config = _configManager.LoadConfiguration();

            textBoxFixedPort.Text = _config.FixedPort.ToString();
            checkBoxNetworkAccess.Checked = _config.AllowNetworkAccess;

            UpdateConnectionString();
        }

        private void SaveConfiguration()
        {
            try
            {
                if (int.TryParse(textBoxFixedPort.Text, out int fixedPort))
                {
                    _config.FixedPort = fixedPort;
                }

                _config.AllowNetworkAccess = checkBoxNetworkAccess.Checked;

                if (_selectedInstance != null)
                {
                    _config.LastSelectedInstance = _selectedInstance.WorkspaceId;
                }

                _configManager.SaveConfiguration(_config);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error saving configuration: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void RefreshInstances()
        {
            listBoxInstances.Items.Clear();

            if (!_detector.IsWorkspacePathValid())
            {
                listBoxInstances.Items.Add("Power BI Desktop workspace folder not found");
                return;
            }

            var instances = _detector.DetectRunningInstances();

            if (instances.Count == 0)
            {
                listBoxInstances.Items.Add("No running Power BI Desktop instances found");
                return;
            }

            foreach (var instance in instances)
            {
                listBoxInstances.Items.Add(instance);
            }

            if (instances.Count > 0)
            {
                if (!string.IsNullOrEmpty(_config.LastSelectedInstance))
                {
                    var lastInstance = instances.FirstOrDefault(i => i.WorkspaceId == _config.LastSelectedInstance);
                    if (lastInstance != null)
                    {
                        listBoxInstances.SelectedItem = lastInstance;
                        return;
                    }
                }

                listBoxInstances.SelectedIndex = 0;
            }
        }

        private async void ButtonStart_Click(object sender, EventArgs e)
        {
            if (_selectedInstance == null)
            {
                MessageBox.Show(
                    "Please select a Power BI Desktop instance first.",
                    "No Instance Selected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!int.TryParse(textBoxFixedPort.Text, out int fixedPort) || fixedPort < 1024 || fixedPort > 65535)
            {
                MessageBox.Show(
                    "Please enter a valid port number (1024-65535).",
                    "Invalid Port",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                ClearLog();
                LogMessage($"Starting port forwarding for {_selectedInstance.FileName}");
                LogMessage($"Target: localhost:{_selectedInstance.Port}");

                if (!string.IsNullOrEmpty(_selectedInstance.DatabaseName))
                {
                    LogMessage($"Database: {_selectedInstance.DatabaseName}");
                }
                else
                {
                    LogMessage("Database: (not detected)");
                }

                bool allowRemote = checkBoxNetworkAccess.Checked;

                if (allowRemote)
                {
                    CheckFirewall();
                }

                await _proxyService.StartAsync(fixedPort, _selectedInstance.Port, allowRemote);

                UpdateStatus("Running", true);
                UpdateConnectionString();
                SaveConfiguration();

                LogMessage("Port forwarding started successfully");
                LogMessage($"Clients can connect to: localhost:{fixedPort}");

                if (allowRemote)
                {
                    LogMessage("Network access enabled - accessible from other computers");
                    LogMessage("Remote connection info:");
                    LogMessage("  • Use explicit credentials (username/password)");
                    LogMessage("  • Windows Integrated Authentication may not work from remote hosts");
                    LogMessage("  • Provide your Microsoft Account or local Windows credentials");

                    try
                    {
                        var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                        foreach (var ip in host.AddressList)
                        {
                            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                LogMessage($"  Network address: {ip}:{fixedPort}");
                            }
                        }
                    }
                    catch
                    {
                        LogMessage("  Could not determine local IP addresses");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to start: {ex.Message}");
                MessageBox.Show(
                    $"Failed to start port forwarding:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                UpdateStatus("Error", false);
            }
        }

        private void ButtonStop_Click(object sender, EventArgs e)
        {
            try
            {
                LogMessage("Stopping port forwarding...");
                _proxyService.Stop();
                UpdateStatus("Stopped", false);
            }
            catch (Exception ex)
            {
                LogMessage($"Error stopping: {ex.Message}");
                MessageBox.Show(
                    $"Error stopping port forwarding:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void ButtonRefresh_Click(object sender, EventArgs e)
        {
            LogMessage("Refreshing Power BI instances...");
            RefreshInstances();

            int count = 0;
            foreach (var item in listBoxInstances.Items)
            {
                if (item is PowerBIInstance instance)
                {
                    count++;
                    LogMessage($"  Found: {instance.FileName} (Port: {instance.Port}, DB: {instance.DatabaseName ?? "unknown"})");
                }
            }

            if (count > 0)
            {
                LogMessage($"Total: {count} running instance(s)");
            }
            else
            {
                LogMessage("No running instances found");
            }
        }

        private void ButtonCopy_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(textBoxConnectionString.Text))
            {
                Clipboard.SetText(textBoxConnectionString.Text);
                LogMessage("Server address copied to clipboard");
            }
        }

        private void ButtonOpenLogs_Click(object sender, EventArgs e)
        {
            try
            {
                string logPath = _configManager.GetAppDataPath();
                System.Diagnostics.Process.Start("explorer.exe", logPath);
                LogMessage($"Opened log folder: {logPath}");
            }
            catch (Exception ex)
            {
                LogMessage($"Error opening log folder: {ex.Message}");
                MessageBox.Show(
                    $"Could not open log folder:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void CheckBoxNetworkAccess_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxNetworkAccess.Checked)
            {
                LogMessage("Network access will be enabled on next start");
            }
        }

        private void ListBoxInstances_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_proxyService != null && _proxyService.IsRunning)
            {
                if (_selectedInstance != null)
                {
                    listBoxInstances.SelectedItem = _selectedInstance;
                }
                return;
            }

            if (listBoxInstances.SelectedItem is PowerBIInstance instance)
            {
                _selectedInstance = instance;
                UpdateConnectionString();
            }
            else
            {
                _selectedInstance = null;
            }
        }

        private void UpdateConnectionString()
        {
            if (int.TryParse(textBoxFixedPort.Text, out int port))
            {
                textBoxConnectionString.Text = $"localhost:{port}";
            }
        }

        private void UpdateStatus(string status, bool isRunning)
        {
            labelStatus.Text = $"Status: {status}";
            labelStatus.ForeColor = isRunning ? System.Drawing.Color.Green : System.Drawing.Color.Red;

            buttonStart.Enabled = !isRunning;
            buttonStop.Enabled = isRunning;
            buttonRefresh.Enabled = !isRunning;

            textBoxFixedPort.Enabled = !isRunning;
            checkBoxNetworkAccess.Enabled = !isRunning;
            listBoxInstances.Enabled = !isRunning;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_proxyService != null && _proxyService.IsRunning)
            {
                var result = MessageBox.Show(
                    "Port forwarding is still running. Stop it and exit?",
                    "Confirm Exit",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result == DialogResult.Yes)
                {
                    LogMessage("Application closing - stopping port forwarding");
                    _proxyService.Stop();
                }
                else
                {
                    e.Cancel = true;
                    return;
                }
            }

            _proxyService?.Dispose();
            SaveConfiguration();
        }

        private void LogMessage(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => LogMessage(message)));
                return;
            }

            string timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";

            textBoxLog.AppendText($"{timestampedMessage}{Environment.NewLine}");
            textBoxLog.SelectionStart = textBoxLog.Text.Length;
            textBoxLog.ScrollToCaret();

            try
            {
                string logFile = _configManager.GetLogFilePath();
                File.AppendAllText(logFile, timestampedMessage + Environment.NewLine);
            }
            catch
            {
                // Silently fail if can't write to log file
            }
        }

        private void ClearLog()
        {
            textBoxLog.Clear();
        }

        private void CheckFirewall()
        {
            var result = MessageBox.Show(
                "Network Access Configuration:\n\n" +
                "✓ Remote connections are supported\n" +
                "✓ Clients must provide explicit credentials\n" +
                "  (username/password, not Windows Authentication)\n\n" +
                "Note: Windows Firewall may block connections.\n" +
                "Would you like instructions on how to configure the firewall?",
                "Network Access",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information
            );

            if (result == DialogResult.Yes)
            {
                ShowFirewallInstructions();
            }
        }

        private void ShowFirewallInstructions()
        {
            string instructions =
                "To allow network access through Windows Firewall:\n\n" +
                "1. Open Windows Defender Firewall with Advanced Security\n" +
                "2. Click 'Inbound Rules' → 'New Rule'\n" +
                "3. Select 'Port' → Next\n" +
                "4. Select 'TCP' and enter port: " + textBoxFixedPort.Text + "\n" +
                "5. Select 'Allow the connection' → Next\n" +
                "6. Check all profiles (Domain, Private, Public) → Next\n" +
                "7. Name: 'Power BI Port Wrapper' → Finish\n\n" +
                "Or run this PowerShell command as Administrator:\n\n" +
                $"New-NetFirewallRule -DisplayName \"Power BI Port Wrapper\" -Direction Inbound -LocalPort {textBoxFixedPort.Text} -Protocol TCP -Action Allow";

            var form = new Form
            {
                Text = "Firewall Configuration Instructions",
                Width = 600,
                Height = 400,
                StartPosition = FormStartPosition.CenterParent
            };

            var textBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Text = instructions,
                Font = new System.Drawing.Font("Consolas", 9),
                ReadOnly = true
            };

            var copyButton = new Button
            {
                Text = "Copy PowerShell Command",
                Dock = DockStyle.Bottom,
                Height = 35
            };

            copyButton.Click += (s, ev) =>
            {
                string psCommand = $"New-NetFirewallRule -DisplayName \"Power BI Port Wrapper\" -Direction Inbound -LocalPort {textBoxFixedPort.Text} -Protocol TCP -Action Allow";
                Clipboard.SetText(psCommand);
                MessageBox.Show("PowerShell command copied to clipboard!", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            form.Controls.Add(textBox);
            form.Controls.Add(copyButton);
            form.ShowDialog();
        }
    }
}
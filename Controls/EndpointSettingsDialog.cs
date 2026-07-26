using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Presenters;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Controls
{
    /// <summary>
    /// The XMLA endpoint's settings in one place (#125): what it is doing, the switch,
    /// the two values that have to be typed rather than picked, and how callers
    /// authenticate.
    ///
    /// Every change is written through <see cref="ConfigService"/> and applied by
    /// <see cref="XmlaEndpointCoordinator"/> — this dialog never starts or stops the
    /// listener itself, which is why it stays in agreement with the tray. There is no
    /// OK/Cancel: settings take effect as they are changed, exactly as they do in the
    /// tray, so a Close button is the whole ceremony.
    /// </summary>
    public class EndpointSettingsDialog : Form
    {
        private readonly ConfigService _config;
        private readonly XmlaEndpointCoordinator _endpoint;

        private readonly Label _status = new Label();
        private readonly Label _warning = new Label();
        private readonly CheckBox _enabled = new CheckBox();
        private readonly NumericUpDown _port = new NumericUpDown();
        private readonly TextBox _hostname = new TextBox();
        private readonly ComboBox _authMode = new ComboBox();
        private readonly Label _authDescription = new Label();
        private readonly Button _copyAclCommand = new Button();

        /// <summary>
        /// Debounces the port so that spinning 55555 → 55560 binds once, at the end,
        /// rather than binding every value on the way.
        /// </summary>
        private readonly System.Windows.Forms.Timer _portCommitTimer =
            new System.Windows.Forms.Timer { Interval = 700 };

        /// <summary>
        /// True while the controls are being filled from configuration, so that
        /// projecting a value back into the UI does not look like a user edit and
        /// write it out again.
        /// </summary>
        private bool _loading;

        public EndpointSettingsDialog(ConfigService config, XmlaEndpointCoordinator endpoint)
        {
            _config = config;
            _endpoint = endpoint;

            BuildLayout();
            WireEvents();
            LoadFromConfig();

            _endpoint.StatusChanged += OnStatusChanged;
        }

        private HttpBridgeConfig Settings => _config?.Current?.HttpBridge;

        /// <summary>
        /// Built with a TableLayoutPanel rather than fixed coordinates. The first cut
        /// used absolute positions and both failure modes followed immediately: the
        /// authentication description ran off the right edge, and Restart overlapped
        /// Close because both auto-sized. Text length varies by mode and by DPI, so
        /// nothing here may depend on a guessed pixel.
        /// </summary>
        private void BuildLayout()
        {
            Text = "XMLA endpoint";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;

            // Grow to fit the content: the longest description is three lines, and a
            // fixed height would clip it at another DPI or font size.
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            int wrapWidth = LogicalToDeviceUnits(420);
            int fieldWidth = LogicalToDeviceUnits(260);

            var layout = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = new Padding(LogicalToDeviceUnits(12))
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _status.AutoSize = true;
            _status.Font = new Font(Font, FontStyle.Bold);
            _status.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(6));
            AddSpanningRow(layout, _status);

            _warning.AutoSize = true;
            _warning.MaximumSize = new Size(wrapWidth, 0);   // wraps, then grows downward
            _warning.ForeColor = Color.Firebrick;
            _warning.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(6));
            AddSpanningRow(layout, _warning);

            _enabled.AutoSize = true;
            _enabled.Text = "Enabled";
            _enabled.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(10));
            AddSpanningRow(layout, _enabled);

            // Named so that tooling and tests can find them: a NumericUpDown contains
            // its own TextBox, so "the first TextBox" is not the host name field.
            _port.Name = "portField";
            _hostname.Name = "hostnameField";

            _port.Minimum = ConfigService.MinEndpointPort;
            _port.Maximum = ConfigService.MaxEndpointPort;
            _port.Width = LogicalToDeviceUnits(90);
            AddFieldRow(layout, "Port", _port);

            _hostname.Width = fieldWidth;
            _hostname.PlaceholderText = "detected automatically";
            AddFieldRow(layout, "Host name", _hostname);

            _authMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _authMode.Width = fieldWidth;
            foreach (BridgeAuthMode mode in BridgeAuthModeLabel.Order)
                _authMode.Items.Add(BridgeAuthModeLabel.For(mode));
            AddFieldRow(layout, "Authentication", _authMode);

            _authDescription.AutoSize = true;
            _authDescription.MaximumSize = new Size(wrapWidth, 0);
            _authDescription.ForeColor = SystemColors.GrayText;
            _authDescription.Margin = new Padding(0, LogicalToDeviceUnits(2), 0, LogicalToDeviceUnits(10));
            AddSpanningRow(layout, _authDescription);

            AddSpanningRow(layout, BuildButtonRow());

            Controls.Add(layout);
        }

        /// <summary>
        /// Buttons in a right-to-left flow, so they pack from the right edge and can
        /// never overlap however long their captions get.
        /// </summary>
        private Control BuildButtonRow()
        {
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };

            var close = new Button { Text = "Close", AutoSize = true };
            close.Click += (s, e) => Close();

            var restart = new Button { Text = "Restart", AutoSize = true };
            restart.Click += (s, e) => _endpoint?.Restart();

            _copyAclCommand.Text = "Copy the command that fixes LAN access";
            _copyAclCommand.AutoSize = true;
            _copyAclCommand.Visible = false;

            buttons.Controls.Add(close);
            buttons.Controls.Add(restart);
            buttons.Controls.Add(_copyAclCommand);

            AcceptButton = close;
            CancelButton = close;
            return buttons;
        }

        private void AddSpanningRow(TableLayoutPanel layout, Control control)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control, 0, row);
            layout.SetColumnSpan(control, 2);
        }

        private void AddFieldRow(TableLayoutPanel layout, string caption, Control field)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(new Label
            {
                Text = caption,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, LogicalToDeviceUnits(12), LogicalToDeviceUnits(6))
            }, 0, row);

            field.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(6));
            layout.Controls.Add(field, 1, row);
        }

        private void WireEvents()
        {
            _enabled.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                _config?.SetEndpointEnabled(_enabled.Checked);
            };

            // A port change rebinds the listener, so it cannot fire on every keystroke:
            // typing "6" on the way to "60123" would bind port 6. But waiting for focus
            // to leave means the consequences - a failed bind, a fallback to localhost -
            // stay invisible while the user is looking straight at the dialog. So the
            // commit is debounced: keep typing or spinning freely, and shortly after
            // stopping it applies and the status updates in place.
            _port.ValueChanged += (s, e) =>
            {
                if (_loading) return;
                _portCommitTimer.Stop();
                _portCommitTimer.Start();
            };

            _portCommitTimer.Tick += (s, e) =>
            {
                // A WinForms timer repeats until stopped; this one is a one-shot.
                _portCommitTimer.Stop();
                if (_loading) return;
                _config?.SetEndpointPort((int)_port.Value);
            };

            // Leaving the field commits immediately rather than waiting out the delay.
            _port.Leave += (s, e) =>
            {
                if (_loading) return;
                _portCommitTimer.Stop();
                _config?.SetEndpointPort((int)_port.Value);
            };

            _hostname.Leave += (s, e) =>
            {
                if (_loading) return;
                _config?.SetEndpointHostname(_hostname.Text);
            };

            _authMode.SelectedIndexChanged += (s, e) =>
            {
                if (_loading || _authMode.SelectedIndex < 0) return;
                BridgeAuthMode mode = BridgeAuthModeLabel.Order[_authMode.SelectedIndex];
                _authDescription.Text = BridgeAuthModeLabel.Describe(mode);
                _config?.SetEndpointAuthMode(mode);
            };

            _copyAclCommand.Click += (s, e) =>
                EndpointMenuBuilder.CopyToClipboard(
                    EndpointUrlBuilder.UrlAclCommand(_endpoint?.Status?.Port ?? 0));
        }

        private void LoadFromConfig()
        {
            HttpBridgeConfig settings = Settings;
            if (settings == null) return;

            _loading = true;
            try
            {
                _enabled.Checked = settings.Enabled;
                _port.Value = Math.Min(Math.Max(settings.Port, _port.Minimum), _port.Maximum);
                _hostname.Text = settings.Hostname ?? string.Empty;

                int index = BridgeAuthModeLabel.Order.ToList().IndexOf(settings.AuthMode);
                _authMode.SelectedIndex = index >= 0 ? index : 0;
                _authDescription.Text = BridgeAuthModeLabel.Describe(settings.AuthMode);
            }
            finally
            {
                _loading = false;
            }

            ShowStatus(_endpoint?.Status);
        }

        /// <summary>
        /// Status arrives from whichever thread applied it — a serve completing on a
        /// worker can trigger a configuration change — so it is marshaled here.
        /// </summary>
        private void OnStatusChanged(object sender, EndpointStatus status)
        {
            if (IsDisposed || Disposing) return;
            try { BeginInvoke(new Action(() => ShowStatus(status))); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void ShowStatus(EndpointStatus status)
        {
            if (status == null) return;

            _status.Text = status.Summary;
            _copyAclCommand.Visible = status.Running && status.IsLocalOnly;

            if (status.Running && status.IsLocalOnly)
            {
                _warning.Text =
                    "Other machines cannot reach this endpoint. It needs a one-time URL " +
                    "reservation — copy the command below and run it as Administrator.";
            }
            else if (status.IsUnauthenticated)
            {
                _warning.Text =
                    "No authentication: anyone who can reach this port can read and change " +
                    "every served model.";
            }
            else
            {
                _warning.Text = string.Empty;
            }
        }

        /// <summary>
        /// The typed fields commit on Leave, which normally happens when the user
        /// clicks Close. Pressing Enter fires the accept button without moving focus,
        /// so without this a port typed and confirmed with the keyboard would be
        /// silently discarded. The setters ignore unchanged values, so committing
        /// twice costs nothing.
        /// </summary>
        private void CommitPendingEdits()
        {
            if (_loading || _config == null) return;

            _portCommitTimer.Stop();
            _config.SetEndpointPort((int)_port.Value);
            _config.SetEndpointHostname(_hostname.Text);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            CommitPendingEdits();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _endpoint.StatusChanged -= OnStatusChanged;
            _portCommitTimer.Stop();
            _portCommitTimer.Dispose();
            base.OnFormClosed(e);
        }
    }
}

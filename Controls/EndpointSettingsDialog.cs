using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PBIRelay.Models;
using PBIRelay.Presenters;
using PBIRelay.Services;

namespace PBIRelay.Controls
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
        private readonly CheckBox _accessLog = new CheckBox();
        private readonly NumericUpDown _port = new NumericUpDown();
        private readonly TextBox _hostname = new TextBox();
        private readonly ComboBox _authMode = new ComboBox();
        private readonly Label _authDescription = new Label();
        private CertificateSettingsSection _certificates;

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
        private readonly Func<string> _accessLogPath;

        public EndpointSettingsDialog(
            ConfigService config, XmlaEndpointCoordinator endpoint, Func<string> accessLogPath = null)
        {
            _config = config;
            _endpoint = endpoint;
            _accessLogPath = accessLogPath;

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

            // Access logging belongs here as much as in the tray: the dashboard is
            // where someone goes looking for a setting, and a switch that exists in
            // only one of the two surfaces is the drift #107 was about (#128).
            _accessLog.AutoSize = true;
            _accessLog.Text = "Record every request in the access log";
            _accessLog.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(2));
            AddSpanningRow(layout, _accessLog);

            var accessLogNote = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(wrapWidth, 0),
                ForeColor = SystemColors.GrayText,
                Text = "Who connected, to which model, when. Safe to leave on: it records " +
                       "that a query ran, never the query or its results.",
                Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(10))
            };
            AddSpanningRow(layout, accessLogNote);

            // Encryption last: it is the only section with a sub-choice, and putting it
            // above the plain switches would push them off the first glance (#132). Its
            // rows join THIS layout rather than nesting one, so its fields line up with
            // Port and Authentication above.
            _certificates = new CertificateSettingsSection(
                _config, wrapWidth, fieldWidth, LogicalToDeviceUnits(6));
            _certificates.AddTo(
                control => AddSpanningRow(layout, control),
                (caption, field) => AddFieldRow(layout, caption, field));

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

            var openAccessLog = new Button { Text = "Access log…", AutoSize = true };
            openAccessLog.Click += (s, e) => Presenters.AccessLogAction.Open(_accessLogPath?.Invoke());

            buttons.Controls.Add(close);
            buttons.Controls.Add(restart);
            buttons.Controls.Add(openAccessLog);

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

        private void AddFieldRow(TableLayoutPanel layout, string caption, Control field) =>
            AddFieldRow(layout, new Label { Text = caption }, field);

        /// <summary>
        /// The caption as a Label rather than a string, so a section that renames its
        /// own captions at runtime - the certificate one does - still gets its rows from
        /// here. Every field row in the dialog must come through this method: a second
        /// layout with its own caption column cannot agree with this one's width, and
        /// the fields end up a few pixels apart.
        /// </summary>
        private void AddFieldRow(TableLayoutPanel layout, Label caption, Control field)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            caption.AutoSize = true;
            caption.Anchor = AnchorStyles.Left;
            caption.Margin = new Padding(0, 0, LogicalToDeviceUnits(12), LogicalToDeviceUnits(6));
            layout.Controls.Add(caption, 0, row);

            field.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(6));
            layout.Controls.Add(field, 1, row);
        }

        private void WireEvents()
        {
            _accessLog.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                _config?.SetAccessLog(_accessLog.Checked);
            };

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
                _authDescription.Text = BridgeAuthModeLabel.Describe(mode, Settings?.UseHttps ?? false);
                _config?.SetEndpointAuthMode(mode);
            };

        }

        private void LoadFromConfig()
        {
            HttpBridgeConfig settings = Settings;
            if (settings == null) return;

            _loading = true;
            try
            {
                _enabled.Checked = settings.Enabled;
                _accessLog.Checked = settings.AccessLog;
                _port.Value = Math.Min(Math.Max(settings.Port, _port.Minimum), _port.Maximum);
                _hostname.Text = settings.Hostname ?? string.Empty;

                int index = BridgeAuthModeLabel.Order.ToList().IndexOf(settings.AuthMode);
                _authMode.SelectedIndex = index >= 0 ? index : 0;
                _authDescription.Text = BridgeAuthModeLabel.Describe(settings.AuthMode, settings.UseHttps);
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

            // The certificate actually being SERVED, which is not necessarily the one the
            // settings below resolve to - those describe what the next start would use.
            _status.Text = status.Https && !string.IsNullOrEmpty(status.CertificateSubject)
                ? $"{status.Summary} — serving {status.CertificateSubject}" +
                  (status.CertificateExpiry.HasValue
                      ? $", valid until {status.CertificateExpiry.Value:yyyy-MM-dd}"
                      : string.Empty)
                : status.Summary;

            _certificates?.Refresh(Settings);

            // The authentication note depends on the transport - "the password is not
            // encrypted in transit" stops being true the moment HTTPS goes on, and that
            // switch lives in the section below this one.
            if (Settings != null)
                _authDescription.Text = BridgeAuthModeLabel.Describe(Settings.AuthMode, Settings.UseHttps);

            // The URL-reservation warning went with the localhost fallback (#132):
            // Kestrel binds every address, so the only thing left to warn about is
            // running with no authentication at all.
            if (status.IsUnauthenticated)
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
            _certificates?.CommitPaths();
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

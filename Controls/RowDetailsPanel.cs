using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;
using PBIPortWrapper.Presenters;
using System.Linq;

namespace PBIPortWrapper.Controls
{
    public class RowDetailsPanel : UserControl
    {
        private readonly RowDetailsPresenter _presenter;
        private readonly Action<string> _logCallback;

        private GroupBox _grpInfo;
        private GroupBox _grpConnections;
        private GroupBox _grpStrings;
        private GroupBox _grpAlias;

        private Label _lblFile;
        private Label _lblFolder;
        private Label _lblDB;
        private Label _lblAlias;
        private Label _lblServeState;
        private ListView _lvConnections;
        private NavigableTextBox _txtAlias;
        private Button _btnSaveAlias;
        private Label _lblAliasHint;
        private ToolTip _sharedToolTip;

        private int _currentFixedPort = 0; // Still need local state for UI updates
        private string _stringsKey;        // last (port, serving alias) the copy buttons were built for
        private System.Windows.Forms.Timer _refreshTimer;

        public RowDetailsPanel(
            RowDetailsPresenter presenter,
            Action<string> logCallback)
        {
            _presenter = presenter;
            _logCallback = logCallback;

            InitializeComponent();
            _sharedToolTip = new ToolTip();
            RefreshData();
            
            _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _refreshTimer.Tick += (s, e) => UpdateLiveStats();
            _refreshTimer.Start();
        }

        



        protected override void Dispose(bool disposing)
        {
            if (disposing && _refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Dispose();
            }
            if (disposing && _sharedToolTip != null)
            {
                _sharedToolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            // All design-time dimensions are 96-DPI logical pixels; scale them to
            // the actual monitor DPI or text overflows the fixed-width columns.
            int S(int v) => LogicalToDeviceUnits(v);

            this.Size = new Size(S(800), S(180));
            this.BackColor = Color.WhiteSmoke;
            this.Padding = new Padding(S(10));

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 4;
            layout.RowCount = 1;

            // Initial Styles (will be updated by Resize event)
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F)); // Info (65% of remainder)
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F)); // Connections (35% of remainder)
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(170))); // Strings (Fixed 170)
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(250))); // Rename (Min 170, Max 300)
            
            _grpInfo = CreateGroupBox("Database Info");
            _grpConnections = CreateGroupBox("Connections"); // Shortened from "Active Connections" to prevent wrapping
            _grpStrings = CreateGroupBox("Connection Strings");
            _grpAlias = CreateGroupBox("Serve Alias");

            layout.Controls.Add(_grpInfo, 0, 0);
            layout.Controls.Add(_grpConnections, 1, 0);
            layout.Controls.Add(_grpStrings, 2, 0);
            layout.Controls.Add(_grpAlias, 3, 0);

            this.Controls.Add(layout);

            // Responsive Layout Logic
            layout.Resize += (s, e) =>
            {
                float totalW = layout.ClientSize.Width;

                // 1. Strings: Fixed Width 170
                float colStrings = S(170);

                // 2. Rename: Scale between 170 and 300
                float colRename = totalW * 0.25f; // Aim for 25% width
                if (colRename < S(170)) colRename = S(170);
                if (colRename > S(300)) colRename = S(300);

                layout.ColumnStyles[2].SizeType = SizeType.Absolute;
                layout.ColumnStyles[2].Width = colStrings;

                layout.ColumnStyles[3].SizeType = SizeType.Absolute;
                layout.ColumnStyles[3].Width = colRename;
                
                // Remainder goes to col 0 and 1 via Percent
            };

            // 1. Info area
            var flowInfo = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoScroll = true, WrapContents = false };
            _lblFile = new Label { AutoSize = true, Padding = new Padding(0, 0, 0, 2) };
            _lblFolder = new Label { AutoSize = true, Padding = new Padding(0, 0, 0, 2) };
            _lblDB = new Label { AutoSize = true, Padding = new Padding(0, 0, 0, 2) };
            _lblAlias = new Label { AutoSize = true, Padding = new Padding(0, 0, 0, 2), ForeColor = Color.DarkGray };
            _lblServeState = new Label { AutoSize = true, Padding = new Padding(0, 0, 0, 2), Font = new Font(Font, FontStyle.Bold) };

            flowInfo.Controls.AddRange(new Control[] { _lblFile, _lblFolder, _lblDB, _lblAlias, _lblServeState });
            _grpInfo.Controls.Add(flowInfo);
            
            _grpInfo.Resize += (s, e) => 
            {
                int w = Math.Max(_grpInfo.ClientSize.Width - 20, 50);
                foreach (Control c in flowInfo.Controls) c.MaximumSize = new Size(w, 0);
            };

            // 2. Connections
             _lvConnections = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
             _lvConnections.Columns.Add("Client", S(120)); // Initial
             _lvConnections.Columns.Add("Duration", S(80));   // Initial
             _grpConnections.Controls.Add(_lvConnections);

             _grpConnections.Resize += (s, e) =>
             {
                 int w = _lvConnections.ClientSize.Width;
                 _lvConnections.Columns[1].Width = S(80); // Fixed duration
                 _lvConnections.Columns[0].Width = Math.Max(w - S(85), S(100)); // Remainder to EP
             };

             // 3. Strings
             var flowStrings = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
             _grpStrings.Controls.Add(flowStrings);
             
             _grpStrings.Resize += (s, e) =>
             {
                 int w = Math.Max(flowStrings.ClientSize.Width - 10, 50);
                 foreach (Control c in flowStrings.Controls) c.Width = w;
             };

             // 4. Serve alias editor (the raw rename-DB danger flow is retired —
             // serving owns renames now, #59)
             var flowAlias = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(S(5)) };

             _txtAlias = new NavigableTextBox { PlaceholderText = "Stable database name" };
             _btnSaveAlias = new Button { Text = "Save Alias", Height = S(30) };
             _btnSaveAlias.Click += BtnSaveAlias_Click;
             _lblAliasHint = new Label { AutoSize = true, ForeColor = Color.DarkGray, Text = "Used as the database name while serving." };

             flowAlias.Controls.Add(_txtAlias);
             flowAlias.Controls.Add(_btnSaveAlias);
             flowAlias.Controls.Add(_lblAliasHint);
             _grpAlias.Controls.Add(flowAlias);

             _grpAlias.Resize += (s, e) =>
             {
                 int w = Math.Max(flowAlias.ClientSize.Width - 14, 50);
                 _txtAlias.Width = w;
                 _btnSaveAlias.Width = w;
                 _lblAliasHint.MaximumSize = new Size(w, 0);
             };
        }

        private GroupBox CreateGroupBox(string title)
        {
            return new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };
        }

        public void UpdateInstance(PowerBIInstance instance)
        {
            _presenter.UpdateInstance(instance);
            RefreshData();
        }

        public void RefreshData()
        {
            var data = _presenter.GetDisplayData();

            _lblFile.Text = $"Model: {data.ModelName}";
            _lblFolder.Text = data.TooltipText;
            _lblDB.Text = $"DB: {data.DatabaseOriginalName}";

            if (!string.IsNullOrEmpty(data.DatabaseAlias))
            {
                 _lblAlias.Text = $"Alias: {data.DatabaseAlias}";
                 if (!_txtAlias.Focused && _txtAlias.Text != data.DatabaseAlias)
                    _txtAlias.Text = data.DatabaseAlias;
            }
            else
            {
                 _lblAlias.Text = "";
                 if (!_txtAlias.Focused) _txtAlias.Text = "";
            }

            UpdateServeState(data);
            UpdateConnectionStrings(data.FixedPort, data.IsServing ? data.DatabaseAlias : null);
        }

        /// <summary>
        /// Makes the alias/DB relationship explicit (#59 polish): serving, alias
        /// configured but idle, or a renamed DB without a session (crash leftover).
        /// </summary>
        private void UpdateServeState(DetailsDisplayData data)
        {
            // #9: a rule saved under "Untitled" is orphaned once the model gets
            // its real name - configuration stays locked until the file is saved.
            bool untitled = string.Equals(data.ModelName, "Untitled", StringComparison.OrdinalIgnoreCase);

            if (untitled)
            {
                _lblServeState.Text = "Unsaved model — save the .pbix to enable configuration.";
                _lblServeState.ForeColor = Color.DarkOrange;
            }
            else if (data.IsServing)
            {
                _lblServeState.Text = $"Serving as '{data.DatabaseAlias}' on port {data.FixedPort}";
                _lblServeState.ForeColor = Color.MediumBlue;
            }
            else if (string.IsNullOrEmpty(data.DatabaseAlias))
            {
                _lblServeState.Text = "No alias configured — set one to enable serving.";
                _lblServeState.ForeColor = Color.DarkGray;
            }
            else if (string.Equals(data.DatabaseOriginalName, data.DatabaseAlias, StringComparison.OrdinalIgnoreCase))
            {
                _lblServeState.Text = "DB already carries the alias name but nothing is serving (crash recovery pending?)";
                _lblServeState.ForeColor = Color.DarkOrange;
            }
            else
            {
                _lblServeState.Text = "Alias configured — not serving.";
                _lblServeState.ForeColor = Color.DarkGray;
            }

            // The alias is the serve session's identity; lock it while in use.
            bool locked = data.IsServing || untitled;
            _txtAlias.Enabled = !locked;
            _btnSaveAlias.Enabled = !locked;
            _lblAliasHint.Text = untitled
                ? "Save the .pbix first."
                : locked
                    ? "Stop serving to change the alias."
                    : "Used as the database name while serving.";
        }

        public void UpdateConnectionInfo(int fixedPort)
        {
            _currentFixedPort = fixedPort;
            var data = _presenter.GetDisplayData();
            UpdateConnectionStrings(fixedPort, data.IsServing ? data.DatabaseAlias : null);
        }

        private void UpdateConnectionStrings(int fixedPort, string servingAlias)
        {
            var flow = _grpStrings.Controls[0] as FlowLayoutPanel;
            if (flow == null) return;

            // Called from every panel reposition — only rebuild on actual change.
            string key = $"{fixedPort}|{servingAlias}";
            if (key == _stringsKey) return;
            _stringsKey = key;

            flow.Controls.Clear();
            if (fixedPort > 0)
            {
                AddCopyButton(flow, $"localhost:{fixedPort}", "Host:Port");
                AddCopyButton(flow, $"Data Source=localhost:{fixedPort}", "Connection String");
                if (!string.IsNullOrEmpty(servingAlias))
                {
                    // Alias only holds while the serve session keeps the DB renamed.
                    AddCopyButton(flow,
                        $"Provider=MSOLAP;Data Source=localhost:{fixedPort};Initial Catalog={servingAlias}",
                        "MSOLAP (alias)");
                }
            }
            else
            {
                 flow.Controls.Add(new Label { Text = "Set a Fixed Port to see connection strings.", AutoSize = true, MaximumSize = new Size(LogicalToDeviceUnits(180), 0) });
            }
        }

        private void AddCopyButton(FlowLayoutPanel flow, string text, string label)
        {
            var btn = new Button { Text = $"Copy {label}", Width = LogicalToDeviceUnits(150), Height = LogicalToDeviceUnits(28) };
            btn.Click += (s, e) => { Clipboard.SetText(text); };
            _sharedToolTip.SetToolTip(btn, text);
            flow.Controls.Add(btn);
        }

        private void UpdateLiveStats()
        {
            if (_currentFixedPort <= 0) 
            {
                _lvConnections.Items.Clear();
                return;
            }

            var connections = _presenter.GetActiveConnections(_currentFixedPort);
            
            _lvConnections.BeginUpdate();
            _lvConnections.Items.Clear();
            foreach (var conn in connections)
            {
                var duration = DateTime.Now - conn.ConnectedAt;
                var item = new ListViewItem(conn.RemoteEndpoint);
                item.SubItems.Add(duration.ToString(@"hh\:mm\:ss"));
                _lvConnections.Items.Add(item);
            }
            _lvConnections.EndUpdate();
        }

        private void BtnSaveAlias_Click(object sender, EventArgs e)
        {
            string alias = _txtAlias.Text.Trim();

            // Alias input must pass the same rules as the rename itself (#59
            // polish); an empty box clears the alias and disables serving.
            if (alias.Length > 0)
            {
                var (isValid, error) = AliasValidator.ValidateAlias(alias);
                if (!isValid)
                {
                    MessageBox.Show(error, "Invalid alias", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            try
            {
                _presenter.SaveDatabaseAlias(alias);
                _logCallback?.Invoke(alias.Length > 0
                    ? $"Serve alias set to '{alias}'."
                    : "Serve alias cleared.");
                RefreshData();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save alias: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

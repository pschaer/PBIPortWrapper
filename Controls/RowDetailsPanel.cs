using System;
using System.Drawing;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;
using PBIPortWrapper.Presenters;

namespace PBIPortWrapper.Controls
{
    public class RowDetailsPanel : UserControl
    {
        private readonly RowDetailsPresenter _presenter;

        private GroupBox _grpInfo;
        private GroupBox _grpStrings;

        private Label _lblFile;
        private Label _lblFolder;
        private Label _lblDB;
        private Label _lblAlias;
        private Label _lblServeState;
        private ToolTip _sharedToolTip;

        private string _stringsKey;        // last (url, serving alias) the buttons were built for

        public RowDetailsPanel(RowDetailsPresenter presenter)
        {
            _presenter = presenter;

            InitializeComponent();
            _sharedToolTip = new ToolTip();
            RefreshData();
        }

        



        protected override void Dispose(bool disposing)
        {
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
            layout.ColumnCount = 2;
            layout.RowCount = 1;

            // Two columns since the alias moved to the grid: 0 Info (the remainder),
            // 1 Connection strings (fixed). The Resize handler below indexes these by
            // position, which is precisely what broke when the Connections box was
            // removed in #126 — so any column change here means checking it too.
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(200)));

            _grpInfo = CreateGroupBox("Database Info");
            _grpStrings = CreateGroupBox("Connection Strings");

            layout.Controls.Add(_grpInfo, 0, 0);
            layout.Controls.Add(_grpStrings, 1, 0);

            this.Controls.Add(layout);

            layout.Resize += (s, e) =>
            {
                layout.ColumnStyles[1].SizeType = SizeType.Absolute;
                layout.ColumnStyles[1].Width = S(200);
                // The remainder goes to column 0 via Percent.
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

             // 3. Strings
             var flowStrings = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
             _grpStrings.Controls.Add(flowStrings);
             
             _grpStrings.Resize += (s, e) =>
             {
                 int w = Math.Max(flowStrings.ClientSize.Width - 10, 50);
                 foreach (Control c in flowStrings.Controls) c.Width = w;
             };
        }

        private static string LeafOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
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

            // The workspace path wraps to three or four lines at any sane panel width,
            // which is what put a scrollbar on this box permanently. Show its leaf and
            // keep the whole path one hover away.
            _lblFolder.Text = "Workspace: " + LeafOf(data.WorkspacePath);
            _sharedToolTip.SetToolTip(_lblFolder, data.TooltipText);

            _lblDB.Text = $"DB: {data.DatabaseOriginalName}";

            _lblAlias.Text = string.IsNullOrEmpty(data.DatabaseAlias)
                ? ""
                : $"Alias: {data.DatabaseAlias}";

            UpdateServeState(data);
            UpdateConnectionStrings(
                data.ModelName, data.ConnectionString, data.IsServing ? data.DatabaseAlias : null);
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
                // No port here: serving binds nothing, the model is addressed by name
                // on the endpoint (#126). The old "on port 0" said exactly that, badly.
                _lblServeState.Text = $"Serving as '{data.DatabaseAlias}'";
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

        }

        private void UpdateConnectionStrings(string modelName, string endpointUrl, string servingAlias)
        {
            var flow = _grpStrings.Controls[0] as FlowLayoutPanel;
            if (flow == null) return;

            // Called from every panel reposition — only rebuild on actual change.
            string key = $"{endpointUrl}|{servingAlias}";
            if (key == _stringsKey) return;
            _stringsKey = key;

            flow.Controls.Clear();

            // These describe a live address. A model is reachable only while it is
            // served and the endpoint is running, so outside that there is nothing
            // truthful to offer (#126) — better to say so than to hand out a string
            // that cannot connect.
            if (!string.IsNullOrEmpty(endpointUrl) && !string.IsNullOrEmpty(servingAlias))
            {
                // Same three actions, same wording as the tray's per-model submenu.
                AddCopyButton(flow, "Copy endpoint URL", endpointUrl);
                AddCopyButton(flow, "Copy connection string",
                    ConnectionStringBuilder.ForEndpoint(endpointUrl, servingAlias));

                var odc = new Button
                {
                    Text = "Save .odc…",
                    Width = LogicalToDeviceUnits(150),
                    Height = LogicalToDeviceUnits(28)
                };
                odc.Click += (s, e) => OdcSaveAction.Save(modelName, endpointUrl, servingAlias);
                _sharedToolTip.SetToolTip(odc,
                    "An Excel connection file: double-click it to get a PivotTable on this model.");
                flow.Controls.Add(odc);
            }
            else
            {
                flow.Controls.Add(new Label
                {
                    Text = "Serve this model, with the XMLA endpoint running, to get its connection details.",
                    AutoSize = true,
                    MaximumSize = new Size(LogicalToDeviceUnits(180), 0)
                });
            }
        }

        private void AddCopyButton(FlowLayoutPanel flow, string label, string value)
        {
            var btn = new Button { Text = label, Width = LogicalToDeviceUnits(150), Height = LogicalToDeviceUnits(28) };
            btn.Click += (s, e) => { Clipboard.SetText(value); };
            _sharedToolTip.SetToolTip(btn, value);
            flow.Controls.Add(btn);
        }
    }
}

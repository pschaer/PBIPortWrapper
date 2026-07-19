using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    public class ViewEventCoordinator
    {
        private readonly DataGridView _dataGridView;
        private readonly ValidationService _validationService;
        private readonly GridPresenter _gridPresenter;
        private readonly Action<int> _onToggleExpand;
        private readonly RowActionHandler _actionHandler;
        private readonly ServeActionHandler _serveHandler;
        private readonly Func<int, bool> _isRunningProvider;
        private readonly Func<List<PowerBIInstance>> _instancesProvider;

        public event EventHandler<RowActionEventArgs> ActionRequested;
        public event EventHandler<ConfigChangeEventArgs> ConfigRequested;

        public ViewContextMenuHandler ContextMenuHandler { get; private set; }

        public ViewEventCoordinator(
            DataGridView dataGridView,
            ContextMenuStrip contextMenu,
            ValidationService validationService,
            GridPresenter gridPresenter,
            Func<List<PowerBIInstance>> instancesProvider,
            Func<int, bool> isRunningProvider,
            Action refreshCallback,
            Action<int> onToggleExpand,
            ServeActionHandler serveHandler)
        {
            _dataGridView = dataGridView;
            _validationService = validationService;
            _gridPresenter = gridPresenter;
            _isRunningProvider = isRunningProvider;
            _onToggleExpand = onToggleExpand;
            _instancesProvider = instancesProvider;
            _serveHandler = serveHandler;
            
            ContextMenuHandler = new ViewContextMenuHandler(dataGridView);
            _actionHandler = new RowActionHandler(
                dataGridView, validationService, gridPresenter, instancesProvider);
            
            _actionHandler.ActionRequested += (s, e) => ActionRequested?.Invoke(s, e);
            _actionHandler.ConfigRequested += (s, e) => ConfigRequested?.Invoke(s, e);
        }

        public void OnCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            if (_dataGridView.Columns[e.ColumnIndex].Name == "colExpand")
            {
                // Row identity is WorkspaceId; detail panels remain keyed by ProcessId.
                if (_dataGridView.Rows[e.RowIndex].Tag is string workspaceId)
                {
                    var instance = _instancesProvider().FirstOrDefault(i => i.WorkspaceId == workspaceId);
                    if (instance != null)
                        _onToggleExpand?.Invoke(instance.ProcessId);
                }
                return;
            }

            if (_dataGridView.Columns[e.ColumnIndex].Name == "colServe")
            {
                var row = _dataGridView.Rows[e.RowIndex];
                switch (row.Cells["colServe"].Value?.ToString())
                {
                    case "Serve": _ = _serveHandler.HandleServeAsync(row); break;
                    case "Stop Serving": _ = _serveHandler.HandleStopServingAsync(row); break;
                }
                return;
            }

            if (_dataGridView.Columns[e.ColumnIndex].Name == "colAction")
            {
                var row = _dataGridView.Rows[e.RowIndex];
                string action = row.Cells["colAction"].Value?.ToString();

                if (string.IsNullOrEmpty(action)) return;

                switch (action)
                {
                    case "Set Port": _actionHandler.HandleSetPort(row, e.RowIndex); break;
                    case "Start": _actionHandler.HandleStart(row, e.RowIndex); break;
                    case "Stop": _actionHandler.HandleStop(row); break;
                    case "Remove": _actionHandler.HandleRemove(row); break;
                }
            }
        }

        public void OnCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (_dataGridView.Columns[e.ColumnIndex].Name == "colFixedPort")
            {
                var val = _validationService.ValidatePortAssignment(e.FormattedValue.ToString(), _dataGridView, e.RowIndex);
                if (!val.IsValid)
                {
                    e.Cancel = true;
                    _dataGridView.Rows[e.RowIndex].ErrorText = val.ErrorMessage;
                    if (val.ErrorMessage.Contains("already assigned"))
                        MessageBox.Show(val.ErrorMessage, "Port Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    _dataGridView.Rows[e.RowIndex].ErrorText = string.Empty;
                }
            }
        }

        public void OnCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
             var row = _dataGridView.Rows[e.RowIndex];
             UpdateConfigFromRow(row);
             
             if (row.Cells["colFixedPort"].Value != null && 
                 int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out int port) && port > 0)
             {
                 if (!_isRunningProvider(port))
                     _gridPresenter.SetRowStatus(row, "Ready", Color.Black, "Start", false);
             }
        }
        
        public void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string colName = _dataGridView.Columns[e.ColumnIndex].Name;
            if (colName == "colAuto" || colName == "colNetwork")
                UpdateConfigFromRow(_dataGridView.Rows[e.RowIndex]);
        }
        
        private void UpdateConfigFromRow(DataGridViewRow row)
        {
            string modelName = row.Cells["colModelName"].Value?.ToString();
            int fixedPort = 0;
            if (row.Cells["colFixedPort"].Value != null)
                 int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out fixedPort);
            
            bool auto = Convert.ToBoolean(row.Cells["colAuto"].Value);
            bool network = Convert.ToBoolean(row.Cells["colNetwork"].Value);
            
            ConfigRequested?.Invoke(this, new ConfigChangeEventArgs 
            {
                ModelName = modelName,
                FixedPort = fixedPort,
                Auto = auto,
                AllowNetwork = network,
                Row = row
            });
        }

        public void OnCellEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            if (_dataGridView.Columns[e.ColumnIndex].Name == "colFixedPort")
            {
                var row = _dataGridView.Rows[e.RowIndex];
                // Read-only cell means the row is not configurable right now
                // (running, serving, or Untitled - #9); don't auto-suggest into it.
                if (row.Cells["colFixedPort"].ReadOnly) return;
                if (row.Cells["colFixedPort"].Value == null || string.IsNullOrEmpty(row.Cells["colFixedPort"].Value.ToString()))
                {
                    int suggestedPort = 55555;
                    while (_validationService.IsPortDuplicate(suggestedPort, _dataGridView, e.RowIndex))
                        suggestedPort++;
                    
                    row.Cells["colFixedPort"].Value = suggestedPort;
                    _gridPresenter.SetRowStatus(row, "Ready", Color.Black, "Start", false);
                }
            }
        }

        public void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var hit = _dataGridView.HitTest(e.X, e.Y);
                if (hit.RowIndex >= 0)
                {
                    _dataGridView.ClearSelection();
                    _dataGridView.Rows[hit.RowIndex].Selected = true;
                }
            }
        }
    }
}

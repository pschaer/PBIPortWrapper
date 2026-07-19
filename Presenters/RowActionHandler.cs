using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    public class RowActionHandler
    {
        private readonly DataGridView _dataGridView;
        private readonly ValidationService _validationService;
        private readonly GridPresenter _gridPresenter;
        private readonly Func<List<PowerBIInstance>> _instancesProvider;

        public event EventHandler<RowActionEventArgs> ActionRequested;
        public event EventHandler<ConfigChangeEventArgs> ConfigRequested;

        public RowActionHandler(
            DataGridView dataGridView,
            ValidationService validationService,
            GridPresenter gridPresenter,
            Func<List<PowerBIInstance>> instancesProvider)
        {
            _dataGridView = dataGridView;
            _validationService = validationService;
            _gridPresenter = gridPresenter;
            _instancesProvider = instancesProvider;
        }

        public void HandleSetPort(DataGridViewRow row, int rowIndex)
        {
            int fixedPort = ParsePort(row);
            if (fixedPort == 0)
            {
                fixedPort = SuggestPort(rowIndex);
                row.Cells["colFixedPort"].Value = fixedPort;
            }

            if (fixedPort > 0 && ValidatePort(fixedPort, rowIndex))
            {
                FireConfigUpdate(row);
                _gridPresenter.SetRowStatus(row, "Ready", Color.Black, "Start", false);
            }
        }

        public void HandleStart(DataGridViewRow row, int rowIndex)
        {
            int fixedPort = ParsePort(row);
            string workspaceId = row.Tag as string;
            var instance = _instancesProvider().FirstOrDefault(i => i.WorkspaceId == workspaceId);

            if (instance != null)
            {
                if (ValidatePort(fixedPort, rowIndex))
                {
                    bool allowNetwork = Convert.ToBoolean(row.Cells["colNetwork"].Value);
                    if (allowNetwork)
                    {
                        var result = MessageBox.Show(
                            $"Network Access is enabled for this instance.\nEnsure Windows Firewall allows inbound connections on port {fixedPort}.\n\nContinue?",
                            "Network Access", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        if (result != DialogResult.Yes) return;
                    }

                    FireConfigUpdate(row);
                    
                    ActionRequested?.Invoke(this, new RowActionEventArgs 
                    { 
                        Action = RowActionType.Start,
                        Instance = instance,
                        FixedPort = fixedPort,
                        AllowNetwork = allowNetwork,
                        Row = row
                    });
                }
            }
            else
            {
                MessageBox.Show("Power BI instance not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void HandleStop(DataGridViewRow row)
        {
            int fixedPort = ParsePort(row);
            string workspaceId = row.Tag as string;
            var instance = _instancesProvider().FirstOrDefault(i => i.WorkspaceId == workspaceId);
            // Validation/Warning logic moved to subscriber to avoid dependency on ProxyManager here.

            ActionRequested?.Invoke(this, new RowActionEventArgs
            {
                Action = RowActionType.Stop,
                Instance = instance,
                FixedPort = fixedPort,
                Row = row
            });
        }

        public void HandleRemove(DataGridViewRow row)
        {
            string status = row.Cells["colStatus"].Value?.ToString();
            if (status == "Running")
            {
                MessageBox.Show("Cannot remove configuration while proxy is running.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string modelName = row.Cells["colModelName"].Value?.ToString();
            var result = MessageBox.Show($"Remove configuration for '{modelName}'?", "Confirm Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                ActionRequested?.Invoke(this, new RowActionEventArgs 
                { 
                    Action = RowActionType.Remove,
                    ModelName = modelName,
                    Row = row
                });
                
                _dataGridView.Rows.Remove(row);
            }
        }

        private int ParsePort(DataGridViewRow row)
        {
            if (row.Cells["colFixedPort"].Value != null && int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out int p))
                return p;
            return 0;
        }

        private int SuggestPort(int rowIndex)
        {
            int suggestedPort = 55555;
            while (_validationService.IsPortDuplicate(suggestedPort, _dataGridView, rowIndex))
            {
                suggestedPort++;
            }
            return suggestedPort;
        }

        private bool ValidatePort(int port, int rowIndex)
        {
            if (_validationService.IsPortDuplicate(port, _dataGridView, rowIndex))
            {
                MessageBox.Show($"Port {port} is already assigned to another instance.", "Port Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }
        
        private void FireConfigUpdate(DataGridViewRow row)
        {
            string modelName = row.Cells["colModelName"].Value?.ToString();
            int fixedPort = ParsePort(row);
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
    }
}

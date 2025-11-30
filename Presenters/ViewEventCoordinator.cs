using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    public class ViewEventCoordinator
    {
        private readonly DataGridView _dataGridView;
        private readonly ContextMenuStrip _contextMenu;
        private readonly ProxyManager _proxyManager;
        private readonly ValidationService _validationService;
        private readonly GridPresenter _gridPresenter;
        private readonly ProxyPresenter _proxyPresenter;
        private readonly ConfigPresenter _configPresenter;
        private readonly Func<ProxyConfiguration> _configProvider;
        private readonly Func<List<PowerBIInstance>> _instancesProvider;
        private readonly Action _refreshCallback;

        public ViewContextMenuHandler ContextMenuHandler { get; }

        public ViewEventCoordinator(
            DataGridView dataGridView,
            ContextMenuStrip contextMenu,
            ProxyManager proxyManager,
            ValidationService validationService,
            GridPresenter gridPresenter,
            ProxyPresenter proxyPresenter,
            ConfigPresenter configPresenter,
            Func<ProxyConfiguration> configProvider,
            Func<List<PowerBIInstance>> instancesProvider,
            Action refreshCallback)
        {
            _dataGridView = dataGridView;
            _contextMenu = contextMenu;
            _proxyManager = proxyManager;
            _validationService = validationService;
            _gridPresenter = gridPresenter;
            _proxyPresenter = proxyPresenter;
            _configPresenter = configPresenter;
            _configProvider = configProvider;
            _instancesProvider = instancesProvider;
            _refreshCallback = refreshCallback;
            
            ContextMenuHandler = new ViewContextMenuHandler(dataGridView);
        }

        public async void OnCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            if (_dataGridView.Columns[e.ColumnIndex].Name == "colAction")
            {
                var row = _dataGridView.Rows[e.RowIndex];
                string action = row.Cells["colAction"].Value?.ToString();
                
                if (string.IsNullOrEmpty(action)) return; 

                int fixedPort = 0;
                if (row.Cells["colFixedPort"].Value != null)
                    int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out fixedPort);

                if (action == "Set Port")
                {
                    // Auto-populate Fixed Port with next available port if empty
                    if (fixedPort == 0)
                    {
                        // Use same logic as OnCellEnter - suggest next available port starting from 55555
                        int suggestedPort = 55555;
                        while (_validationService.IsPortDuplicate(suggestedPort, _dataGridView, e.RowIndex))
                        {
                            suggestedPort++;
                        }
                        fixedPort = suggestedPort;
                        row.Cells["colFixedPort"].Value = fixedPort;
                    }
                    
                    // Validate and save the port configuration
                    if (fixedPort > 0)
                    {
                        if (_validationService.IsPortDuplicate(fixedPort, _dataGridView, e.RowIndex))
                        {
                            MessageBox.Show($"Port {fixedPort} is already assigned to another instance.", "Port Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        _configPresenter.UpdateAndSaveRule(row, _configProvider());
                        _gridPresenter.SetRowStatus(row, "Ready", Color.Black, "Start", false);
                    }
                    return;
                }

                if (action == "Start")
                {
                    int? pid = row.Tag as int?;
                    var instance = _instancesProvider().FirstOrDefault(i => i.ProcessId == pid);

                    if (instance != null)
                    {
                        if (_validationService.IsPortDuplicate(fixedPort, _dataGridView, e.RowIndex))
                        {
                            MessageBox.Show($"Port {fixedPort} is already assigned to another instance.", "Port Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        bool allowNetwork = Convert.ToBoolean(row.Cells["colNetwork"].Value);
                        if (allowNetwork)
                        {
                            var result = MessageBox.Show(
                                "Network Access is enabled for this instance.\nEnsure Windows Firewall allows inbound connections on port " + fixedPort + ".\n\nContinue?", 
                                "Network Access", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                            if (result != DialogResult.Yes) return;
                        }

                        await _proxyPresenter.StartProxyAsync(instance, fixedPort, allowNetwork);
                        _configPresenter.UpdateAndSaveRule(row, _configProvider());
                    }
                    else
                    {
                        MessageBox.Show("Power BI instance not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else if (action == "Stop")
                {
                    int activeCount = _proxyManager.GetActiveConnections(fixedPort);
                    if (activeCount > 0)
                    {
                        var result = MessageBox.Show(
                            $"There are {activeCount} active connection(s) to this proxy.\nStopping it will disconnect them.\n\nAre you sure you want to stop?", 
                            "Active Connections Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        
                        if (result != DialogResult.Yes) return;
                    }

                    _proxyPresenter.StopProxy(fixedPort);
                }
                                                                else if (action == "Remove")
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
                        _configPresenter.DeleteConfiguration(modelName, _configProvider());
                        _dataGridView.Rows.Remove(row);
                    }
                }
            }
        }

        public void OnCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (_dataGridView.Columns[e.ColumnIndex].Name == "colFixedPort")
            {
                var validation = _validationService.ValidatePortAssignment(e.FormattedValue.ToString(), _dataGridView, e.RowIndex);
                if (!validation.IsValid)
                {
                    e.Cancel = true;
                    _dataGridView.Rows[e.RowIndex].ErrorText = validation.ErrorMessage;
                    if (validation.ErrorMessage.Contains("already assigned"))
                    {
                        MessageBox.Show(validation.ErrorMessage, "Port Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
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
             _configPresenter.UpdateAndSaveRule(row, _configProvider());
             
             if (row.Cells["colFixedPort"].Value != null && 
                 int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out int port) && port > 0)
             {
                 if (!_proxyManager.IsRunning(port))
                 {
                     _gridPresenter.SetRowStatus(row, "Ready", Color.Black, "Start", false);
                 }
             }
        }
        
        public void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                string colName = _dataGridView.Columns[e.ColumnIndex].Name;
                if (colName == "colAuto" || colName == "colNetwork")
                {
                    _configPresenter.UpdateAndSaveRule(_dataGridView.Rows[e.RowIndex], _configProvider());
                }
            }
        }

        public void OnCellEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            if (_dataGridView.Columns[e.ColumnIndex].Name == "colFixedPort")
            {
                var row = _dataGridView.Rows[e.RowIndex];
                if (row.Cells["colFixedPort"].Value == null || string.IsNullOrEmpty(row.Cells["colFixedPort"].Value.ToString()))
                {
                    // Suggest next available port
                    int suggestedPort = 55555;
                    while (_validationService.IsPortDuplicate(suggestedPort, _dataGridView, e.RowIndex))
                    {
                        suggestedPort++;
                    }
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
                    _contextMenu.Show(_dataGridView, e.Location);
                }
            }
        }
    }
}

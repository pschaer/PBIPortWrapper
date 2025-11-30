using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    public class GridSyncHelper
    {
        private readonly DataGridView _dataGridView;
        private readonly ProxyManager _proxyManager;
        private readonly ProxyConfiguration _config;
        private readonly Action<string> _logCallback;
        private readonly Action<DataGridViewRow, string, Color, string, bool> _setRowStatus;

        public GridSyncHelper(
            DataGridView dataGridView,
            ProxyManager proxyManager,
            ProxyConfiguration config,
            Action<string> logCallback,
            Action<DataGridViewRow, string, Color, string, bool> setRowStatus)
        {
            _dataGridView = dataGridView;
            _proxyManager = proxyManager;
            _config = config;
            _logCallback = logCallback;
            _setRowStatus = setRowStatus;
        }

                                public void RefreshGrid(List<PowerBIInstance> instances)
        {
            RefreshGrid(instances, _config);
        }

                public void RefreshGrid(List<PowerBIInstance> instances, ProxyConfiguration config)
        {
            var processedRows = new HashSet<DataGridViewRow>();

            // 1. Update existing rows with detected instances
            foreach (var instance in instances)
            {
                DataGridViewRow row = null;

                // Priority 1: Match by ProcessId (Tag)
                row = _dataGridView.Rows
                    .Cast<DataGridViewRow>()
                    .FirstOrDefault(r => (r.Tag as int?) == instance.ProcessId);

                // Priority 2: Match by Name (First come, first served)
                if (row == null)
                {
                    row = _dataGridView.Rows
                        .Cast<DataGridViewRow>()
                        .FirstOrDefault(r => 
                        {
                            return r.Cells["colModelName"].Value?.ToString() == instance.FileName && r.Tag == null;
                        });
                }

                if (row == null)
                {
                    // New Row
                    int rowIndex = _dataGridView.Rows.Add();
                    row = _dataGridView.Rows[rowIndex];
                    
                    // Apply saved rule if exists
                    var rule = config.PortMappings.FirstOrDefault(r => r.ModelNamePattern == instance.FileName);

                    // CHECK: Is this rule already active on another row?
                    bool isRuleActive = false;
                    if (rule != null)
                    {
                        foreach (DataGridViewRow r in _dataGridView.Rows)
                        {
                            if (r == row) continue;
                            
                            string rName = r.Cells["colModelName"].Value?.ToString();
                            if (rName == instance.FileName)
                            {
                                if (r.Cells["colFixedPort"].Value != null && 
                                    !string.IsNullOrEmpty(r.Cells["colFixedPort"].Value.ToString()))
                                {
                                    isRuleActive = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (rule != null && !isRuleActive)
                    {
                        row.Cells["colFixedPort"].Value = rule.FixedPort;
                        row.Cells["colAuto"].Value = rule.AutoConnect;
                        row.Cells["colNetwork"].Value = rule.AllowNetworkAccess;
                    }
                    else
                    {
                        row.Cells["colFixedPort"].Value = null; 
                        row.Cells["colAuto"].Value = false;
                        row.Cells["colNetwork"].Value = false;
                        
                        if (isRuleActive)
                        {
                            _logCallback($"Duplicate instance detected for '{instance.FileName}'. Configuration not applied to avoid conflict.");
                        }
                    }
                }

                // MERGE LOGIC
                if (row != null)
                {
                    var offlineRow = _dataGridView.Rows
                        .Cast<DataGridViewRow>()
                        .FirstOrDefault(r => 
                            r != row && 
                            r.Tag == null && 
                            r.Cells["colModelName"].Value?.ToString() == instance.FileName);

                    if (offlineRow != null)
                    {
                        if (offlineRow.Cells["colFixedPort"].Value != null)
                            row.Cells["colFixedPort"].Value = offlineRow.Cells["colFixedPort"].Value;
                            
                        row.Cells["colAuto"].Value = offlineRow.Cells["colAuto"].Value;
                        row.Cells["colNetwork"].Value = offlineRow.Cells["colNetwork"].Value;
                        
                        _dataGridView.Rows.Remove(offlineRow);
                        _logCallback($"Merged offline config for '{instance.FileName}' into running instance.");
                    }
                }

                // Update Row Data
                row.Tag = instance.ProcessId;
                row.Cells["colModelName"].Value = instance.FileName;
                row.Cells["colModelName"].ToolTipText = $"Name: {instance.FileName}\nPath: {instance.FilePath}";
                row.Cells["colPbiPort"].Value = instance.Port;

                // Update Status
                int fixedPort = 0;
                if (row.Cells["colFixedPort"].Value != null && int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out int fp))
                {
                    fixedPort = fp;
                }

                if (fixedPort > 0 && _proxyManager.IsRunning(fixedPort))
                {
                    _setRowStatus(row, "Running", Color.Green, "Stop", true);
                    int activeCount = _proxyManager.GetActiveConnections(fixedPort);
                    row.Cells["colActive"].Value = activeCount;
                }
                else
                {
                    if (fixedPort > 0)
                        _setRowStatus(row, "Ready", Color.Black, "Start", false);
                    else
                        _setRowStatus(row, "Ready", Color.Black, "Set Port", false);
                    
                    row.Cells["colActive"].Value = "";
                }

                processedRows.Add(row);
            }

            // 2. Handle rows that are NOT in the detected list
            var rowsToRemove = new List<DataGridViewRow>();
            
            foreach (DataGridViewRow row in _dataGridView.Rows)
            {
                if (processedRows.Contains(row)) continue;

                int fixedPort = 0;
                if (row.Cells["colFixedPort"].Value != null)
                {
                    int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out fixedPort);
                }

                if (fixedPort > 0 && _proxyManager.IsRunning(fixedPort))
                {
                    _proxyManager.StopProxy(fixedPort);
                    _logCallback($"Auto-stopped proxy on port {fixedPort} (PBI closed)");
                }

                string modelName = row.Cells["colModelName"].Value?.ToString();
                
                var rule = config.PortMappings.FirstOrDefault(r => r.ModelNamePattern == modelName);

                if (rule != null)
                {
                    row.Tag = null;
                    row.Cells["colPbiPort"].Value = "";
                    row.Cells["colModelName"].ToolTipText = $"Name: {modelName}\n(Offline)";
                    
                    _setRowStatus(row, "Offline", Color.Gray, "Remove", false); 
                    row.Cells["colActive"].Value = "";
                }
                else
                {
                    rowsToRemove.Add(row);
                }
            }

                        foreach (var gridRow in rowsToRemove)
            {
                _dataGridView.Rows.Remove(gridRow);
            }
            
                        // 3. Ensure all saved configs have a row
            foreach (var rule in config.PortMappings)
            {
                bool exists = false;
                foreach (DataGridViewRow row in _dataGridView.Rows)
                {
                    string rowName = row.Cells["colModelName"].Value?.ToString();
                    if (rowName == rule.ModelNamePattern)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    int rowIndex = _dataGridView.Rows.Add();
                    DataGridViewRow row = _dataGridView.Rows[rowIndex];
                    
                    row.Cells["colModelName"].Value = rule.ModelNamePattern;
                    row.Cells["colModelName"].ToolTipText = $"Name: {rule.ModelNamePattern}\n(Offline)";
                    row.Cells["colFixedPort"].Value = rule.FixedPort;
                    row.Cells["colAuto"].Value = rule.AutoConnect;
                    row.Cells["colNetwork"].Value = rule.AllowNetworkAccess;
                    row.Cells["colPbiPort"].Value = "";
                    _setRowStatus(row, "Offline", Color.Gray, "Remove", false);
                    row.Cells["colActive"].Value = "";
                }
            }
        }
    }
}

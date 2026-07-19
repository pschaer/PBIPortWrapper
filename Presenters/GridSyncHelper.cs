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
        private readonly RowStatusPainter _painter;
        private readonly DetailRowManager _detailRowManager;

        public GridSyncHelper(
            DataGridView dataGridView,
            ProxyManager proxyManager,
            ProxyConfiguration config,
            Action<string> logCallback,
            RowStatusPainter painter)
        {
            _dataGridView = dataGridView;
            _proxyManager = proxyManager;
            _config = config;
            _logCallback = logCallback;
            _painter = painter;
            _detailRowManager = new DetailRowManager();
        }

        public void RefreshGrid(List<PowerBIInstance> instances) => RefreshGrid(instances, _config);

        public void RefreshGrid(List<PowerBIInstance> instances, ProxyConfiguration config, HashSet<int> expandedPids = null)
        {
            var processedRows = new HashSet<DataGridViewRow>();

            foreach (var instance in instances)
            {
                var row = FindRow(instance);

                if (row == null)
                {
                    row = _dataGridView.Rows[_dataGridView.Rows.Add()];
                    ApplyInitialConfig(row, instance, config);
                }
                else
                {
                    MergeOfflineConfig(row, instance);
                }

                UpdateRowData(row, instance, expandedPids);
                processedRows.Add(row);

                // Detail Row Logic
                bool isExpanded = expandedPids?.Contains(instance.ProcessId) ?? false;
                _detailRowManager.HandleDetailRow(_dataGridView, row.Index, isExpanded, instance.ProcessId);
                
                if (isExpanded && row.Index + 1 < _dataGridView.Rows.Count)
                {
                    var detailRow = _dataGridView.Rows[row.Index + 1];
                    if (_detailRowManager.IsDetailRowFor(detailRow, instance.ProcessId))
                        processedRows.Add(detailRow);
                }

                _painter.Paint(row);
            }

            RemoveStaleRows(processedRows, config);
            EnsureConfigRows(config);
        }

        private DataGridViewRow FindRow(PowerBIInstance instance)
        {
            // Identity is the workspace dir name: unique per session, never 0/unknown
            // (unlike ProcessId, which is 0 when WMI denies CommandLine access).
            return _dataGridView.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(r => (r.Tag as string) == instance.WorkspaceId)
                ?? _dataGridView.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(r => r.Cells["colModelName"].Value?.ToString() == instance.FileName && !IsDetail(r));
        }

        private bool IsDetail(DataGridViewRow row) => _detailRowManager.IsDetailRow(row);

        private void ApplyInitialConfig(DataGridViewRow row, PowerBIInstance instance, ProxyConfiguration config)
        {
            var rule = config.PortMappings.FirstOrDefault(r => r.ModelNamePattern == instance.FileName);
            bool isActive = rule != null && _dataGridView.Rows.Cast<DataGridViewRow>()
                .Any(r => r != row && !IsDetail(r) && r.Cells["colModelName"].Value?.ToString() == instance.FileName && 
                          r.Cells["colFixedPort"].Value?.ToString().Length > 0);

            if (rule != null && !isActive)
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
                if (isActive) _logCallback($"Duplicate instance '{instance.FileName}'. Config skipped.");
            }
        }

        private void MergeOfflineConfig(DataGridViewRow row, PowerBIInstance instance)
        {
            var offlineRow = _dataGridView.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(r => r != row && !IsDetail(r) && r.Cells["colModelName"].Value?.ToString() == instance.FileName);

            if (offlineRow != null)
            {
                if (offlineRow.Cells["colFixedPort"].Value != null)
                    row.Cells["colFixedPort"].Value = offlineRow.Cells["colFixedPort"].Value;
                row.Cells["colAuto"].Value = offlineRow.Cells["colAuto"].Value;
                row.Cells["colNetwork"].Value = offlineRow.Cells["colNetwork"].Value;
                
                _dataGridView.Rows.Remove(offlineRow);
                _logCallback($"Merged offline config for '{instance.FileName}'.");
            }
        }

        private void UpdateRowData(DataGridViewRow row, PowerBIInstance instance, HashSet<int> expandedPids)
        {
            row.Tag = instance.WorkspaceId;

            string newName = instance.FileName;
            // Only update cell if value changed to prevent grid focus stealing
            if (row.Cells["colModelName"].Value?.ToString() != newName)
            {
                row.Cells["colModelName"].Value = newName;
                // "Workspace:" not "Path:" — FilePath is the AS workspace dir, not
                // the .pbix (#59 polish); ViewContextMenuHandler parses this label.
                row.Cells["colModelName"].ToolTipText = $"Name: {instance.FileName}\nWorkspace: {instance.FilePath}";
            }

            int newPort = instance.Port;
            bool portChanged = true;
            if (row.Cells["colPbiPort"].Value is int oldPort && oldPort == newPort)
            {
                portChanged = false;
            }

            if (portChanged)
            {
                row.Cells["colPbiPort"].Value = newPort;
            }

            string newExpand = (expandedPids?.Contains(instance.ProcessId) ?? false) ? "▼" : "▶";
            if (row.Cells["colExpand"].Value?.ToString() != newExpand)
            {
                row.Cells["colExpand"].Value = newExpand;
            }
        }

        private void RemoveStaleRows(HashSet<DataGridViewRow> processedRows, ProxyConfiguration config)
        {
            var toRemove = new List<DataGridViewRow>();
            foreach (DataGridViewRow row in _dataGridView.Rows)
            {
                if (processedRows.Contains(row)) continue;
                if (IsDetail(row)) { toRemove.Add(row); continue; }

                int port = 0;
                if (row.Cells["colFixedPort"].Value != null) int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out port);

                if (port > 0 && _proxyManager.IsRunning(port))
                {
                    _proxyManager.StopProxy(port);
                    _logCallback($"Auto-stopped proxy {port} (PBI closed)");
                }

                string name = row.Cells["colModelName"].Value?.ToString();
                if (config.PortMappings.Any(r => r.ModelNamePattern == name))
                {
                    row.Tag = null;
                    row.Cells["colPbiPort"].Value = "";
                    row.Cells["colExpand"].Value = "";
                    row.Cells["colModelName"].ToolTipText = $"Name: {name}\n(Offline)";
                    _painter.PaintOffline(row);
                }
                else
                {
                    toRemove.Add(row);
                }
            }
            toRemove.ForEach(r => _dataGridView.Rows.Remove(r));
        }

        private void EnsureConfigRows(ProxyConfiguration config)
        {
            foreach (var rule in config.PortMappings)
            {
                if (_dataGridView.Rows.Cast<DataGridViewRow>().Any(r => !IsDetail(r) && r.Cells["colModelName"].Value?.ToString() == rule.ModelNamePattern)) continue;

                var row = _dataGridView.Rows[_dataGridView.Rows.Add()];
                row.Cells["colModelName"].Value = rule.ModelNamePattern;
                row.Cells["colModelName"].ToolTipText = $"Name: {rule.ModelNamePattern}\n(Offline)";
                row.Cells["colFixedPort"].Value = rule.FixedPort;
                row.Cells["colAuto"].Value = rule.AutoConnect;
                row.Cells["colNetwork"].Value = rule.AllowNetworkAccess;
                row.Cells["colPbiPort"].Value = "";
                row.Cells["colExpand"].Value = "";
                _painter.PaintOffline(row);
            }
        }
    }
}

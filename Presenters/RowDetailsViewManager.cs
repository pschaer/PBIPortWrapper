using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PBIPortWrapper.Controls;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    public class RowDetailsViewManager
    {
        private readonly DataGridView _dataGridView;
        private readonly ProxyManager _proxyManager;
        private readonly ConfigService _configService;
        private readonly ServeSessionService _serveSessions;
        private readonly Action<string> _logCallback;
        private readonly Dictionary<int, RowDetailsPanel> _panels = new Dictionary<int, RowDetailsPanel>();
        private readonly DetailRowManager _detailRowManager = new DetailRowManager();

        public RowDetailsViewManager(
            DataGridView dataGridView,
            ProxyManager proxyManager,
            ConfigService configService,
            ServeSessionService serveSessions,
            Action<string> logCallback)
        {
            _dataGridView = dataGridView;
            _proxyManager = proxyManager;
            _configService = configService;
            _serveSessions = serveSessions;
            _logCallback = logCallback;

            _dataGridView.Scroll += (s, e) => UpdatePanelPositions();
            _dataGridView.Resize += (s, e) => UpdatePanelPositions();
        }

        public void SyncDetailsPanels(List<PowerBIInstance> instances, HashSet<int> expandedPids)
        {
            if (expandedPids == null) expandedPids = new HashSet<int>();

            // 1. Remove panels for non-expanded or missing instances
            var toRemove = _panels.Keys.Where(k => !expandedPids.Contains(k)).ToList();
            foreach (var pid in toRemove)
            {
                if (_panels.TryGetValue(pid, out var panel))
                {
                    _dataGridView.Controls.Remove(panel);
                    panel.Dispose();
                    _panels.Remove(pid);
                }
            }

            // 2. Create panels for new expanded instances, update existing
            foreach (var pid in expandedPids)
            {
                var instance = instances.FirstOrDefault(i => i.ProcessId == pid);
                if (instance == null) continue;

                if (!_panels.ContainsKey(pid))
                {
                    var presenter = new RowDetailsPresenter(instance, _proxyManager, _configService, _serveSessions);
                    var panel = new RowDetailsPanel(presenter, _logCallback);
                    _dataGridView.Controls.Add(panel);
                    _panels[pid] = panel;
                }
                else
                {
                    // Each scan rebuilds the instance list; hand the panel the fresh
                    // object so live values (DB name after a serve rename) show up.
                    _panels[pid].UpdateInstance(instance);
                }
            }

            UpdatePanelPositions();
        }

        public void UpdatePanelPositions()
        {
            foreach (var kvp in _panels)
            {
                int pid = kvp.Key;
                var panel = kvp.Value;
                bool visible = false;

                // Find the Detail Row for this PID
                foreach (DataGridViewRow row in _dataGridView.Rows)
                {
                     if (_detailRowManager.IsDetailRowFor(row, pid))
                     {
                         if (row.Displayed)
                         {
                             var rect = _dataGridView.GetRowDisplayRectangle(row.Index, true);
                             if (panel.Left != rect.X || panel.Top != rect.Y || panel.Width != rect.Width || panel.Height != rect.Height)
                             {
                                 panel.SetBounds(rect.X, rect.Y, rect.Width, rect.Height);
                             }
                             panel.Visible = true;
                             visible = true;

                             // Update Panel Data (Port)
                             if (row.Index > 0)
                             {
                                 var parentRow = _dataGridView.Rows[row.Index - 1];
                                 if (parentRow.Cells["colFixedPort"].Value != null && 
                                     int.TryParse(parentRow.Cells["colFixedPort"].Value.ToString(), out int port))
                                 {
                                     panel.UpdateConnectionInfo(port);
                                 }
                                 else
                                 {
                                     panel.UpdateConnectionInfo(0);
                                 }
                             }
                         }
                         break;
                     }
                }

                if (!visible)
                {
                    panel.Visible = false;
                }
            }
        }
    }
}

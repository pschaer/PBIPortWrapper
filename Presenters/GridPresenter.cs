using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    // FILE SIZE: MAX 250 lines - enforced by build target
    public class GridPresenter
    {
        private readonly DataGridView _dataGridView;
        private readonly RowStatusPainter _painter;
        private readonly GridSyncHelper _syncHelper;
        private readonly DetailRowManager _detailRowManager = new DetailRowManager();

        // NOTE: Config is READ-ONLY in Presenter. Writes must go through ConfigService.

        public GridPresenter(
            DataGridView dataGridView,
            ProxyManager proxyManager,
            ValidationService validationService,
            ProxyConfiguration config,
            Func<string, ServeSession> sessionLookup,
            Func<string, PortMappingRule> ruleLookup,
            Action<string> logCallback)
        {
            _dataGridView = dataGridView;

            _painter = new RowStatusPainter(
                proxyManager, sessionLookup, ruleLookup, SetRowStatus, logCallback);

            _syncHelper = new GridSyncHelper(
                dataGridView,
                proxyManager,
                config,
                logCallback,
                _painter);
        }

        public void RefreshGrid(List<PowerBIInstance> instances)
        {
            _syncHelper.RefreshGrid(instances);
        }

        public void RefreshGrid(List<PowerBIInstance> instances, ProxyConfiguration config, HashSet<int> expandedPids)
        {
            _syncHelper.RefreshGrid(instances, config, expandedPids);
        }

        public void SetRowStatus(DataGridViewRow row, string status, Color color, string actionText, bool isReadOnly)
        {
            row.Cells["colStatus"].Value = status;
            row.Cells["colStatus"].Style.ForeColor = color;
            row.Cells["colAction"].Value = actionText;
            row.Cells["colFixedPort"].ReadOnly = isReadOnly;
            row.Cells["colNetwork"].ReadOnly = isReadOnly;
        }

        /// <summary>
        /// Event path (proxy started/stopped, serve session started/ended):
        /// repaints the rows on the given fixed port from current state.
        /// </summary>
        public void UpdateGridStatus(int fixedPort)
        {
            if (_dataGridView.InvokeRequired)
            {
                _dataGridView.Invoke(new Action(() => UpdateGridStatus(fixedPort)));
                return;
            }

            foreach (DataGridViewRow row in _dataGridView.Rows)
            {
                if (row.Cells["colFixedPort"].Value != null &&
                    int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out int fp) && fp == fixedPort)
                {
                    _painter.Paint(row);
                }
            }
        }

        /// <summary>
        /// Repaints every main row; used when serve/config state changes without a
        /// new instance snapshot (alias saved, session started or ended).
        /// </summary>
        public void RepaintAllRows()
        {
            if (!_dataGridView.IsHandleCreated) return;
            _dataGridView.BeginInvoke(new Action(() =>
            {
                foreach (DataGridViewRow row in _dataGridView.Rows)
                {
                    if (!_detailRowManager.IsDetailRow(row))
                        _painter.Paint(row);
                }
            }));
        }

        public void UpdateActiveConnections(int fixedPort, int count)
        {
            if (_dataGridView.InvokeRequired)
            {
                _dataGridView.Invoke(new Action(() => UpdateActiveConnections(fixedPort, count)));
                return;
            }

            foreach (DataGridViewRow row in _dataGridView.Rows)
            {
                if (row.Cells["colFixedPort"].Value != null &&
                    int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out int fp) && fp == fixedPort)
                {
                    row.Cells["colActive"].Value = count;
                }
            }
        }
    }
}

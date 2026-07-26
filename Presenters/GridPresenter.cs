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
            ProxyConfiguration config,
            Func<string, ServeSession> sessionLookup,
            Func<string, ModelRule> ruleLookup,
            Action<string> logCallback)
        {
            _dataGridView = dataGridView;

            _painter = new RowStatusPainter(
                sessionLookup, ruleLookup, SetRowStatus, logCallback);

            _syncHelper = new GridSyncHelper(
                dataGridView,
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
            row.Cells["colAlias"].ReadOnly = isReadOnly;
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
    }
}

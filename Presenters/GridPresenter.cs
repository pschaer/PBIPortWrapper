using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    // FILE SIZE: Current=~100 lines (Reduced from 300)
    // MAX 300 lines - enforced in code review
    public class GridPresenter
    {
        private readonly DataGridView _dataGridView;
        private readonly ProxyManager _proxyManager;
        private readonly GridSyncHelper _syncHelper;
        
        // NOTE: Config is READ-ONLY in Presenter. Writes must go through ConfigPresenter.
        // We don't store _config here anymore, it's in the helper.

        public GridPresenter(
            DataGridView dataGridView, 
            ProxyManager proxyManager, 
            ValidationService validationService, 
            ProxyConfiguration config,
            Action<string> logCallback)
        {
            _dataGridView = dataGridView;
            _proxyManager = proxyManager;
            
            // Initialize Helper
            _syncHelper = new GridSyncHelper(
                dataGridView, 
                proxyManager, 
                config, 
                logCallback, 
                SetRowStatus // Pass delegate
            );
        }

                public void RefreshGrid(List<PowerBIInstance> instances)
        {
            _syncHelper.RefreshGrid(instances);
        }

        public void RefreshGrid(List<PowerBIInstance> instances, ProxyConfiguration config)
        {
            _syncHelper.RefreshGrid(instances, config);
        }

        public void SetRowStatus(DataGridViewRow row, string status, Color color, string actionText, bool isReadOnly)
        {
            row.Cells["colStatus"].Value = status;
            row.Cells["colStatus"].Style.ForeColor = color;
            row.Cells["colAction"].Value = actionText;
            row.Cells["colFixedPort"].ReadOnly = isReadOnly;
            row.Cells["colNetwork"].ReadOnly = isReadOnly;
        }

        public void UpdateGridStatus(int fixedPort, bool isRunning)
        {
            if (_dataGridView.InvokeRequired)
            {
                _dataGridView.Invoke(new Action(() => UpdateGridStatus(fixedPort, isRunning)));
                return;
            }

            foreach (DataGridViewRow row in _dataGridView.Rows)
            {
                if (row.Cells["colFixedPort"].Value != null && 
                    int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out int fp) && fp == fixedPort)
                {
                    if (isRunning)
                    {
                        SetRowStatus(row, "Running", Color.Green, "Stop", true);
                        int activeCount = _proxyManager.GetActiveConnections(fixedPort);
                        row.Cells["colActive"].Value = activeCount;
                    }
                    else
                    {
                        string pbiPort = row.Cells["colPbiPort"].Value?.ToString();
                        if (!string.IsNullOrEmpty(pbiPort))
                        {
                            SetRowStatus(row, "Ready", Color.Black, "Start", false);
                        }
                        else
                        {
                            SetRowStatus(row, "Offline", Color.Gray, "Remove", false);
                        }
                        row.Cells["colActive"].Value = "";
                    }
                }
            }
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

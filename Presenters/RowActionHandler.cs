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
        private readonly GridPresenter _gridPresenter;
        private readonly Func<List<PowerBIInstance>> _instancesProvider;
        private readonly Func<string, ModelRule> _ruleLookup;

        public event EventHandler<RowActionEventArgs> ActionRequested;
        public event EventHandler<ConfigChangeEventArgs> ConfigRequested;

        public RowActionHandler(
            DataGridView dataGridView,
            GridPresenter gridPresenter,
            Func<List<PowerBIInstance>> instancesProvider,
            Func<string, ModelRule> ruleLookup)
        {
            _dataGridView = dataGridView;
            _gridPresenter = gridPresenter;
            _instancesProvider = instancesProvider;
            _ruleLookup = ruleLookup;
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
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PBIRelay.Models;
using PBIRelay.Services;

namespace PBIRelay.Presenters
{
    public class ViewEventCoordinator
    {
        private readonly DataGridView _dataGridView;
        private readonly GridPresenter _gridPresenter;
        private readonly Action<int> _onToggleExpand;
        private readonly RowActionHandler _actionHandler;
        private readonly ServeActionHandler _serveHandler;
        private readonly Func<List<PowerBIInstance>> _instancesProvider;
        private readonly Func<string, ModelRule> _ruleLookup;
        private readonly Func<Point, int?> _panelPidAt;

        public event EventHandler<RowActionEventArgs> ActionRequested;
        public event EventHandler<ConfigChangeEventArgs> ConfigRequested;

        public ViewContextMenuHandler ContextMenuHandler { get; private set; }

        public ViewEventCoordinator(
            DataGridView dataGridView,
            ContextMenuStrip contextMenu,
            GridPresenter gridPresenter,
            Func<List<PowerBIInstance>> instancesProvider,
            Action refreshCallback,
            Action<int> onToggleExpand,
            ServeActionHandler serveHandler,
            Func<string, ModelRule> ruleLookup,
            Func<string, bool> isServing,
            Func<Point, int?> panelPidAt,
            Action<string> logCallback)
        {
            _dataGridView = dataGridView;
            _gridPresenter = gridPresenter;
            _onToggleExpand = onToggleExpand;
            _instancesProvider = instancesProvider;
            _serveHandler = serveHandler;
            _ruleLookup = ruleLookup;
            _panelPidAt = panelPidAt;

            ContextMenuHandler = new ViewContextMenuHandler(logCallback);
            _actionHandler = new RowActionHandler(
                dataGridView, gridPresenter, instancesProvider, ruleLookup);

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

            if (_dataGridView.Columns[e.ColumnIndex].Name == "colAction")
            {
                var row = _dataGridView.Rows[e.RowIndex];
                // The cell says what pressing it does, so it just does that (#126).
                // With one action available per state, a menu to reveal a single item
                // was pure friction.
                string action = row.Cells["colAction"].Value?.ToString();
                if (action == HostActionLabel.For(HostAction.Serve)) _ = _serveHandler.HandleServeAsync(row);
                else if (action == HostActionLabel.For(HostAction.Stop)) _ = _serveHandler.HandleStopServingAsync(row);
                else if (action == "Remove") _actionHandler.HandleRemove(row);
                else if (action == "Set name") FocusAliasCell(row);
            }
        }

        // Every config cell persists from the user's own commit — the On-detection
        // dropdown on CurrentCellDirtyStateChanged, the alias on CellEndEdit, both
        // wired in MainForm. RowStatusPainter's projection is display-only, so nothing
        // it writes can loop back into config.

        /// <summary>Puts the caret in the alias cell, which is what "Set name" means.</summary>
        private void FocusAliasCell(DataGridViewRow row)
        {
            var cell = row.Cells["colAlias"];
            if (cell.ReadOnly) return;

            _dataGridView.CurrentCell = cell;
            _dataGridView.BeginEdit(selectAll: true);
        }

        public void OnCellEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
        }

        /// <summary>
        /// Decides which model the grid context menu is about, from where the pointer is.
        ///
        /// This replaces reading the grid's selection, which could not work inside an
        /// expanded details panel (#151) - see <see cref="ViewContextMenuHandler"/>. The
        /// panels are checked first because they cover their row.
        /// </summary>
        public void OnContextMenuOpening(object sender, CancelEventArgs e)
        {
            var point = _dataGridView.PointToClient(Control.MousePosition);

            int? panelPid = _panelPidAt?.Invoke(point);
            if (panelPid.HasValue)
            {
                var panelInstance = _instancesProvider()
                    .FirstOrDefault(i => i.ProcessId == panelPid.Value);
                Target(panelInstance, RowOf(panelInstance?.WorkspaceId));
                return;
            }

            var hit = _dataGridView.HitTest(point.X, point.Y);
            if (hit.RowIndex < 0)
            {
                // Empty space below the rows: every item would be about nothing, and an
                // item that quietly does nothing is the bug this fixes.
                e.Cancel = true;
                return;
            }

            var row = _dataGridView.Rows[hit.RowIndex];
            // An offline row's Tag is cleared when its instance goes away, so this
            // resolves to null and the menu says the model is not running.
            Target(_instancesProvider().FirstOrDefault(i => i.WorkspaceId == (row.Tag as string)), row);
        }

        /// <summary>
        /// Points the menu at an instance and highlights the row it belongs to, so the
        /// model the menu will act on is visible before anything is clicked.
        /// </summary>
        private void Target(PowerBIInstance instance, DataGridViewRow row)
        {
            ContextMenuHandler.SetTarget(instance);

            _dataGridView.ClearSelection();
            if (row != null) row.Selected = true;
        }

        private DataGridViewRow RowOf(string workspaceId)
        {
            if (string.IsNullOrEmpty(workspaceId)) return null;
            return _dataGridView.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(r => (r.Tag as string) == workspaceId);
        }
    }
}

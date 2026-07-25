using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    // FILE SIZE: MAX 250 lines - enforced by build target
    /// <summary>
    /// Builds and shows the grid's single "Action" menu for a row from the same
    /// <see cref="HostStateMachine.AvailableActions"/> + <see cref="HostActionLabel"/>
    /// the tray uses (#88), so grid and tray offer an identical action set. Forward/Stop
    /// reuse the grid's RowActionHandler (its port validation and warnings); Serve /
    /// Stop-serving reuse the ServeActionHandler. The row is in exactly one state, so a
    /// menu with the actions available from that state replaces the old Action + Serve
    /// columns.
    /// </summary>
    public class RowActionMenuBuilder
    {
        private readonly RowActionHandler _actionHandler;
        private readonly ServeActionHandler _serveHandler;
        private readonly Func<int, bool> _isRunning;
        private readonly Func<string, bool> _isServing;

        public RowActionMenuBuilder(
            RowActionHandler actionHandler,
            ServeActionHandler serveHandler,
            Func<int, bool> isRunning,
            Func<string, bool> isServing)
        {
            _actionHandler = actionHandler;
            _serveHandler = serveHandler;
            _isRunning = isRunning;
            _isServing = isServing;
        }

        /// <summary>Shows the available-actions menu for a live, configured row at the given screen point.</summary>
        public void ShowFor(DataGridViewRow row, int rowIndex, Point screenLocation)
        {
            string ws = row.Tag as string;
            int port = 0;
            if (row.Cells["colFixedPort"].Value != null)
                int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out port);

            bool serving = !string.IsNullOrEmpty(ws) && _isServing(ws);
            bool forwarding = port > 0 && _isRunning(port);
            var state = HostStateMachine.CurrentState(serving, forwarding);

            var actions = HostStateMachine.AvailableActions(state);
            if (actions.Count == 0) return;

            var menu = new ContextMenuStrip();
            foreach (var action in actions)
            {
                var captured = action;
                menu.Items.Add(new ToolStripMenuItem(HostActionLabel.For(action), null,
                    (s, e) => Dispatch(captured, row, rowIndex)));
            }
            menu.Show(screenLocation);
        }

        private void Dispatch(HostAction action, DataGridViewRow row, int rowIndex)
        {
            switch (action)
            {
                case HostAction.Forward: _actionHandler.HandleStart(row, rowIndex); break;
                case HostAction.Stop: _actionHandler.HandleStop(row); break;
                case HostAction.Serve: _ = _serveHandler.HandleServeAsync(row); break;
                case HostAction.StopServing: _ = _serveHandler.HandleStopServingAsync(row); break;
            }
        }
    }
}

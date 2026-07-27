using System;
using System.Drawing;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    // FILE SIZE: MAX 250 lines - enforced by build target
    /// <summary>
    /// Single decision point for a row's Status and Action cells, shared by the
    /// snapshot refresh (GridSyncHelper) and the serve event path (GridPresenter) so
    /// the two can never disagree about what a row is doing.
    /// </summary>
    public class RowStatusPainter
    {
        private readonly Func<string, ServeSession> _sessionLookup; // by workspace id
        private readonly Func<string, ModelRule> _ruleLookup; // by model name
        private readonly Action<DataGridViewRow, string, Color, string, bool> _setRowStatus;
        private readonly Action<string> _log;

        public RowStatusPainter(
            Func<string, ServeSession> sessionLookup,
            Func<string, ModelRule> ruleLookup,
            Action<DataGridViewRow, string, Color, string, bool> setRowStatus,
            Action<string> log)
        {
            _sessionLookup = sessionLookup;
            _ruleLookup = ruleLookup;
            _setRowStatus = setRowStatus;
            _log = log;
        }

        /// <summary>Repaints one main grid row from current serve state.</summary>
        public void Paint(DataGridViewRow row)
        {
            bool live = !string.IsNullOrEmpty(row.Cells["colPbiPort"].Value?.ToString());
            if (!live)
            {
                PaintOffline(row);
                return;
            }

            ProjectPolicy(row);

            // #9: config rules match by file name, so an unsaved model would
            // orphan its rule on the first real save - block configuration and
            // say why instead of silently not persisting.
            if (string.Equals(row.Cells["colModelName"].Value?.ToString(), "Untitled", StringComparison.OrdinalIgnoreCase))
            {
                _setRowStatus(row, "Unsaved", Color.Gray, "", true);
                row.Cells["colStatus"].ToolTipText = "Save the .pbix in Power BI Desktop to configure this instance.";
                return;
            }

            // The Action cell names the one action available from this state and
            // performs it on click (#126) — with a single action per state, a menu to
            // reveal one item was friction for nothing.
            var workspaceId = row.Tag as string;
            var session = workspaceId != null ? _sessionLookup(workspaceId) : null;
            if (session != null)
            {
                _setRowStatus(row, "Serving", Color.MediumBlue, HostActionLabel.For(HostAction.Stop), true);
                return;
            }

            // Off. Serving needs a stable name, so a model without one is pointed at
            // where that is set instead of being offered an action that would fail.
            bool hasAlias = !string.IsNullOrWhiteSpace(_ruleLookup(row.Cells["colModelName"].Value?.ToString())?.StableAlias);
            _setRowStatus(row, "Ready", Color.Black,
                hasAlias ? HostActionLabel.For(HostAction.Serve) : "Set name", false);
        }

        public void PaintOffline(DataGridViewRow row)
        {
            ProjectPolicy(row);
            _setRowStatus(row, "Offline", Color.Gray, "Remove", false);
        }

        /// <summary>
        /// Projects the row's config-editable cells that can also change from the tray -
        /// the On-detection dropdown and the Network checkbox - from the rule, so a
        /// change made on the tray (which raises ConfigurationChanged → a repaint, not a
        /// full snapshot) shows up in the grid too (#88). Any externally-changeable cell
        /// must be projected here, not only in the snapshot path (GridSyncHelper). This
        /// is display-only: the user's own edits persist via CurrentCellDirtyStateChanged,
        /// so writing here never loops back into config.
        /// </summary>
        private void ProjectPolicy(DataGridViewRow row)
        {
            var rule = _ruleLookup(row.Cells["colModelName"].Value?.ToString());
            if (rule == null) return; // no profile yet: leave the defaults the grid set

            var grid = row.DataGridView;
            var policyCell = row.Cells["colOnDetection"];
            // Skip the dropdown while it is open/being edited so a pick isn't clobbered.
            if (!(grid != null && grid.IsCurrentCellInEditMode && grid.CurrentCell == policyCell))
            {
                string label = OnDetectionPolicyLabel.For(rule.OnDetection);
                if (policyCell.Value?.ToString() != label) policyCell.Value = label;
            }

            // Read-only (#129) is editable from the tray too, so it projects here for
            // the same reason the dropdown does. A checkbox commits on the click, so
            // there is no half-finished edit to protect.
            var readOnlyCell = row.Cells["colReadOnly"];
            if (!(readOnlyCell.Value is bool current) || current != rule.ReadOnly)
            {
                readOnlyCell.Value = rule.ReadOnly;
            }

            // The alias is user-editable in the grid, so leave it alone while it is
            // being typed - projecting over a half-typed name would fight the user.
            var aliasCell = row.Cells["colAlias"];
            if (!(grid != null && grid.IsCurrentCellInEditMode && grid.CurrentCell == aliasCell)
                && aliasCell.Value?.ToString() != rule.StableAlias)
            {
                aliasCell.Value = rule.StableAlias;
            }
        }
    }
}

using System.Windows.Forms;

namespace PBIPortWrapper.Presenters
{
    public class DetailRowManager
    {
        public bool IsDetailRow(DataGridViewRow row)
        {
            return row.Tag is string tag && tag.StartsWith("Detail:");
        }

        public bool IsDetailRowFor(DataGridViewRow row, int processId)
        {
            return row.Tag is string tag && tag == $"Detail:{processId}";
        }

        public void HandleDetailRow(DataGridView grid, int parentRawIndex, bool isExpanded, int processId)
        {
            int nextIndex = parentRawIndex + 1;
            bool hasDetailRow = false;

            // Check if detail row exists
            if (nextIndex < grid.Rows.Count)
            {
                var nextRow = grid.Rows[nextIndex];
                if (IsDetailRowFor(nextRow, processId))
                {
                    hasDetailRow = true;
                    if (!isExpanded)
                    {
                        grid.Rows.RemoveAt(nextIndex);
                    }
                }
            }

            // Create detail row if needed
            if (isExpanded && !hasDetailRow)
            {
                grid.Rows.Insert(nextIndex, 1);
                var detailRow = grid.Rows[nextIndex];
                detailRow.Tag = $"Detail:{processId}";
                // DPI-aware: 150 designer pixels expressed relative to the font
                detailRow.Height = grid.Font.Height * 10;
                detailRow.Cells["colModelName"].Value = "Loading details...";
            }
        }
    }
}

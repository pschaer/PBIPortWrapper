using System;
using System.Collections.Generic;
using System.Windows.Forms;
using PBIRelay.Models;

namespace PBIRelay.Presenters
{
    // FILE SIZE: MAX 250 lines - enforced by build target
    /// <summary>
    /// Lays out the instance grid's columns: the two added in code, their order, their
    /// sizing and their alignment.
    ///
    /// Pure view configuration with no state and no event wiring, extracted from
    /// MainForm to keep the composition root inside its size limit.
    /// </summary>
    public static class GridColumnLayout
    {
        /// <summary>
        /// The column order left to right. Named once here rather than as a run of
        /// DisplayIndex assignments, so re-ordering is a single edit.
        /// </summary>
        private static readonly string[] Order =
        {
            "colExpand", "colModelName", "colAlias", "colPbiPort",
            "colOnDetection", "colReadOnly", "colAction", "colStatus"
        };

        /// <summary>
        /// Every filled column's share of the width, relative to the others.
        ///
        /// One table rather than scattered assignments, because a column with no
        /// entry keeps WinForms' default FillWeight of 100 and silently swallows the
        /// row — which is exactly what a missing entry for the alias did.
        /// <see cref="Apply"/> throws if a column here is not in <see cref="Order"/>,
        /// or vice versa, so the two cannot drift.
        /// </summary>
        private static readonly (string Column, float Weight)[] FillWeights =
        {
            ("colModelName", 2.4f),
            ("colAlias", 2.2f),
            ("colOnDetection", 2.0f),   // "Serve after grace period" is wide
            ("colReadOnly", 1.0f),
            ("colPbiPort", 1.0f),
            ("colAction", 1.0f),
            ("colStatus", 1.0f)
        };

        private static readonly string[] CentredCells =
        {
            "colPbiPort", "colStatus", "colReadOnly"
        };

        private static readonly string[] CentredHeadersOnly =
        {
            "colOnDetection", "colAction"
        };

        /// <summary>
        /// Configures the grid. <paramref name="scale"/> converts logical pixels to
        /// device units — the form's <c>LogicalToDeviceUnits</c>, passed in because
        /// that is a Control member and this is not a control.
        /// </summary>
        public static void Apply(DataGridView grid, Func<int, int> scale)
        {
            // The designer's RowTemplate.Height is 96-DPI pixels, but fonts scale
            // with monitor DPI (PerMonitorV2) - rows must follow the font or the
            // text gets clipped on scaled displays.
            int rowHeight = grid.Font.Height + 10;
            grid.RowTemplate.Height = rowHeight;
            grid.RowTemplate.MinimumHeight = rowHeight;

            AddExpandColumn(grid, scale);
            FillOnDetectionChoices(grid);

            for (int i = 0; i < Order.Length; i++)
                grid.Columns[Order[i]].DisplayIndex = i;

            // The expander is the one fixed-width column; everything else shares the
            // rest by weight.
            grid.Columns["colExpand"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            grid.Columns["colExpand"].Width = scale(30);

            AssertEveryColumnHasAWeight();

            foreach ((string name, float weight) in FillWeights)
            {
                grid.Columns[name].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                grid.Columns[name].FillWeight = weight;
            }

            foreach (string name in CentredCells)
            {
                grid.Columns[name].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.Columns[name].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            foreach (string name in CentredHeadersOnly)
                grid.Columns[name].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }

        /// <summary>
        /// Every ordered column except the fixed-width expander must have a weight,
        /// and no weight may name a column that is not shown. Without this, adding a
        /// column and forgetting its weight leaves it at FillWeight 100, which looks
        /// like the grid is broken rather than like one line is missing.
        /// </summary>
        private static void AssertEveryColumnHasAWeight()
        {
            var weighted = new HashSet<string>();
            foreach ((string name, _) in FillWeights) weighted.Add(name);

            foreach (string name in Order)
            {
                if (name == "colExpand") continue;
                if (!weighted.Contains(name))
                    throw new InvalidOperationException($"Grid column '{name}' has no fill weight.");
            }

            var shown = new HashSet<string>(Order);
            foreach ((string name, _) in FillWeights)
            {
                if (!shown.Contains(name))
                    throw new InvalidOperationException($"Fill weight names unknown column '{name}'.");
            }
        }

        private static void AddExpandColumn(DataGridView grid, Func<int, int> scale)
        {
            if (grid.Columns.Contains("colExpand")) return;

            var column = new DataGridViewTextBoxColumn
            {
                Name = "colExpand",
                HeaderText = "",
                ReadOnly = true,
                Width = scale(30)
            };
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns.Insert(0, column);
        }

        /// <summary>
        /// The same choices, in the same order, as the tray's "On detection" submenu,
        /// so grid and tray read identically (#88).
        /// </summary>
        private static void FillOnDetectionChoices(DataGridView grid)
        {
            var column = (DataGridViewComboBoxColumn)grid.Columns["colOnDetection"];
            column.Items.Clear();
            foreach (OnDetectionPolicy policy in OnDetectionPolicyLabel.Order)
                column.Items.Add(OnDetectionPolicyLabel.For(policy));
            column.FlatStyle = FlatStyle.Flat;
        }
    }
}

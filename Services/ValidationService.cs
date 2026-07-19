using System.Collections.Generic;
using System.Windows.Forms;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// DataGridView adapter over the Core PortValidator: extracts assigned ports
    /// from grid rows and delegates the actual validation.
    /// </summary>
    public class ValidationService
    {
        public bool IsPortValid(string portString, out int port)
        {
            return PortValidator.TryParsePort(portString, out port);
        }

        public bool IsPortDuplicate(int port, DataGridView grid, int excludeRowIndex)
        {
            foreach (int otherPort in AssignedPorts(grid, excludeRowIndex))
            {
                if (otherPort == port) return true;
            }
            return false;
        }

        public (bool IsValid, string ErrorMessage) ValidatePortAssignment(string portString, DataGridView grid, int rowIndex)
        {
            return PortValidator.ValidatePortAssignment(portString, AssignedPorts(grid, rowIndex));
        }

        private static IEnumerable<int> AssignedPorts(DataGridView grid, int excludeRowIndex)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Index == excludeRowIndex) continue;

                if (row.Cells["colFixedPort"].Value != null &&
                    int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out int otherPort))
                {
                    yield return otherPort;
                }
            }
        }
    }
}

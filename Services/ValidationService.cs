using System.Windows.Forms;

namespace PBIPortWrapper.Services
{
    public class ValidationService
    {
        public bool IsPortValid(string portString, out int port)
        {
            return int.TryParse(portString, out port) && port > 0 && port <= 65535;
        }

        public bool IsPortDuplicate(int port, DataGridView grid, int excludeRowIndex)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Index == excludeRowIndex) continue;

                if (row.Cells["colFixedPort"].Value != null &&
                    int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out int otherPort))
                {
                    if (otherPort == port) return true;
                }
            }
            return false;
        }

        public (bool IsValid, string ErrorMessage) ValidatePortAssignment(string portString, DataGridView grid, int rowIndex)
        {
            if (string.IsNullOrEmpty(portString)) return (true, string.Empty); // Allow empty

            if (!int.TryParse(portString, out int newPort))
            {
                return (false, "Port must be a number");
            }

            if (newPort < 1 || newPort > 65535)
            {
                return (false, "Port must be between 1 and 65535");
            }

            if (IsPortDuplicate(newPort, grid, rowIndex))
            {
                return (false, $"Port {newPort} is already assigned to another instance.");
            }

            return (true, string.Empty);
        }
    }
}

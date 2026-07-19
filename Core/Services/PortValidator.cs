using System.Collections.Generic;
using System.Linq;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Grid-independent port validation. The UI's ValidationService adapts
    /// DataGridView rows onto these methods.
    /// </summary>
    public static class PortValidator
    {
        public static bool TryParsePort(string portString, out int port)
        {
            return int.TryParse(portString, out port) && port > 0 && port <= 65535;
        }

        public static (bool IsValid, string ErrorMessage) ValidatePortAssignment(
            string portString,
            IEnumerable<int> otherAssignedPorts)
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

            if (otherAssignedPorts.Contains(newPort))
            {
                return (false, $"Port {newPort} is already assigned to another instance.");
            }

            return (true, string.Empty);
        }
    }
}

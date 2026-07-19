using System;
using System.Windows.Forms;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Presenters
{
    public enum RowActionType { Start, Stop, Remove }

    public class RowActionEventArgs : EventArgs
    {
        public RowActionType Action { get; set; }
        public PowerBIInstance Instance { get; set; } // For Start
        public int FixedPort { get; set; }           // For Start/Stop
        public bool AllowNetwork { get; set; }       // For Start
        public string ModelName { get; set; }        // For Remove
        public DataGridViewRow Row { get; set; }     // For UI feedback context
    }

    public class ConfigChangeEventArgs : EventArgs
    {
        public string ModelName { get; set; }
        public int FixedPort { get; set; }
        public bool Auto { get; set; }
        public bool AllowNetwork { get; set; }
        public DataGridViewRow Row { get; set; }
    }
}

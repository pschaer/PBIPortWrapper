using System;

namespace PBIPortWrapper.Models
{
    public class PowerBIInstance
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public int Port { get; set; }
        public string DatabaseName { get; set; }
        public DateTime LastModified { get; set; }
        public string WorkspaceId { get; set; }
        public int ProcessId { get; set; }
        public int ParentProcessId { get; set; }

        public override string ToString()
        {
            return $"{FileName} (Port: {Port})";
        }
    }
}

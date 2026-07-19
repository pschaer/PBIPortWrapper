using System;

namespace PBIPortWrapper.Models
{
    /// <summary>In-memory state of one active serve session (#57).</summary>
    public class ServeSession
    {
        public string WorkspaceId { get; set; }
        public string FileName { get; set; }
        public string Alias { get; set; }
        public string DatabaseId { get; set; }
        public int InstancePort { get; set; }
        public int FixedPort { get; set; }
        public int Pid { get; set; }
        public DateTime StartedUtc { get; set; }
    }
}

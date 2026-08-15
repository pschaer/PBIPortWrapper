using System;

namespace PBIRelay.Models
{
    /// <summary>
    /// Crash-recovery anchor for a serve session (#57), persisted to config BEFORE
    /// the database is renamed. If PBIRelay dies while serving, the next start
    /// finds this record and can offer restore/resume (#58). DatabaseId is the
    /// original database name (a GUID) — immutable through rename per experiment E2,
    /// so it both identifies the instance's database and is the name to restore.
    /// </summary>
    public class ServeRecoveryRecord
    {
        public string DatabaseId { get; set; }
        public string Alias { get; set; }
        public string WorkspaceId { get; set; }
        public int Pid { get; set; }
        public DateTime StartedUtc { get; set; }
    }
}

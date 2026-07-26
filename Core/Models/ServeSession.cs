using System;

namespace PBIPortWrapper.Models
{
    /// <summary>
    /// In-memory state of one active serve session (#57).
    ///
    /// There is no fixed port here: serving renames the database and the XMLA endpoint
    /// reaches it on <see cref="InstancePort"/>, the engine's own live port (#126).
    /// Clients address the model by alias, not by port.
    /// </summary>
    public class ServeSession
    {
        public string WorkspaceId { get; set; }
        public string FileName { get; set; }
        public string Alias { get; set; }
        public string DatabaseId { get; set; }

        /// <summary>The Analysis Services port this model's engine is listening on.</summary>
        public int InstancePort { get; set; }

        public int Pid { get; set; }
        public DateTime StartedUtc { get; set; }
    }
}

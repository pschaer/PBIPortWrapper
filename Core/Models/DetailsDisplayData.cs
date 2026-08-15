using System;

namespace PBIRelay.Models
{
    public class DetailsDisplayData
    {
        public string ModelName { get; set; }
        public int PbiPort { get; set; }
        public string ConnectionString { get; set; }
        public string DatabaseOriginalName { get; set; }
        public string DatabaseAlias { get; set; }

        /// <summary>An active serve session exists for this instance (#59).</summary>
        public bool IsServing { get; set; }
        public string FullTitle { get; set; }

        /// <summary>The AS workspace directory. Long enough to wrap several times, so
        /// the panel shows its leaf and keeps the whole path in the tooltip.</summary>
        public string WorkspacePath { get; set; }
        public string TooltipText { get; set; }
    }
}

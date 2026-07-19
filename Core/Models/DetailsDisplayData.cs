using System;

namespace PBIPortWrapper.Models
{
    public class DetailsDisplayData
    {
        public string ModelName { get; set; }
        public int PbiPort { get; set; }
        public int FixedPort { get; set; }
        public string ConnectionString { get; set; }
        public string DatabaseOriginalName { get; set; }
        public string DatabaseAlias { get; set; }

        /// <summary>An active serve session exists for this instance (#59).</summary>
        public bool IsServing { get; set; }
        public string FullTitle { get; set; }
        public string TooltipText { get; set; }
    }
}

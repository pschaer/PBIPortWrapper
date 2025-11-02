namespace PowerBIPortWrapper.Models
{
    public class ProxyConfiguration
    {
        public int FixedPort { get; set; } = 55555;
        public bool AllowNetworkAccess { get; set; } = false;
        public int NetworkPort { get; set; } = 55556;
        public bool AutoStart { get; set; } = false;
        public string LastSelectedInstance { get; set; }
    }
}

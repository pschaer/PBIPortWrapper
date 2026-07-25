namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Formats the connection coordinates external tools use (#85). Pure string
    /// building; picking the host (localhost vs LAN address) is the caller's job.
    /// </summary>
    public static class ConnectionStringBuilder
    {
        /// <summary>
        /// The short Data-Source form tools like DAX Studio accept, e.g.
        /// <c>localhost:55555</c>.
        /// </summary>
        public static string DataSource(string host, int port) => $"{host}:{port}";

        /// <summary>
        /// The full MSOLAP connection string. Includes the stable catalog (alias)
        /// when one is given, e.g.
        /// <c>Provider=MSOLAP;Data Source=localhost:55555;Initial Catalog=Sales</c>.
        /// </summary>
        public static string Full(string host, int port, string alias = null)
        {
            var s = $"Provider=MSOLAP;Data Source={host}:{port}";
            if (!string.IsNullOrWhiteSpace(alias))
                s += $";Initial Catalog={alias}";
            return s;
        }
    }
}

using System;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Extracts the model name from a Power BI Desktop window title. Some builds use
    /// "&lt;name&gt; - Power BI Desktop"; others show just the name. Handles both (#94).
    /// </summary>
    public static class WindowTitleParser
    {
        private const string Suffix = " - Power BI Desktop";

        public static string ModelName(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;

            int i = title.LastIndexOf(Suffix, StringComparison.Ordinal);
            string name = (i >= 0 ? title.Substring(0, i) : title).Trim();
            return string.IsNullOrEmpty(name) ? null : name;
        }
    }
}

using System.Security;
using System.Text;

namespace PBIRelay.Services
{
    /// <summary>
    /// Builds an Office Data Connection (<c>.odc</c>) file for a served model so a
    /// user (or a colleague) double-clicks it and gets an Excel PivotTable — no
    /// connection string ever typed or seen (#86, the Excel hand-off in
    /// docs/tray-workflow.md). Pure string building; the caller supplies the host,
    /// port and stable catalog and owns writing the file to disk.
    ///
    /// The connection string comes from <see cref="ConnectionStringBuilder.ForEndpoint"/>
    /// so the .odc and the copy buttons stay in lockstep. Because the alias and the
    /// endpoint's address are stable across Desktop restarts, a saved .odc keeps
    /// resolving — the core promise of serving.
    /// </summary>
    public static class OdcFileBuilder
    {
        /// <summary>
        /// The cube name Excel connects to for a Power BI / tabular model. Power BI
        /// Desktop exposes its model as a single cube named "Model".
        /// </summary>
        public const string DefaultCube = "Model";

        /// <summary>
        /// Builds the full .odc file content (an Office-recognized HTML document) for
        /// a PivotTable against <paramref name="catalog"/> at
        /// <paramref name="endpointUrl"/> — the model's own URL on the XMLA endpoint
        /// (#126), which is now the only way a client reaches it.
        /// </summary>
        public static string Build(string modelName, string endpointUrl, string catalog, string cube = DefaultCube)
        {
            string title = string.IsNullOrWhiteSpace(modelName) ? catalog : modelName;
            string connection = ConnectionStringBuilder.ForEndpoint(endpointUrl, catalog);

            string t = Escape(title);
            string cat = Escape(catalog);
            string cb = Escape(string.IsNullOrWhiteSpace(cube) ? DefaultCube : cube);
            string conn = Escape(connection);

            var sb = new StringBuilder();
            sb.Append("<html xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns=\"http://www.w3.org/TR/REC-html40\">\r\n");
            sb.Append("<head>\r\n");
            sb.Append("<meta http-equiv=\"Content-Type\" content=\"text/x-ms-odc; charset=utf-8\">\r\n");
            sb.Append("<meta name=\"ProgId\" content=\"ODC.Cube\">\r\n");
            sb.Append("<meta name=\"SourceType\" content=\"OLEDB\">\r\n");
            sb.Append($"<meta name=\"Catalog\" content=\"{cat}\">\r\n");
            sb.Append($"<meta name=\"Table\" content=\"{cb}\">\r\n");
            sb.Append($"<title>{t}</title>\r\n");
            sb.Append("<xml id=\"docprops\"><o:DocumentProperties xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns=\"http://www.w3.org/TR/REC-html40\">\r\n");
            sb.Append($"  <o:Name>{t}</o:Name>\r\n");
            sb.Append(" </o:DocumentProperties>\r\n");
            sb.Append("</xml><xml id=\"msodc\"><odc:OfficeDataConnection xmlns:odc=\"urn:schemas-microsoft-com:office:odc\" xmlns=\"http://www.w3.org/TR/REC-html40\">\r\n");
            sb.Append("  <odc:Connection odc:Type=\"OLEDB\">\r\n");
            sb.Append($"   <odc:ConnectionString>{conn}</odc:ConnectionString>\r\n");
            sb.Append("   <odc:CommandType>Cube</odc:CommandType>\r\n");
            sb.Append($"   <odc:CommandText>{cb}</odc:CommandText>\r\n");
            sb.Append("  </odc:Connection>\r\n");
            sb.Append(" </odc:OfficeDataConnection>\r\n");
            sb.Append("</xml>\r\n");
            sb.Append("</head>\r\n");
            sb.Append("</html>\r\n");
            return sb.ToString();
        }

        /// <summary>
        /// A safe default file name (with the <c>.odc</c> extension) for a model,
        /// stripping characters Windows disallows in file names.
        /// </summary>
        public static string SuggestFileName(string name)
        {
            string baseName = string.IsNullOrWhiteSpace(name) ? "model" : name.Trim();
            var sb = new StringBuilder(baseName.Length);
            foreach (char c in baseName)
                sb.Append("<>:\"/\\|?*".IndexOf(c) >= 0 || c < ' ' ? '_' : c);
            return sb.ToString() + ".odc";
        }

        private static string Escape(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : SecurityElement.Escape(value);
    }
}

using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    /// <summary>
    /// The "Save .odc…" flow (#86), shared by the tray menu and the row details panel
    /// so the two offer the same thing. The file's contents come from
    /// <see cref="OdcFileBuilder"/>; this only owns the dialogs.
    /// </summary>
    public static class OdcSaveAction
    {
        public static void Save(string modelName, string endpointUrl, string catalog)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl) || string.IsNullOrWhiteSpace(catalog)) return;

            using (var dialog = new SaveFileDialog
            {
                Title = "Save Office Data Connection",
                Filter = "Office Data Connection (*.odc)|*.odc",
                DefaultExt = "odc",
                AddExtension = true,
                FileName = OdcFileBuilder.SuggestFileName(catalog)
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                try
                {
                    string content = OdcFileBuilder.Build(modelName, endpointUrl, catalog);
                    File.WriteAllText(dialog.FileName, content, new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not save the .odc file:\n{ex.Message}",
                        "Save .odc", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
    }
}

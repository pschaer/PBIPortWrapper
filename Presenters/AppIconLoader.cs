using System;
using System.Drawing;
using System.IO;

namespace PBIPortWrapper.Presenters
{
    /// <summary>
    /// Loads the app icon from Resources\app_icon.png next to the executable.
    /// Extracted from MainForm (#85a) to keep the form under its size limit.
    /// </summary>
    public static class AppIconLoader
    {
        /// <summary>Returns the app icon, or null if it can't be loaded (icon is optional).</summary>
        public static Icon TryLoad()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app_icon.png");
                if (!File.Exists(path)) return null;
                using (var bmp = new Bitmap(path))
                    return Icon.FromHandle(bmp.GetHicon());
            }
            catch
            {
                return null;
            }
        }
    }
}

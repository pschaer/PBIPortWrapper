using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PBIRelay.Presenters
{
    /// <summary>
    /// Loads the app icon from the embedded multi-frame app.ico.
    /// Extracted from MainForm (#85a) to keep the form under its size limit.
    ///
    /// <para>
    /// The frame matters. This used to read the 256x256 <c>Resources\app_icon.png</c>
    /// and call <c>Bitmap.GetHicon()</c>, which yields an icon with exactly one frame.
    /// Windows then had nothing to choose from and rescaled that single bitmap for
    /// every surface - 256 -> 24 for the taskbar button, 256 -> 16 for the tray. At
    /// that reduction the one-pixel gaps between the chart bars blur away and the bars
    /// merge into a smear, which is what made the taskbar icon look squashed.
    /// </para>
    /// <para>
    /// app.ico carries hand-sized 16/24/32/48 frames, so asking for the size the
    /// surface actually draws at lets Windows pick a frame instead of inventing one.
    /// </para>
    /// </summary>
    public static class AppIconLoader
    {
        private const string ResourceName = "PBIRelay.app.ico";

        /// <summary>
        /// The window icon, keeping every frame so WinForms can pull the right one for
        /// the title bar, the Alt-Tab list and the taskbar button in turn. Null if it
        /// cannot be loaded - the icon is optional and its absence must not stop startup.
        /// </summary>
        public static Icon TryLoad() => Load(null);

        /// <summary>
        /// The tray icon at the small-icon size. The tray draws at that size and nothing
        /// larger, so picking the frame here avoids handing Windows a big one to shrink.
        /// </summary>
        public static Icon TryLoadSmall() => Load(SystemInformation.SmallIconSize);

        private static Icon Load(Size? size)
        {
            try
            {
                using Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(ResourceName);
                if (stream == null) return null;

                return size.HasValue ? new Icon(stream, size.Value) : new Icon(stream);
            }
            catch
            {
                return null;
            }
        }
    }
}

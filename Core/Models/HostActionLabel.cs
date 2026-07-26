namespace PBIPortWrapper.Models
{
    /// <summary>
    /// User-facing labels for <see cref="HostAction"/>, shared by the tray menu and the
    /// grid's single Action menu (#88) so both offer identically-labelled actions.
    /// </summary>
    public static class HostActionLabel
    {
        public static string For(HostAction action)
        {
            switch (action)
            {
                case HostAction.Serve: return "Serve";
                case HostAction.Stop: return "Stop";
                default: return action.ToString();
            }
        }
    }
}

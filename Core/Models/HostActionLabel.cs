namespace PBIPortWrapper.Models
{
    /// <summary>
    /// User-facing labels for <see cref="HostAction"/>, shared by the tray menu and the
    /// grid's single Action menu (#88) so both offer identically-labelled actions.
    /// Both "stop" actions read simply as "Stop" - the model's current state makes it
    /// unambiguous (a serving row's Stop restores the name; a forwarding row's Stop
    /// stops the proxy).
    /// </summary>
    public static class HostActionLabel
    {
        public static string For(HostAction action)
        {
            switch (action)
            {
                case HostAction.Forward: return "Forward";
                case HostAction.Serve: return "Serve";
                case HostAction.StopServing: return "Stop";
                case HostAction.Stop: return "Stop";
                default: return action.ToString();
            }
        }
    }
}

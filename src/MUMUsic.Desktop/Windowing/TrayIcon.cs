namespace MUMUsic.Desktop.Windowing;

/// <summary>The notification-area icon: closing the window hides it there and the music keeps playing.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    /// <param name="onOpen">Brings the window back.</param>
    /// <param name="onPlayer">Sends "play", "next" or "prev" to the player.</param>
    /// <param name="onExit">Really quits the app.</param>
    public TrayIcon(Icon? icon, Action onOpen, Action<string> onPlayer, Action onExit)
    {
        var menu = new ContextMenuStrip();
        var open = menu.Items.Add("Show MUMUsic", null, (_, _) => onOpen());
        open.Font = new Font(open.Font, FontStyle.Bold);
        menu.Items.Add("Play / pause", null, (_, _) => onPlayer("play"));
        menu.Items.Add("Next song", null, (_, _) => onPlayer("next"));
        menu.Items.Add("Previous song", null, (_, _) => onPlayer("prev"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => onExit());

        _icon = new NotifyIcon
        {
            Icon = icon ?? SystemIcons.Application,
            Text = AppInfo.Name,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left) onOpen();
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}

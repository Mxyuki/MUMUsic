using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using MUMUsic.Desktop.Server;
using MUMUsic.Desktop.Settings;
using MUMUsic.Desktop.Windowing;

namespace MUMUsic.Desktop;

/// <summary>
/// The window: WebView2 showing the local server's page, with the title bar drawn by the page. Closing it hides it to
/// the tray and the music keeps playing; "Quit" in the tray menu really exits.
/// </summary>
internal sealed class MainForm : Form, IWindowHost
{
    private static readonly Size StartSize = new(1360, 860);
    private static readonly Size MinSize = new(980, 640);

    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly SettingsStore _settings;
    private readonly string _origin;
    private readonly bool _isDebug;
    private readonly TrayIcon _tray;
    private HostBridge? _bridge;
    private bool _isExiting;
    private bool _wasMaximized;
    // A song asked for by a mumusic:// link, until the page takes it (take_open): the page may still be loading.
    private string? _openSong;

    /// <summary>A setup downloaded by an app update, run by Program.cs once the app has fully quit.</summary>
    public string? PendingSetup { get; private set; }

    public MainForm(SettingsStore settings, string origin, bool isDebug)
    {
        _settings = settings;
        _origin = origin;
        _isDebug = isDebug;
        Text = AppInfo.Name;
        Icon = LoadIcon();
        StartPosition = FormStartPosition.Manual;
        BackColor = IsSystemDark() ? Color.FromArgb(0x0c, 0x0d, 0x0e) : Color.FromArgb(0xe8, 0xe8, 0xe3);
        _webView.DefaultBackgroundColor = BackColor;
        Controls.Add(_webView);
        _tray = new TrayIcon(Icon, ShowFromTray, command => _bridge?.Emit("tray", command), Exit);
    }

    /// <summary>Brings the window back from the tray or the taskbar (also when the app is started a second time).</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = _wasMaximized ? FormWindowState.Maximized : FormWindowState.Normal;
        Activate();
    }

    public void QuitAndInstall(string setupPath)
    {
        PendingSetup = setupPath;
        BeginInvoke(Exit);
    }

    /// <summary>A mumusic://play/&lt;song&gt; link: brings the window up and has the page play the song.</summary>
    public void OpenLink(string link)
    {
        if (AppLinks.SongId(link) is not { } id) return;
        Log.Write($"[link] play {id}");
        _openSong = id;
        if (!IsHandleCreated) return;          // first start: the page takes it once it is ready
        ShowFromTray();
        _bridge?.Emit("open", id);
    }

    /// <summary>Really quits (the tray's Quit, or the setup closing the app).</summary>
    public void Quit() => Exit();

    private void Exit()
    {
        _isExiting = true;
        Close();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var scale = DeviceDpi / 96.0;
        MinimumSize = new Size((int)(MinSize.Width * scale), (int)(MinSize.Height * scale));
        var saved = _settings.Current;
        Bounds = StartBounds(saved, scale);
        if (saved.Maximized) WindowState = FormWindowState.Maximized;
        WindowChrome.RefreshFrame(Handle);
    }

    /// <summary>Where the window was last time, kept inside a screen's work area (a screen may be gone, or smaller);
    /// centred on the screen under the cursor the first time.</summary>
    private static Rectangle StartBounds(AppSettings saved, double scale)
    {
        var size = saved.Window is [var width, var height] && width > 0 && height > 0 ? new Size(width, height) : StartSize;
        size = new Size((int)(size.Width * scale), (int)(size.Height * scale));
        var at = saved.WindowAt is [var x, var y] ? new Point(x, y) : (Point?)null;
        var work = (at is { } point ? Screen.FromRectangle(new Rectangle(point, size)) : Screen.FromPoint(Cursor.Position)).WorkingArea;
        size = new Size(Math.Min(size.Width, work.Width), Math.Min(size.Height, work.Height));
        var location = at is { } p && work.IntersectsWith(new Rectangle(p, size))
            ? new Point(Math.Clamp(p.X, work.Left, work.Right - size.Width), Math.Clamp(p.Y, work.Top, work.Bottom - size.Height))
            : new Point(work.Left + (work.Width - size.Width) / 2, work.Top + (work.Height - size.Height) / 2);
        return new Rectangle(location, size);
    }

    protected override void WndProc(ref Message m)
    {
        if (WindowChrome.TryHandleCalcSize(ref m, message => base.WndProc(ref message), DeviceDpi)) return;
        base.WndProc(ref m);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized) return;
        var maximized = WindowState == FormWindowState.Maximized;
        if (maximized == _wasMaximized) return;
        _wasMaximized = maximized;
        _bridge?.Emit("windowState", maximized);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The window's own close (Alt+F4, the page's X) hides to the tray; Quit, an update or Windows shutting down exit.
        if (e.CloseReason == CloseReason.UserClosing && !_isExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        SaveWindow();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tray.Dispose();
        base.OnFormClosed(e);
    }

    private void SaveWindow()
    {
        var maximized = WindowState == FormWindowState.Maximized;
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        var scale = DeviceDpi / 96.0;
        _settings.Update(s =>
        {
            s.Maximized = maximized;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            s.Window = [(int)(bounds.Width / scale), (int)(bounds.Height / scale)];
            s.WindowAt = [bounds.Left, bounds.Top];
        });
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            await InitializeWebViewAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            var answer = MessageBox.Show(this,
                "MUMUsic needs the Microsoft Edge WebView2 Runtime, which is missing on this computer.\n\nOpen the download page?",
                AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Error);
            if (answer == DialogResult.Yes) OpenExternal("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            Exit();
        }
    }

    private async Task InitializeWebViewAsync()
    {
        // A song opened from a friend's Discord status (mumusic:// link) starts playing with no click in the page, which
        // a browser's autoplay rule would block.
        var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
        var environment = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewData, options);
        await _webView.EnsureCoreWebView2Async(environment);
        var core = _webView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = _isDebug;
        core.Settings.AreDefaultContextMenusEnabled = _isDebug;
        core.Settings.AreBrowserAcceleratorKeysEnabled = _isDebug;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            OpenExternal(args.Uri);
        };
        // The app is one page; any navigation away from it is a link for the browser.
        core.NavigationStarting += (_, args) =>
        {
            if (args.Uri.StartsWith(_origin + "/", StringComparison.OrdinalIgnoreCase)) return;
            args.Cancel = true;
            OpenExternal(args.Uri);
        };
        _bridge = new HostBridge(core, _origin, Call);
        _bridge.Attach();
        core.Navigate(_origin + "/");
    }

    /// <summary>The window controls of the page (web/app.js, "window" section).</summary>
    private object? Call(string method, JsonElement[] args)
    {
        switch (method)
        {
            case "minimize":
                WindowState = FormWindowState.Minimized;
                return null;
            case "toggle_maximize":
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                return WindowState == FormWindowState.Maximized;
            case "take_open":
                var song = _openSong;
                _openSong = null;
                return song;
            case "is_maximized":
                return WindowState == FormWindowState.Maximized;
            case "close":
                Hide();
                return null;
            case "quit":
                BeginInvoke(Exit);
                return null;
            case "start_move":
                WindowChrome.StartDrag(Handle, WindowChrome.HitTest["move"]);
                return null;
            case "start_resize" when args is [{ ValueKind: JsonValueKind.String } edge] && WindowChrome.HitTest.TryGetValue(edge.GetString()!, out var hit):
                if (WindowState == FormWindowState.Normal) WindowChrome.StartDrag(Handle, hit);
                return null;
            case "pick_library_file":
                using (var dialog = new OpenFileDialog { Filter = "MUMUsic library (*.mumu)|*.mumu|All files (*.*)|*.*", Title = "Open a library file" })
                {
                    return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
                }
            case "open_url" when args is [{ ValueKind: JsonValueKind.String } url]:
                OpenExternal(url.GetString()!);
                return null;
            default:
                throw new InvalidOperationException($"Unknown window method \"{method}\"");
        }
    }

    /// <summary>Opens a web link in the default browser; anything but https is ignored.</summary>
    private static void OpenExternal(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("open link", error);
        }
    }

    private static bool IsSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is 0;
    }

    private static Icon? LoadIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("MUMUsic.ico");
        return stream is null ? null : new Icon(stream);
    }
}

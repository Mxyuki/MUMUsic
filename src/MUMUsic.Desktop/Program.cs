using System.Diagnostics;
using MUMUsic.Desktop.Server;
using MUMUsic.Desktop.Settings;
using MUMUsic.Desktop.Updates;
using MUMUsic.Desktop.UserData;

namespace MUMUsic.Desktop;

internal static class Program
{
    // /SILENT shows only the setup's progress window; the setup starts the app again when it is done.
    private const string UpdateSetupArguments = "/SILENT /SP- /NOCANCEL /NORESTART /CLOSEAPPLICATIONS";

    [STAThread]
    private static void Main(string[] args)
    {
        // One instance: starting MUMUsic again shows the window of the one already running (and hands it the
        // mumusic:// link it was started with, if any).
        using var instance = new Mutex(true, AppInfo.InstanceMutex, out var isFirst);
        using var showWindow = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.ShowWindowEvent);
        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.QuitEvent);
        if (!isFirst)
        {
            if (AppLinks.FromArgs(args) is { } link) AppLinks.Send(link);
            showWindow.Set();
            return;
        }

        Log.Open();
        // The setup starts the app in the program folder; WebView2's processes inherit that folder and outlive the app
        // by a few seconds, which keeps the uninstaller from removing it.
        Environment.CurrentDirectory = AppPaths.UserDir;
        Application.ThreadException += (_, e) => Log.Error("ui", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"[crash] {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("task", e.Exception.GetBaseException());
            e.SetObserved();
        };
        ApplicationConfiguration.Initialize();

        string? setup;
        try
        {
            setup = Run(args, showWindow, quit);
        }
        catch (Exception error)
        {
            Log.Write($"[crash] {error}");
            MessageBox.Show($"MUMUsic could not start:\n\n{error.Message}\n\nDetails are in {AppPaths.LogFile}", AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (setup is null) return;

        // The setup waits while the instance mutex exists: release it before starting the setup.
        instance.ReleaseMutex();
        instance.Dispose();
        Log.Write($"[update] running {Path.GetFileName(setup)}");
        Process.Start(new ProcessStartInfo(setup, UpdateSetupArguments) { UseShellExecute = true });
    }

    /// <summary>Runs the app until it quits. Returns the setup to run when it quit for an update.</summary>
    private static string? Run(string[] args, EventWaitHandle showWindow, EventWaitHandle quit)
    {
        var settings = new SettingsStore(AppPaths.SettingsFile);
        var user = new UserStore(AppPaths.UserDb);
        user.Backup();
        Task.Run(UpdateService.DeleteOldSetups);
        var state = new AppState(settings);

        MainForm? window = null;
        var api = new Api(settings, user, state, new DeferredWindow(() => window!));
        var server = new LocalServer(api.HandleAsync);
        var port = server.Start(settings.Current.Port);
        if (port != settings.Current.Port) settings.Update(s => s.Port = port);
        var origin = $"http://127.0.0.1:{port}";
        api.Origin = origin;
        state.LoadInBackground();

        window = new MainForm(settings, origin, args.Contains("--debug"));
        if (AppLinks.FromArgs(args) is { } link) window.OpenLink(link);
        AppLinks.Listen(received =>
        {
            if (window.IsHandleCreated) window.BeginInvoke(() => window.OpenLink(received));
        });
        var showRequests = ThreadPool.RegisterWaitForSingleObject(showWindow, (_, _) =>
        {
            if (window.IsHandleCreated) window.BeginInvoke(window.ShowFromTray);
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        var quitRequests = ThreadPool.RegisterWaitForSingleObject(quit, (_, _) =>
        {
            if (window.IsHandleCreated) window.BeginInvoke(window.Quit);
        }, null, Timeout.Infinite, executeOnlyOnce: true);
        Application.Run(window);
        showRequests.Unregister(null);
        quitRequests.Unregister(null);
        return window.PendingSetup;
    }

    /// <summary>The API is built before the window (the window needs the server's port).</summary>
    private sealed class DeferredWindow(Func<IWindowHost> window) : IWindowHost
    {
        public void QuitAndInstall(string setupPath) => window().QuitAndInstall(setupPath);
    }
}

using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Capstan;

public partial class App : Application
{
    private KeyboardHook? _hook;
    private AccentHook? _accentHook;
    private LanguageOverlay? _overlay;
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;

    private static string LogPath => Path.Combine(AppContext.BaseDirectory, "capstan.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log($"FATAL: {args.ExceptionObject}");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log($"UI Exception: {args.Exception}");
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Log($"Task Exception: {args.Exception}");
            args.SetObserved();
        };

        Log("App starting");

        try
        {
            _overlay = new LanguageOverlay();
            Log("LanguageOverlay created");

            _hook = new KeyboardHook(_overlay);
            _hook.Install();
            Log("KeyboardHook installed");

            _accentHook = new AccentHook();
            _accentHook.Install();
            Log("AccentHook installed");

            _mainWindow = new MainWindow();
            Log("MainWindow created");

            // Check if started with --minimized (e.g., from Windows startup)
            bool startMinimized = false;
            foreach (string arg in e.Args)
            {
                if (arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/minimized", StringComparison.OrdinalIgnoreCase))
                {
                    startMinimized = true;
                    break;
                }
            }

            if (!startMinimized)
            {
                _mainWindow.Show();
            }

            SetupTrayIcon();
            Log("App started successfully");
        }
        catch (Exception ex)
        {
            Log($"Startup failed: {ex}");
            throw;
        }
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = LoadIconFromResource(),
            Text = "Capstan",
            Visible = true
        };

        _trayIcon.MouseClick += (s, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                _mainWindow?.Show();
                _mainWindow?.Activate();
            }
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (s, e) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Activate();
        });
        menu.Items.Add("Exit", null, (s, e) => Shutdown());

        _trayIcon.ContextMenuStrip = menu;
    }

    private Icon LoadIconFromResource()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/capstan.ico");
            var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream != null)
            {
                return new Icon(stream);
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to load icon: {ex.Message}");
        }

        return SystemIcons.Application;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log("App exiting");
        _trayIcon?.Dispose();
        _hook?.Uninstall();
        _accentHook?.Uninstall();
        base.OnExit(e);
    }

    public static void Log(string message)
    {
        try
        {
            // Keep log under 1MB - delete if too big
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000)
            {
                File.Delete(LogPath);
            }

            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}";
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch { }
    }
}

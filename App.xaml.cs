using System;
using System.Drawing;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _overlay = new LanguageOverlay();
        _hook = new KeyboardHook(_overlay);
        _hook.Install();
        
        _accentHook = new AccentHook();
        _accentHook.Install();

        _mainWindow = new MainWindow();
        
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
        catch { }
        
        return SystemIcons.Application;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _hook?.Uninstall();
        _accentHook?.Uninstall();
        base.OnExit(e);
    }
}

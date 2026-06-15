using System.Windows;

namespace AppManager;

public partial class App : System.Windows.Application
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            var msg = ex.ExceptionObject is Exception e2 ? e2.Message : ex.ExceptionObject.ToString();
            System.Windows.MessageBox.Show($"未处理异常:\n{msg}", "错误");
        };

        DispatcherUnhandledException += (s, ex) =>
        {
            System.Windows.MessageBox.Show($"UI异常:\n{ex.Exception.Message}", "错误");
            ex.Handled = true;
        };

        SeedConfig();

        var mainWindow = new MainWindow();
        mainWindow.Show();
        CreateTrayIcon();
    }

    private static void SeedConfig()
    {
        // API key is configured via Settings dialog, stored in %AppData%\AppManager\config.json
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "程序管理器"
        };

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add("显示主窗口", null, (s, e) => ShowMainWindow());
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        contextMenu.Items.Add("退出", null, (s, e) => ExitApplication());

        _trayIcon.ContextMenuStrip = contextMenu;
        _trayIcon.DoubleClick += (s, e) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (MainWindow != null)
        {
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }
    }

    private void ExitApplication()
    {
        if (MainWindow != null)
        {
            MainWindow.Close();
        }
        else
        {
            _trayIcon?.Dispose();
            Current.Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}

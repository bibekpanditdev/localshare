using System.Drawing;
using System.Windows.Forms;

namespace LocalShareWindows;

/// <summary>
/// Runs the app as a tray icon rather than a normal window — the Windows equivalent of the
/// Android app's persistent foreground-service notification with a Stop button.
/// </summary>
public class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly AppSettings _settings;
    private readonly ServerManager _server;

    private SettingsForm? _settingsForm;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _statusItem;

    public TrayAppContext(AppSettings settings, ServerManager server, bool startMinimized)
    {
        _settings = settings;
        _server = server;

        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Server is OFF") { Enabled = false };
        _toggleItem = new ToolStripMenuItem("Start Sharing", null, async (_, _) => await ToggleServerAsync());
        var settingsItem = new ToolStripMenuItem("Settings...", null, (_, _) => ShowSettings());
        var copyItem = new ToolStripMenuItem("Copy Link", null, (_, _) => CopyLink());
        var exitItem = new ToolStripMenuItem("Exit", null, async (_, _) => await ExitAsync());

        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(copyItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "LocalShare — Server is OFF",
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ShowSettings();

        if (!startMinimized)
            ShowSettings();

        // Parity with Android's restartable foreground service: if launch-on-startup is
        // configured, bring the server straight up.
        if (startMinimized && StartupManager.IsEnabled())
            _ = ToggleServerAsync();

        RefreshStatus();
    }

    public void ShowSettings()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
            _settingsForm = new SettingsForm(_settings, _server, this);

        _settingsForm.Show();
        _settingsForm.WindowState = FormWindowState.Normal;
        _settingsForm.Activate();
    }

    public async Task ToggleServerAsync()
    {
        if (_server.IsRunning)
        {
            await _server.StopAsync();
        }
        else
        {
            var (ok, error) = await _server.StartAsync();
            if (!ok)
                MessageBox.Show(error, "LocalShare", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        RefreshStatus();
    }

    public void RefreshStatus()
    {
        bool running = _server.IsRunning;
        var primary = NetUtils.GetLocalIpAddresses().FirstOrDefault();

        _statusItem.Text = running
            ? $"Server is ON — http://{primary}:{_settings.Port}"
            : "Server is OFF";
        _toggleItem.Text = running ? "Stop Sharing" : "Start Sharing";

        var tooltip = running
            ? $"LocalShare — ON (http://{primary}:{_settings.Port})"
            : "LocalShare — Server is OFF";
        _trayIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip; // NotifyIcon.Text max length

        _settingsForm?.RefreshFromServerState();
    }

    private void CopyLink()
    {
        var primary = NetUtils.GetLocalIpAddresses().FirstOrDefault() ?? "127.0.0.1";
        var url = $"http://{primary}:{_settings.Port}";
        Clipboard.SetText(url);
        _trayIcon.ShowBalloonTip(2000, "LocalShare", $"Copied {url} to clipboard", ToolTipIcon.Info);
    }

    private async Task ExitAsync()
    {
        await _server.StopAsync();
        _trayIcon.Visible = false;
        Application.Exit();
    }
}

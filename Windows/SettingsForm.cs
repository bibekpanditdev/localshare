using System.Drawing;
using System.Windows.Forms;

namespace LocalShareWindows;

/// <summary>
/// Small settings window: Start/Stop, port, "share entire PC", hidden paths, launch-on-startup,
/// Copy Link, and the list of LAN IPs — the Windows equivalent of Android's MainActivity screen.
/// Config controls are disabled while the server is running, matching the Android behavior.
/// </summary>
public class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly ServerManager _server;
    private readonly TrayAppContext _tray;

    private readonly Label _statusLabel;
    private readonly Button _toggleButton;
    private readonly Button _copyLinkButton;
    private readonly NumericUpDown _portInput;
    private readonly CheckBox _shareEntirePcCheck;
    private readonly TextBox _hiddenPathsInput;
    private readonly CheckBox _launchOnStartupCheck;
    private readonly ListBox _ipList;

    public SettingsForm(AppSettings settings, ServerManager server, TrayAppContext tray)
    {
        _settings = settings;
        _server = server;
        _tray = tray;

        Text = "LocalShare";
        ClientSize = new Size(420, 470);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        int y = 15;

        _statusLabel = new Label { Left = 15, Top = y, Width = 390, Text = "Server is OFF" };
        Controls.Add(_statusLabel);
        y += 28;

        _toggleButton = new Button { Left = 15, Top = y, Width = 120, Height = 28, Text = "Start Sharing" };
        _toggleButton.Click += async (_, _) => await _tray.ToggleServerAsync();
        Controls.Add(_toggleButton);

        _copyLinkButton = new Button { Left = 150, Top = y, Width = 110, Height = 28, Text = "Copy Link" };
        _copyLinkButton.Click += (_, _) => CopyLink();
        Controls.Add(_copyLinkButton);
        y += 42;

        Controls.Add(new Label { Left = 15, Top = y + 3, Width = 100, Text = "Port:" });
        _portInput = new NumericUpDown
        {
            Left = 120, Top = y, Width = 100, Minimum = 1, Maximum = 65535, Value = _settings.Port
        };
        _portInput.ValueChanged += (_, _) => { _settings.Port = (int)_portInput.Value; _settings.Save(); };
        Controls.Add(_portInput);
        y += 35;

        _shareEntirePcCheck = new CheckBox
        {
            Left = 15, Top = y, Width = 380,
            Text = "Share entire PC (all fixed drives)",
            Checked = _settings.ShareEntirePc
        };
        _shareEntirePcCheck.CheckedChanged += (_, _) =>
        {
            _settings.ShareEntirePc = _shareEntirePcCheck.Checked;
            _settings.Save();
        };
        Controls.Add(_shareEntirePcCheck);
        y += 30;

        Controls.Add(new Label
        {
            Left = 15, Top = y, Width = 390, Text = "Hidden paths (comma-separated, relative to root):"
        });
        y += 20;
        _hiddenPathsInput = new TextBox { Left = 15, Top = y, Width = 390, Text = _settings.HiddenPaths };
        _hiddenPathsInput.Leave += (_, _) =>
        {
            _settings.HiddenPaths = _hiddenPathsInput.Text;
            _settings.Save();
        };
        Controls.Add(_hiddenPathsInput);
        y += 35;

        _launchOnStartupCheck = new CheckBox
        {
            Left = 15, Top = y, Width = 390,
            Text = "Launch on Windows startup (minimized to tray)",
            Checked = StartupManager.IsEnabled()
        };
        _launchOnStartupCheck.CheckedChanged += (_, _) =>
        {
            _settings.LaunchOnStartup = _launchOnStartupCheck.Checked;
            _settings.Save();
            StartupManager.SetEnabled(_launchOnStartupCheck.Checked);
        };
        Controls.Add(_launchOnStartupCheck);
        y += 35;

        Controls.Add(new Label { Left = 15, Top = y, Width = 390, Text = "Local network addresses:" });
        y += 20;
        _ipList = new ListBox { Left = 15, Top = y, Width = 390, Height = 110 };
        Controls.Add(_ipList);

        FormClosing += (_, e) =>
        {
            // The tray icon is the primary UI surface — closing the window just hides it.
            e.Cancel = true;
            Hide();
        };

        RefreshFromServerState();
    }

    public void RefreshFromServerState()
    {
        bool running = _server.IsRunning;
        var ips = NetUtils.GetLocalIpAddresses();

        _statusLabel.Text = running
            ? $"Server is ON — http://{ips.FirstOrDefault()}:{_settings.Port}"
            : "Server is OFF";
        _toggleButton.Text = running ? "Stop Sharing" : "Start Sharing";

        _portInput.Enabled = !running;
        _shareEntirePcCheck.Enabled = !running;
        _hiddenPathsInput.Enabled = !running;

        _ipList.Items.Clear();
        foreach (var ip in ips) _ipList.Items.Add(ip);
        if (_ipList.Items.Count == 0) _ipList.Items.Add("(no LAN connection detected)");
    }

    private void CopyLink()
    {
        var primary = NetUtils.GetLocalIpAddresses().FirstOrDefault() ?? "127.0.0.1";
        var url = $"http://{primary}:{_settings.Port}";
        Clipboard.SetText(url);
        MessageBox.Show($"Copied {url} to clipboard", "LocalShare", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

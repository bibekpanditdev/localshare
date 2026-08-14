using System.Windows.Forms;

namespace LocalShareWindows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var settings = AppSettings.Load();
        var serverManager = new ServerManager(settings);

        bool startMinimized = args.Contains("--minimized");
        var trayApp = new TrayAppContext(settings, serverManager, startMinimized);

        Application.Run(trayApp);
    }
}

namespace LocalShareWindows;

/// <summary>
/// Equivalent of the Android app's root list (LocalShare Folder / Internal Storage / SD Card).
/// "LocalShare Folder" is always present; whole-drive roots are opt-in via ShareEntirePc,
/// mirroring Android's "Full Storage" switch.
/// </summary>
public class RootManager
{
    public const string DefaultRootName = "LocalShare Folder";

    private readonly AppSettings _settings;

    public static string DefaultRootPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "LocalShare");

    public RootManager(AppSettings settings)
    {
        _settings = settings;
        Directory.CreateDirectory(DefaultRootPath);
    }

    public Dictionary<string, string> GetRoots()
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultRootName] = DefaultRootPath
        };

        if (_settings.ShareEntirePc)
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                var letter = drive.Name.TrimEnd('\\');
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
                roots[$"{label} ({letter})"] = drive.RootDirectory.FullName;
            }
        }

        return roots;
    }

    public string? ResolveRootPath(string rootName)
    {
        return GetRoots().TryGetValue(rootName, out var path) ? path : null;
    }

    public List<string> GetHiddenPaths() =>
        (_settings.HiddenPaths ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}

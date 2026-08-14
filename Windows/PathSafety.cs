namespace LocalShareWindows;

/// <summary>
/// Every endpoint that takes a client-supplied relative path must run it through ResolveSafe()
/// before touching the filesystem. This is the sole thing standing between a LAN client and the
/// rest of the disk, since this server has no authentication.
/// </summary>
public static class PathSafety
{
    /// <summary>
    /// Resolves a user-supplied relative path against a root directory and guarantees the
    /// result stays inside that root. Blocks absolute paths, drive-qualified paths, ".." escapes,
    /// and (by canonicalizing via GetFullPath) symlink/junction escapes.
    /// </summary>
    public static string ResolveSafe(string rootDir, string? relPath)
    {
        var rootFull = Path.GetFullPath(rootDir);

        relPath = (relPath ?? "").Replace('\\', '/').TrimStart('/');

        if (Path.IsPathRooted(relPath) || relPath.Contains(':'))
            throw new UnauthorizedAccessException("Invalid path");

        var candidate = Path.GetFullPath(Path.Combine(rootFull, relPath));

        var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        bool insideRoot =
            candidate.Equals(rootFull, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);

        if (!insideRoot)
            throw new UnauthorizedAccessException("Path escapes root");

        return candidate;
    }

    /// <summary>
    /// True if fullPath is at or under one of the user-configured hidden relative paths for this root.
    /// </summary>
    public static bool IsHidden(string rootDir, string fullPath, IEnumerable<string> hiddenRelPaths)
    {
        var rootFull = Path.GetFullPath(rootDir);
        var relFromRoot = Path.GetRelativePath(rootFull, fullPath).Replace('\\', '/');

        foreach (var raw in hiddenRelPaths)
        {
            var hidden = raw.Trim().Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(hidden)) continue;

            if (relFromRoot.Equals(hidden, StringComparison.OrdinalIgnoreCase) ||
                relFromRoot.StartsWith(hidden + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static readonly char[] InvalidChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

    public static string SanitizeFileName(string name)
    {
        var chars = name.Select(c => InvalidChars.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrEmpty(result) ? "file" : result;
    }

    /// <summary>
    /// If dir/desiredName already exists, appends " (1)", " (2)", ... before the extension until
    /// a free name is found — matches the Android uniqueIfExists() de-duplication scheme.
    /// </summary>
    public static string UniqueIfExists(string dir, string desiredName)
    {
        var full = Path.Combine(dir, desiredName);
        if (!File.Exists(full) && !Directory.Exists(full))
            return desiredName;

        var ext = Path.GetExtension(desiredName);
        var baseName = Path.GetFileNameWithoutExtension(desiredName);

        for (int i = 1; ; i++)
        {
            var candidate = $"{baseName} ({i}){ext}";
            var candidateFull = Path.Combine(dir, candidate);
            if (!File.Exists(candidateFull) && !Directory.Exists(candidateFull))
                return candidate;
        }
    }
}

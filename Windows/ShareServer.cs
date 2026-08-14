using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace LocalShareWindows;

/// <summary>
/// Maps every /api/* endpoint. Response shapes, status codes, query-param names, and range-request
/// behavior are written to match the Android ShareServer exactly, so the shared web UI (and a
/// client's saved delete tokens) behave identically against either server.
/// </summary>
public static class ShareServer
{
    private static readonly Dictionary<string, string[]> CategoryExtensions = new()
    {
        ["images"] = new[] { "jpg", "jpeg", "png", "gif", "bmp", "webp" },
        ["videos"] = new[] { "mp4", "mkv", "avi", "mov", "wmv", "flv" },
        ["music"] = new[] { "mp3", "wav", "ogg", "m4a", "flac" },
        ["docs"] = new[] { "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt" },
    };

    public static void MapEndpoints(WebApplication app, RootManager roots, TokenStore tokens, NsdHelper nsd)
    {
        app.MapGet("/api/roots", () =>
        {
            var all = roots.GetRoots();
            var ordered = new List<string> { RootManager.DefaultRootName };
            ordered.AddRange(all.Keys
                .Where(k => !k.Equals(RootManager.DefaultRootName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
            return Results.Json(ordered);
        });

        app.MapGet("/api/peers", () =>
        {
            lock (nsd.DiscoveredPeers)
            {
                return Results.Json(nsd.DiscoveredPeers);
            }
        });

        app.MapGet("/api/list", (string? path, string? category, string? root) =>
            HandleList(roots, path, category, root));

        app.MapGet("/api/download", async (HttpContext ctx, string? path, string? root, string? preview) =>
            await HandleDownload(ctx, roots, path, root, preview));

        app.MapPost("/api/upload", async (HttpRequest req, string? path, string? root) =>
            await HandleUpload(req, roots, tokens, path, root));

        app.MapPost("/api/mkdir", (string? path, string? root, string? name) =>
            HandleMkdir(roots, path, root, name));

        app.MapPost("/api/delete", (string? path, string? root, string? token) =>
            HandleDelete(roots, tokens, path, root, token));

        app.MapPost("/api/rename", (string? path, string? root, string? newName) =>
            HandleRename(roots, tokens, path, root, newName));
    }

    private static IResult ErrorResult(int status, string message) =>
        Results.Json(new { error = message }, statusCode: status);

    private static (string? rootPath, string rootName) ResolveRoot(RootManager roots, string? root)
    {
        var rootName = string.IsNullOrEmpty(root) ? RootManager.DefaultRootName : root;
        return (roots.ResolveRootPath(rootName), rootName);
    }

    // ---- GET /api/list ----------------------------------------------------

    private static IResult HandleList(RootManager roots, string? path, string? category, string? root)
    {
        category = string.IsNullOrEmpty(category) ? "all" : category;
        var (rootPath, rootName) = ResolveRoot(roots, root);
        if (rootPath == null) return ErrorResult(404, "Unknown root");

        string targetDir;
        try { targetDir = PathSafety.ResolveSafe(rootPath, path); }
        catch (UnauthorizedAccessException) { return ErrorResult(400, "Invalid path"); }

        if (!Directory.Exists(targetDir)) return ErrorResult(404, "Not found");

        var hidden = roots.GetHiddenPaths();
        var entries = new List<object>();

        try
        {
            if (category == "all")
            {
                var items = new DirectoryInfo(targetDir).GetFileSystemInfos()
                    .Where(fi => !PathSafety.IsHidden(rootPath, fi.FullName, hidden))
                    .OrderByDescending(fi => (fi.Attributes & FileAttributes.Directory) != 0)
                    .ThenBy(fi => fi.Name, StringComparer.OrdinalIgnoreCase);

                foreach (var fi in items)
                    entries.Add(ToEntry(rootPath, fi));
            }
            else
            {
                if (!CategoryExtensions.TryGetValue(category, out var exts))
                    return ErrorResult(400, "Invalid category");

                IEnumerable<FileInfo> files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                    .Where(f => exts.Contains(Path.GetExtension(f).TrimStart('.').ToLowerInvariant()))
                    .Where(f => !PathSafety.IsHidden(rootPath, f, hidden))
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc);

                foreach (var fi in files)
                    entries.Add(ToEntry(rootPath, fi));
            }
        }
        catch (UnauthorizedAccessException)
        {
            return ErrorResult(403, "Access denied");
        }
        catch (IOException)
        {
            return ErrorResult(400, "Folder unavailable");
        }

        var relCurrent = Path.GetRelativePath(rootPath, targetDir).Replace('\\', '/');
        if (relCurrent == ".") relCurrent = "";

        return Results.Json(new { path = relCurrent, category, root = rootName, entries });
    }

    private static object ToEntry(string rootPath, FileSystemInfo fi)
    {
        bool isDir = (fi.Attributes & FileAttributes.Directory) != 0;
        long size = isDir ? 0 : ((FileInfo)fi).Length;
        var relPath = Path.GetRelativePath(rootPath, fi.FullName).Replace('\\', '/');

        return new
        {
            name = fi.Name,
            isDir,
            size,
            modified = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
            path = relPath
        };
    }

    // ---- GET /api/download --------------------------------------------------

    private const long ChunkSize = 2 * 1024 * 1024; // ~2MB, matches Android's default range chunk

    private static async Task HandleDownload(
        HttpContext ctx, RootManager roots, string? path, string? root, string? preview)
    {
        var (rootPath, _) = ResolveRoot(roots, root);
        if (rootPath == null) { await WriteError(ctx, 404, "Unknown root"); return; }

        string fullPath;
        try { fullPath = PathSafety.ResolveSafe(rootPath, path); }
        catch (UnauthorizedAccessException) { await WriteError(ctx, 400, "Invalid path"); return; }

        if (!File.Exists(fullPath)) { await WriteError(ctx, 404, "Not found"); return; }

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(fullPath, out var mime)) mime = "application/octet-stream";

        var fileInfo = new FileInfo(fullPath);
        long totalLength = fileInfo.Length;
        bool isPreview = preview == "1";

        if (!isPreview)
        {
            var encodedName = Uri.EscapeDataString(fileInfo.Name);
            ctx.Response.Headers.Append("Content-Disposition", $"attachment; filename*=UTF-8''{encodedName}");
        }

        ctx.Response.Headers.Append("Accept-Ranges", "bytes");
        ctx.Response.ContentType = mime;

        long start = 0, end = totalLength - 1;
        bool isRangeRequest = false;

        var rangeHeader = ctx.Request.Headers.Range.ToString();
        if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
        {
            isRangeRequest = true;
            var spec = rangeHeader["bytes=".Length..];
            var parts = spec.Split('-');

            if (parts.Length == 2)
            {
                if (long.TryParse(parts[0], out var s)) start = s;

                if (!string.IsNullOrEmpty(parts[1]) && long.TryParse(parts[1], out var e))
                    end = e;
                else
                    // No explicit end given: serve a bounded chunk rather than the whole
                    // remaining file, matching the Android chunked-range behavior.
                    end = Math.Min(start + ChunkSize - 1, totalLength - 1);
            }
        }

        if (end >= totalLength) end = totalLength - 1;

        if (start < 0 || start > end || totalLength == 0)
        {
            ctx.Response.StatusCode = 416;
            ctx.Response.Headers.Append("Content-Range", $"bytes */{totalLength}");
            return;
        }

        var chunkLength = end - start + 1;

        if (isRangeRequest)
        {
            ctx.Response.StatusCode = 206;
            ctx.Response.Headers.Append("Content-Range", $"bytes {start}-{end}/{totalLength}");
        }
        else
        {
            ctx.Response.StatusCode = 200;
        }

        ctx.Response.ContentLength = chunkLength;

        await using var stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        stream.Seek(start, SeekOrigin.Begin);

        var buffer = new byte[81920];
        long remaining = chunkLength;

        try
        {
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                int read = await stream.ReadAsync(buffer.AsMemory(0, toRead), ctx.RequestAborted);
                if (read == 0) break;
                await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, read), ctx.RequestAborted);
                remaining -= read;
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected mid-stream (e.g. seeked elsewhere) — not an error.
        }
    }

    private static async Task WriteError(HttpContext ctx, int status, string message)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { error = message });
    }

    // ---- POST /api/upload --------------------------------------------------

    private const int UploadBufferSize = 16 * 1024 * 1024; // matches Android's 16MB buffer

    private static async Task<IResult> HandleUpload(
        HttpRequest req, RootManager roots, TokenStore tokens, string? path, string? root)
    {
        var (rootPath, _) = ResolveRoot(roots, root);
        if (rootPath == null) return ErrorResult(404, "Unknown root");

        string targetDir;
        try { targetDir = PathSafety.ResolveSafe(rootPath, path); }
        catch (UnauthorizedAccessException) { return ErrorResult(400, "Invalid path"); }

        if (!req.HasFormContentType) return ErrorResult(400, "Expected multipart/form-data");

        Directory.CreateDirectory(targetDir);

        var form = await req.ReadFormAsync();
        var file = form.Files["file"];
        if (file == null || file.Length == 0) return ErrorResult(400, "No file provided");

        var safeName = PathSafety.SanitizeFileName(file.FileName);
        var uniqueName = PathSafety.UniqueIfExists(targetDir, safeName);
        var destPath = Path.Combine(targetDir, uniqueName);

        try
        {
            await using var dest = new FileStream(
                destPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: UploadBufferSize, useAsync: true);
            await file.CopyToAsync(dest);
        }
        catch (Exception ex)
        {
            return ErrorResult(500, ex.Message);
        }

        var token = tokens.IssueToken(destPath);
        return Results.Json(new { ok = true, name = uniqueName, token });
    }

    // ---- POST /api/mkdir ----------------------------------------------------

    private static IResult HandleMkdir(RootManager roots, string? path, string? root, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return ErrorResult(400, "Missing name");

        var (rootPath, _) = ResolveRoot(roots, root);
        if (rootPath == null) return ErrorResult(404, "Unknown root");

        string targetDir;
        try { targetDir = PathSafety.ResolveSafe(rootPath, path); }
        catch (UnauthorizedAccessException) { return ErrorResult(400, "Invalid path"); }

        var safeName = PathSafety.SanitizeFileName(name);
        var newDir = Path.Combine(targetDir, safeName);

        if (Directory.Exists(newDir) || File.Exists(newDir))
            return ErrorResult(409, "Already exists");

        try { Directory.CreateDirectory(newDir); }
        catch (Exception ex) { return ErrorResult(500, ex.Message); }

        return Results.Json(new { ok = true });
    }

    // ---- POST /api/delete ----------------------------------------------------

    private static IResult HandleDelete(
        RootManager roots, TokenStore tokens, string? path, string? root, string? token)
    {
        var (rootPath, _) = ResolveRoot(roots, root);
        if (rootPath == null) return ErrorResult(404, "Unknown root");

        if (string.IsNullOrEmpty(path)) return ErrorResult(403, "Cannot delete root");

        string targetPath;
        try { targetPath = PathSafety.ResolveSafe(rootPath, path); }
        catch (UnauthorizedAccessException) { return ErrorResult(400, "Invalid path"); }

        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            return ErrorResult(404, "Not found");

        if (tokens.TryGetToken(targetPath, out var expected))
        {
            if (!string.Equals(expected, token, StringComparison.Ordinal))
                return ErrorResult(403, "You don't have authority to delete this file");
        }

        try
        {
            if (Directory.Exists(targetPath))
                Directory.Delete(targetPath, recursive: true);
            else
                File.Delete(targetPath);
        }
        catch (Exception ex)
        {
            return ErrorResult(500, ex.Message);
        }

        tokens.Forget(targetPath);
        return Results.Json(new { ok = true });
    }

    // ---- POST /api/rename ----------------------------------------------------

    private static IResult HandleRename(
        RootManager roots, TokenStore tokens, string? path, string? root, string? newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return ErrorResult(400, "Missing newName");

        var (rootPath, _) = ResolveRoot(roots, root);
        if (rootPath == null) return ErrorResult(404, "Unknown root");

        if (string.IsNullOrEmpty(path)) return ErrorResult(403, "Cannot rename root");

        string targetPath;
        try { targetPath = PathSafety.ResolveSafe(rootPath, path); }
        catch (UnauthorizedAccessException) { return ErrorResult(400, "Invalid path"); }

        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            return ErrorResult(404, "Not found");

        var parentDir = Path.GetDirectoryName(targetPath)!;
        var safeName = PathSafety.SanitizeFileName(newName);
        var uniqueName = PathSafety.UniqueIfExists(parentDir, safeName);
        var destPath = Path.Combine(parentDir, uniqueName);

        try
        {
            if (Directory.Exists(targetPath))
                Directory.Move(targetPath, destPath);
            else
                File.Move(targetPath, destPath);
        }
        catch (Exception ex)
        {
            return ErrorResult(500, ex.Message);
        }

        // Carry the delete-token association forward to the new path/name, if one existed.
        if (tokens.TryGetToken(targetPath, out var existingToken) && existingToken != null)
        {
            tokens.Forget(targetPath);
            tokens.IssueTokenWithValue(destPath, existingToken);
        }

        return Results.Json(new { ok = true });
    }
}

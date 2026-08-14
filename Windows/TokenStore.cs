using System.Collections.Concurrent;

namespace LocalShareWindows;

/// <summary>
/// Tracks which upload-session token "owns" each file, so /api/delete can enforce the same
/// soft, non-authenticated delete-authority model as the Android app: whoever uploaded a file
/// through this server gets a token that lets them delete it later; pre-existing files with no
/// known token can be deleted by anyone (same as Android's behavior for files it didn't upload).
/// </summary>
public class TokenStore
{
    private readonly ConcurrentDictionary<string, string> _tokensByPath = new(StringComparer.OrdinalIgnoreCase);

    public string IssueToken(string fullPath)
    {
        var token = Guid.NewGuid().ToString();
        _tokensByPath[fullPath] = token;
        return token;
    }

    public void IssueTokenWithValue(string fullPath, string token) => _tokensByPath[fullPath] = token;

    public bool TryGetToken(string fullPath, out string? token) => _tokensByPath.TryGetValue(fullPath, out token);

    public void Forget(string fullPath) => _tokensByPath.TryRemove(fullPath, out _);
}

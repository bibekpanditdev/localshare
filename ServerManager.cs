using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LocalShareWindows;

/// <summary>
/// Owns the Kestrel WebApplication instance. Start/Stop rebuild the host each time so a changed
/// port takes effect on the next start — same net effect as Android's ServerService being
/// stopped and restarted.
/// </summary>
public class ServerManager
{
    private WebApplication? _app;
    private readonly NsdHelper _nsd = new();

    public bool IsRunning => _app != null;
    public AppSettings Settings { get; }
    public RootManager Roots { get; }
    public TokenStore Tokens { get; } = new();
    public NsdHelper Nsd => _nsd;

    public ServerManager(AppSettings settings)
    {
        Settings = settings;
        Roots = new RootManager(settings);
    }

    public async Task<(bool ok, string? error)> StartAsync()
    {
        if (IsRunning) return (true, null);

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                });
            });
            builder.WebHost.UseUrls($"http://0.0.0.0:{Settings.Port}");
            builder.Logging.ClearProviders();
            builder.Environment.WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");

            var app = builder.Build();
            app.UseCors();
            app.UseDefaultFiles();
            app.UseStaticFiles();

            ShareServer.MapEndpoints(app, Roots, Tokens, _nsd);

            await app.StartAsync();
            _app = app;
            _nsd.Start(Settings.Port);
            _ = RunBackgroundSync();
            return (true, null);
        }
        catch (AddressInUseException)
        {
            return (false, "Failed to start server. Port might be in use.");
        }
        catch (IOException)
        {
            return (false, "Failed to start server. Port might be in use.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to start server. {ex.Message}");
        }
    }

    private async Task RunBackgroundSync()
    {
        using var client = new HttpClient();
        while (IsRunning)
        {
            try
            {
                var peers = new List<string>();
                lock (_nsd.DiscoveredPeers) peers.AddRange(_nsd.DiscoveredPeers.Values);

                var localPath = RootManager.DefaultRootPath;
                var localFiles = Directory.GetFiles(localPath).Select(Path.GetFileName).ToHashSet();

                foreach (var peerUrl in peers)
                {
                    try
                    {
                        var json = await client.GetStringAsync($"{peerUrl}/api/list?path=&category=all&root=LocalShare%20Folder");
                        var data = System.Text.Json.JsonDocument.Parse(json);
                        var entries = data.RootElement.GetProperty("entries");

                        foreach (var entry in entries.EnumerateArray())
                        {
                            var name = entry.GetProperty("name").GetString();
                            var isDir = entry.GetProperty("isDir").GetBoolean();
                            var path = entry.GetProperty("path").GetString();

                            if (!isDir && name != null && !localFiles.Contains(name))
                            {
                                var fileData = await client.GetByteArrayAsync($"{peerUrl}/api/download?path={Uri.EscapeDataString(path!)}&root=LocalShare%20Folder");
                                await File.WriteAllBytesAsync(Path.Combine(localPath, name), fileData);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            await Task.Delay(60000); // Sync every minute
        }
    }

    public async Task StopAsync()
    {
        _nsd.Stop();
        if (_app == null) return;

        var app = _app;
        _app = null;

        await app.StopAsync();
        await app.DisposeAsync();
    }
}

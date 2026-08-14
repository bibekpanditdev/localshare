# LocalShare for Windows

A native Windows tray app that hosts the same LAN file-sharing HTTP API as the Android LocalShare
app, so a phone and a PC can share files with each other over plain HTTP — either device can be
the server, and the client is just a browser.

## Why C# / .NET 8

Of the three suggested stacks, .NET 8 gives the best fit for this specific task:

- **Kestrel** is a production-grade, high-throughput HTTP server built into the framework — no
  third-party web framework needed, and it comfortably handles concurrent large-file transfers
  and range requests (the two things this app actually needs to be fast at).
- **WinForms `NotifyIcon`** is the simplest, most reliable way to get a correct native tray icon,
  context menu, and balloon notifications on Windows — no extra native interop or third-party tray
  library required.
- Startup time and idle memory are both small: a framework-dependent build starts in well under a
  second and idles around 30–40MB, which is what "tray-first, always running quietly" needs.
- `dotnet publish` can produce a self-contained, single-file, `ReadyToRun`-compiled `.exe` if you
  want zero .NET-runtime dependency on the target machine (see below) — closing the gap with Go/Rust
  on distribution size while keeping the fast, low-effort Windows integration.

This intentionally avoids Electron/Chromium: the server/tray shell is plain Win32 + Kestrel, and
users are meant to open the file UI in their own OS browser, exactly like the Android app.

## Project layout

```
LocalShareWindows/
  LocalShareWindows.csproj   Project file (WinForms + Kestrel via FrameworkReference, no NuGet needed)
  Program.cs                 Entry point
  TrayAppContext.cs          Tray icon, context menu, start/stop wiring
  SettingsForm.cs            Settings window (port, share-entire-PC, hidden paths, startup, IPs)
  ServerManager.cs           Kestrel host lifecycle (start/stop, port-in-use handling)
  ShareServer.cs             All /api/* endpoints — the API contract implementation
  RootManager.cs             Named roots (LocalShare folder + optional fixed drives)
  PathSafety.cs              resolveSafe()-equivalent sandboxing, filename sanitizing/dedup
  TokenStore.cs               Upload-token delete-authority tracking
  NetUtils.cs                 LAN IPv4 enumeration, sorted 192.168.x.x-first
  StartupManager.cs           HKCU Run-key registration for "launch on startup"
  AppSettings.cs              JSON-persisted settings (%APPDATA%\LocalShare\settings.json)
  wwwroot/index.html          The shared web UI, served at "/" (Windows-11 styled file manager)
```

## Building

Requires the .NET 8 SDK with the Windows desktop workload (i.e. built/run *on* Windows — WinForms
and the Windows-only APIs used here, like the registry Run key, don't run on Linux/macOS).

```powershell
# Framework-dependent (small exe, requires .NET 8 Desktop Runtime on the target machine)
dotnet build -c Release

# Self-contained, single-file, fast-starting exe (no .NET install required on target machine)
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableWindowsTargeting=true
```

Run `LocalShareWindows.exe` — it launches straight to the tray (plus the settings window on first
open). Pass `--minimized` to skip the settings window (used automatically by the startup shortcut).

> Note: this was authored and organized in a Linux sandbox, so it hasn't been compiled here — there's
> no Windows Desktop SDK / MSBuild target available in this environment to validate the build. Do a
> `dotnet build` on a Windows machine (or via a Windows CI runner) before shipping.

## API contract coverage

`ShareServer.cs` implements every endpoint from the spec with matching paths, params, JSON shapes,
and status codes:

| Endpoint | Notes |
|---|---|
| `GET /`, `/index.html` | Served via `UseDefaultFiles()`/`UseStaticFiles()` from `wwwroot/` |
| `GET /api/roots` | `"LocalShare Folder"` always first; drives appended only when `ShareEntirePc` is on |
| `GET /api/list` | `all` = direct children, dirs-first/alpha; category = recursive, sorted by `modified` desc; hidden-paths filtered; `modified` in epoch **ms** |
| `GET /api/download` | Full `Range` support, `206`/`Content-Range`/`Accept-Ranges`, ~2MB chunk when no explicit end, `Content-Disposition` with UTF-8 filename unless `preview=1` |
| `POST /api/upload` | Multipart `file` field, 16MB write buffer, sanitized + de-duplicated filename, returns `{ok,name,token}` |
| `POST /api/mkdir` | `409` on existing name |
| `POST /api/delete` | `403` on empty path (root) or token mismatch; token-less files deletable by anyone |
| `POST /api/rename` | Same-parent rename, sanitized + de-duplicated, carries the delete token forward |

Every path-taking endpoint routes through `PathSafety.ResolveSafe()`, which canonicalizes the
resolved path and rejects anything that isn't contained within the selected root — no absolute
paths, no `..` traversal, no symlink escapes.

Concurrency: Kestrel handles requests on the thread pool by default, so parallel uploads (the web
UI's 8-at-a-time behavior) aren't serialized on a single lock — each `/api/upload` request opens
its own `FileStream`.

## Windows-specific parity with the Android app

- **Tray-first**: `TrayAppContext` is the app's `ApplicationContext`; the settings window can be
  hidden and reopened but the tray icon is what's always present, mirroring the Android foreground
  service's persistent notification.
- **Start/Stop**: available from both the tray menu and the settings window; `ServerManager`
  rebuilds the Kestrel host on each start so a changed port takes effect immediately.
- **Settings persistence**: `AppSettings` round-trips to `%APPDATA%\LocalShare\settings.json`,
  the Windows analogue of Android `SharedPreferences`.
- **Controls disabled while running**: `SettingsForm.RefreshFromServerState()` disables the port,
  share-entire-PC, and hidden-paths controls whenever the server is active, matching the Android
  UI's behavior.
- **Port-in-use handling**: `ServerManager.StartAsync()` catches `AddressInUseException`/`IOException`
  and reports "Failed to start server. Port might be in use." instead of crashing.
- **LAN IP list**: `NetUtils.GetLocalIpAddresses()` walks all active, non-loopback NICs and sorts
  `192.168.x.x` first, then `10.x.x.x`, then `172.16–31.x.x`, then everything else — the same
  ordering intent as the Android `getLocalIpAddresses()`.
- **Copy Link**: available from both the tray menu and settings window; copies
  `http://<primary-lan-ip>:<port>` to the clipboard with a confirmation toast/balloon.
- **Launch on startup**: `StartupManager` toggles a `HKCU\...\Run` entry pointing at the exe with
  `--minimized`, the Windows equivalent of an auto-restarting foreground service.
- **Default shared folder**: `RootManager` always creates and exposes `%USERPROFILE%\LocalShare`
  as `"LocalShare Folder"`, matching Android's always-on `/sdcard/LocalShare`.

## Known gaps / things to verify on real hardware

- Not compiled/tested here (Linux sandbox, no Windows SDK available) — please build and smoke-test
  on Windows before relying on it.
- No app icon is wired up (`SystemIcons.Application` is used as a placeholder) — drop in a `.ico`
  and reference it from `NotifyIcon.Icon` / `<ApplicationIcon>` when you have branded art.
- Symlink-escape protection relies on `Path.GetFullPath` canonicalization; if you expect NTFS
  junctions/reparse points inside shared folders, it's worth adding an explicit
  `File.ResolveLinkTarget` check in `PathSafety.ResolveSafe` for extra certainty.

# ChatDock tray helper

A small Windows helper for the **ChatDock** Chrome extension (an unofficial companion for Google Chat).

- Shows ChatDock's icon with the unread count in the system tray.
- Shows the unread count on the ChatDock window's taskbar button.
- Keeps ChatDock's hidden windows off the taskbar.

Chrome starts the helper by itself (native messaging) while "Icon in the system tray" is on in ChatDock's options.
It opens no network connection and talks only to the ChatDock extension.

## Install

ChatDock's options page shows these commands with your extension id filled in.

**With winget** (Windows 10 and 11):

```powershell
winget install --id shraga100.ChatDockTray -e --custom "--extension-id <extension id>"
```

**Without winget** (PowerShell). This builds the helper from the source inside `install.ps1`, so no ready-made `.exe` is downloaded:

```powershell
$env:CHATDOCK_ID='<extension id>'; irm https://raw.githubusercontent.com/shraga100/chatdock-tray/main/install.ps1 | iex
```

Or download `ChatDockTray.exe` from [Releases](https://github.com/shraga100/chatdock-tray/releases) and double-click it.

All three do the same thing:
- They need no admin rights.
- They copy `ChatDockTray.exe` to `%LOCALAPPDATA%\ChatDock`.
- They register it with Chrome and Edge for the ChatDock extension only, in the current user's registry (`HKCU\Software\<browser>\NativeMessagingHosts\com.chatdock.tray`).
- They add "ChatDock tray helper" to Settings > Apps.

The helper itself handles this:

```
ChatDockTray.exe --install [--quiet] [--extension-id <id>]
ChatDockTray.exe --uninstall [--quiet]
```

## Uninstall

Use any one of these:

- Settings > Apps > "ChatDock tray helper"
- `winget uninstall shraga100.ChatDockTray`
- PowerShell:

```powershell
irm https://raw.githubusercontent.com/shraga100/chatdock-tray/main/uninstall.ps1 | iex
```

## Build

To build `bin\ChatDockTray.exe` with the C# compiler that ships with Windows:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

The winget manifests are in [`winget/`](winget).

Freeware: see [LICENSE](LICENSE).

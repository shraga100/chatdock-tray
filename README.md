# ChatDock tray helper

A small Windows helper for the **ChatDock** Chrome extension (an unofficial companion for Google Chat).
It shows ChatDock's icon with the unread count in the system tray and on the ChatDock window's taskbar button, and keeps ChatDock's hidden windows off the taskbar.

## Install

ChatDock's options page shows the command with your extension id filled in. Paste it into PowerShell:

```powershell
$env:CHATDOCK_ID='<extension id>'; irm https://raw.githubusercontent.com/shraga100/chatdock-tray/main/install.ps1 | iex
```

- Current user only, no admin rights.
- No ready-made `.exe` is downloaded: `install.ps1` builds `ChatDockTray.exe` from the C# source it contains
  (`ChatDockTray.cs`, the same code as in this repository) with the compiler that ships with Windows (.NET Framework 4).
- Files go to `%LOCALAPPDATA%\ChatDock`. Chrome and Edge learn about it through
  `HKCU\Software\<browser>\NativeMessagingHosts\com.chatdock.tray`, for the ChatDock extension only.
- Chrome starts the helper itself (native messaging) while "Icon in the system tray" is on in ChatDock.
  It opens no network connection.

## Uninstall

```powershell
irm https://raw.githubusercontent.com/shraga100/chatdock-tray/main/uninstall.ps1 | iex
```

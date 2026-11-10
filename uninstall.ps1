# Removes the ChatDock tray helper (registry entries and %LOCALAPPDATA%\ChatDock).
#   irm https://raw.githubusercontent.com/shraga100/chatdock-tray/main/uninstall.ps1 | iex
& {
  Get-Process ChatDockTray -ErrorAction SilentlyContinue | Stop-Process -Force
  foreach ($browser in 'Google\Chrome', 'Microsoft\Edge') {
    Remove-Item "HKCU:\Software\$browser\NativeMessagingHosts\com.chatdock.tray" -Recurse -Force -ErrorAction SilentlyContinue
  }
  Start-Sleep -Milliseconds 300
  Remove-Item (Join-Path $env:LOCALAPPDATA 'ChatDock') -Recurse -Force -ErrorAction SilentlyContinue
  Write-Host 'ChatDock tray helper removed.' -ForegroundColor Green
}

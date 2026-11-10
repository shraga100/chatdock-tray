# Removes the ChatDock tray helper (same as Settings > Apps > "ChatDock tray helper").
#   irm https://raw.githubusercontent.com/shraga100/chatdock-tray/main/uninstall.ps1 | iex
& {
  $exe = Join-Path $env:LOCALAPPDATA 'ChatDock\ChatDockTray.exe'
  if (Test-Path $exe) {
    # Version 1.0.0 and later remove themselves (registry, installed-apps entry, folder).
    $p = Start-Process -FilePath $exe -ArgumentList '--uninstall', '--quiet' -Wait -PassThru
    if ($p.ExitCode -eq 0) { Start-Sleep -Seconds 3 }
  }
  # Older helpers, or anything left behind.
  Get-Process ChatDockTray -ErrorAction SilentlyContinue | Stop-Process -Force
  foreach ($browser in 'Google\Chrome', 'Microsoft\Edge') {
    Remove-Item "HKCU:\Software\$browser\NativeMessagingHosts\com.chatdock.tray" -Recurse -Force -ErrorAction SilentlyContinue
  }
  Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ChatDockTray' -Recurse -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 300
  Remove-Item (Join-Path $env:LOCALAPPDATA 'ChatDock') -Recurse -Force -ErrorAction SilentlyContinue
  Write-Host 'ChatDock tray helper removed.' -ForegroundColor Green
}

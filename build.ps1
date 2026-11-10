# Builds bin\ChatDockTray.exe from ChatDockTray.cs with the C# compiler that
# ships with Windows (.NET Framework 4). This is the file attached to each
# GitHub release and installed by winget.
#   powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force (Join-Path $here 'bin') | Out-Null
$exe = Join-Path $here 'bin\ChatDockTray.exe'
& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$exe" `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll `
  "/resource:$(Join-Path $here 'icons\icon.png'),icon.png" `
  "/resource:$(Join-Path $here 'icons\icon-off.png'),icon-off.png" `
  (Join-Path $here 'ChatDockTray.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host "Built $exe"
Write-Host "SHA256 $((Get-FileHash $exe -Algorithm SHA256).Hash)"

param([string]$Version='1.0.0')
$ErrorActionPreference='Stop'
# Builds the release assets into dist\:
#   MagicKeyboardSetup.exe                  one-file installer (driver package + tray embedded)
#   MagicKeyboard-<version>-manual.zip      driver package, tray app and PowerShell installer
& (Join-Path $PSScriptRoot 'Build-Gui.ps1')
$driverLog=& cmd /c ('"'+(Join-Path $PSScriptRoot 'driver\Build.cmd')+'" 2>&1')
if($LASTEXITCODE){$driverLog | Out-Host;throw 'Driver build failed.'}
$package=Join-Path $PSScriptRoot 'driver\x64\Release\package'
$dist=Join-Path $PSScriptRoot 'dist'
if(Test-Path -LiteralPath $dist){Remove-Item -LiteralPath $dist -Recurse -Force}
$manual=Join-Path $dist "MagicKeyboard-$Version-manual"
New-Item -ItemType Directory -Force -Path (Join-Path $manual 'driver') | Out-Null
Copy-Item (Join-Path $package '*') (Join-Path $manual 'driver')
Copy-Item (Join-Path $PSScriptRoot 'installer\Install-MagicKeyboardFn.ps1') $manual
Copy-Item (Join-Path $PSScriptRoot 'installer\README.txt') $manual
Compress-Archive -Path (Join-Path $manual '*') -DestinationPath "$manual.zip"

$compiler=Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$resources=Get-ChildItem $package -File | Where-Object Name -ne 'MagicKeyboardFn.pdb' | ForEach-Object {'/resource:'+$_.FullName+',pkg/'+$_.Name}
& $compiler /nologo /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
  ('/win32manifest:'+(Join-Path $PSScriptRoot 'installer\setup.manifest')) ('/out:'+(Join-Path $dist 'MagicKeyboardSetup.exe')) @resources (Join-Path $PSScriptRoot 'installer\Setup.cs')
if($LASTEXITCODE){throw 'Setup build failed.'}
Get-ChildItem $dist -File | Select-Object Name,Length

$ErrorActionPreference='Stop'
$executable=Join-Path $PSScriptRoot 'MagicKeyboardNativeMode.exe'
if(-not (Test-Path $executable)) { & (Join-Path $PSScriptRoot 'Build.ps1') }
$startup=[Environment]::GetFolderPath('Startup')
$linkPath=Join-Path $startup 'Magic Keyboard 0320 Native Controls.lnk'
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut($linkPath)
$shortcut.TargetPath=$executable
$shortcut.WorkingDirectory=$PSScriptRoot
$shortcut.Description='Enable Apple Magic Keyboard native Windows brightness controls'
$shortcut.WindowStyle=7
$shortcut.Save()
Start-Process -FilePath $executable -WindowStyle Hidden
Write-Output ('Startup enabled: '+$linkPath)

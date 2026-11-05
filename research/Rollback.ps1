# Run as administrator to restore the exact pre-project USB keyboard filters.
$ErrorActionPreference='Stop'
Start-Transcript (Join-Path $PSScriptRoot 'rollback.log') -Append
try {
    $startup=Join-Path ([Environment]::GetFolderPath('Startup')) 'Magic Keyboard 0320 Native Controls.lnk'
    if(Test-Path $startup) { Remove-Item -LiteralPath $startup }
    $helper=Join-Path $PSScriptRoot 'MagicKeyboardNativeMode.exe'
    Get-CimInstance Win32_Process -Filter "Name='MagicKeyboardNativeMode.exe'" | Where-Object ExecutablePath -eq $helper | ForEach-Object { Stop-Process -Id $_.ProcessId }
    & (Join-Path $PSScriptRoot 'Stop-MagicControls.ps1')
    $baseline=Get-Content (Join-Path $PSScriptRoot 'before-install.json') -Raw | ConvertFrom-Json
    $id=$baseline.InstanceId
    if($id -notlike 'USB\VID_05AC&PID_0320&MI_01\*') { throw 'Unexpected backup device.' }
    $reg='HKLM:\SYSTEM\CurrentControlSet\Enum\'+$id
    if($baseline.HadLowerFilters) {
        New-ItemProperty $reg -Name LowerFilters -PropertyType MultiString -Value ([string[]]$baseline.LowerFilters) -Force | Out-Null
    } else { Remove-ItemProperty $reg -Name LowerFilters -ErrorAction SilentlyContinue }
    & pnputil /restart-device $id
    if($LASTEXITCODE -ne 0) { throw 'Filter restored; reconnect the keyboard to activate it.' }
    Get-PnpDevice -InstanceId $id | Format-List Status,Problem
    'Original USB keyboard filters restored. Apple packages remain staged for inspection.'
} finally { Stop-Transcript }

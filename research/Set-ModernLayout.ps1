$ErrorActionPreference='Stop'
Start-Transcript (Join-Path $PSScriptRoot 'modern-layout.log')
try {
    $saved=Get-Content (Join-Path $PSScriptRoot 'before-install.json') -Raw | ConvertFrom-Json
    $id=$saved.InstanceId
    $device=Get-PnpDevice -PresentOnly -InstanceId $id
    if ($id -notlike 'USB\VID_05AC&PID_0320&MI_01\*') { throw 'Unexpected device.' }
    $path='HKLM:\SYSTEM\CurrentControlSet\Services\KeyMagic\KeyboardTable'
    $previous=(Get-ItemProperty $path).'VID_05AC&PID_0320'
    if ($previous.Count -ne 5) { throw 'Unexpected table structure.' }
    [byte[]]$next=$previous.Clone()
    $next[2]=3 # NEW_LAYOUT2 from Apple's INF; modern F7-F12 media order.
    New-ItemProperty $path -Name 'VID_05AC&PID_0320' -PropertyType Binary -Value $next -Force | Out-Null
    & pnputil /restart-device $id
    if ($LASTEXITCODE -ne 0) { throw 'Restart failed.' }
    Start-Sleep -Seconds 2
    $device=Get-PnpDevice -PresentOnly -InstanceId $id
    if ($device.Status -ne 'OK') {
        New-ItemProperty $path -Name 'VID_05AC&PID_0320' -PropertyType Binary -Value $previous -Force | Out-Null
        & pnputil /restart-device $id
        throw 'Device failed to start; previous layout restored.'
    }
    'MODERN_LAYOUT_OK'
    Get-ItemProperty $path | Select-Object 'VID_05AC&PID_0320' | Format-List
    Get-Service KeyMagic | Format-List Name,Status
} catch { 'MODERN_LAYOUT_ERROR: '+$_.Exception.Message; exit 1 }
finally { Stop-Transcript }

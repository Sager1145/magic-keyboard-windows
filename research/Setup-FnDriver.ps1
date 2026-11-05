param([switch]$Remove)
# Attaches the test-signed MagicKeyboardFn report filter below Apple KeyMagic2 on
# this keyboard's USB interface only. Run in an administrator PowerShell.
$ErrorActionPreference='Stop'
$service='MagicKeyboardFn'
$binary=Join-Path $env:windir "System32\drivers\$service.sys"
$source=Join-Path $PSScriptRoot "driver\x64\Release\$service.sys"
$backup=Join-Path $PSScriptRoot 'driver\filters-before.json'
$device=Get-PnpDevice -PresentOnly | Where-Object InstanceId -like 'USB\VID_05AC&PID_0320&MI_01\*'
if(@($device).Count -ne 1){throw 'Connect the USB-C Magic Keyboard (05AC:0320) first.'}
$registry="HKLM:\SYSTEM\CurrentControlSet\Enum\$($device.InstanceId)"
function Restore-Filters {
    $saved=Get-Content -LiteralPath $backup -Raw | ConvertFrom-Json
    if($saved.Present){
        New-ItemProperty -LiteralPath $registry -Name LowerFilters -PropertyType MultiString -Value ([string[]]$saved.Filters) -Force | Out-Null
    } else {
        Remove-ItemProperty -LiteralPath $registry -Name LowerFilters -ErrorAction SilentlyContinue
    }
}
function Restart-Keyboard {
    & pnputil.exe /restart-device $device.InstanceId | Out-Null
    if($LASTEXITCODE -eq 3010){return $false}
    if($LASTEXITCODE){throw "Keyboard restart returned $LASTEXITCODE."}
    return $true
}
Start-Transcript (Join-Path $PSScriptRoot 'driver\install.log') -Append
try {
    if($Remove){
        Restore-Filters
        $restarted=Restart-Keyboard
        & sc.exe delete $service | Out-Null
        Remove-Item -LiteralPath $binary -ErrorAction SilentlyContinue
        if(-not $restarted -or (Test-Path -LiteralPath $binary)){'FNDRIVER_REMOVED_REBOOT_REQUIRED'}else{'FNDRIVER_REMOVED'}
        return
    }
    if(Get-Service $service -ErrorAction SilentlyContinue){throw "$service is already installed. Run with -Remove first."}
    if((Get-AuthenticodeSignature -LiteralPath $source).Status -ne 'Valid'){throw 'Build and sign the driver with driver\Build.cmd first.'}
    if((bcdedit /enum '{current}' | Select-String '^testsigning\s+Yes') -eq $null){throw 'Windows test signing mode is off; the test-signed filter cannot load.'}
    $filters=@((Get-Item -LiteralPath $registry).GetValue('LowerFilters'))
    if($filters -contains $service){throw "$service is already listed as a filter."}
    @{Present=$filters.Count -gt 0 -and $null -ne $filters[0];Filters=@($filters | Where-Object {$_})} | ConvertTo-Json | Set-Content -LiteralPath $backup
    Copy-Item -LiteralPath $source -Destination $binary -Force
    try {
        & sc.exe create $service type= kernel start= demand error= normal binPath= "System32\drivers\$service.sys" | Out-Null
        if($LASTEXITCODE){throw 'Service creation failed.'}
        # First entry attaches lowest, so the filter sees reports before KeyMagic2.
        New-ItemProperty -LiteralPath $registry -Name LowerFilters -PropertyType MultiString -Value ([string[]](@($service)+@($filters | Where-Object {$_}))) -Force | Out-Null
        if(-not (Restart-Keyboard)){'FNDRIVER_REBOOT_REQUIRED';return}
        Start-Sleep -Seconds 2
        if(-not (Get-PnpDevice -InstanceId $device.InstanceId -PresentOnly | Where-Object Status -eq 'OK')){throw 'Keyboard did not restart with the filter.'}
        if((Get-Service $service).Status -ne 'Running'){throw "$service did not start."}
        'FNDRIVER_READY'
    } catch {
        Restore-Filters
        try {$null=Restart-Keyboard} catch {Write-Warning $_}
        & sc.exe delete $service | Out-Null
        throw
    }
} finally {
    Stop-Transcript
}

param([switch]$Remove,[switch]$AfterReboot)
$ErrorActionPreference='Stop'
$hub='USB\ROOT_HUB30\5&28362F5C&0&0'
$registry="HKLM:\SYSTEM\CurrentControlSet\Enum\$hub"
$backup=Join-Path $PSScriptRoot 'usbpcap\hub-before.json'
$binary=Join-Path $env:windir 'System32\drivers\USBPcap.sys'
function Restore-Filter {
    $saved=Get-Content -LiteralPath $backup -Raw | ConvertFrom-Json
    if($saved.Present){
        New-ItemProperty -LiteralPath $registry -Name UpperFilters -PropertyType MultiString -Value ([string[]]$saved.Filters) -Force | Out-Null
    } else {
        Remove-ItemProperty -LiteralPath $registry -Name UpperFilters -ErrorAction SilentlyContinue
    }
}
function Restart-Hub {
    & pnputil.exe /restart-device $hub
    if($LASTEXITCODE -eq 3010){return $false}
    if($LASTEXITCODE){throw "Hub restart returned $LASTEXITCODE."}
    return $true
}
Start-Transcript (Join-Path $PSScriptRoot 'usbpcap\install.log') -Append
try {
    if($Remove){
        Restore-Filter
        Restart-Hub
        & sc.exe delete USBPcap
        if($LASTEXITCODE){throw 'Service removal failed.'}
        return
    }
    if(Get-Service USBPcap -ErrorAction SilentlyContinue){throw 'USBPcap service already exists.'}

    $device=Get-PnpDevice -PresentOnly | Where-Object InstanceId -like 'USB\VID_05AC&PID_0320\*'
    $ancestor=$device.InstanceId
    while($ancestor -and $ancestor -ne $hub){
        $ancestor=(Get-PnpDeviceProperty -InstanceId $ancestor -KeyName DEVPKEY_Device_Parent).Data
    }
    if($ancestor -ne $hub){throw 'The keyboard is not connected to the expected USB root hub.'}
    $driver=Join-Path $PSScriptRoot 'usbpcap\variants\USBPcap.sys'
    if((Get-AuthenticodeSignature -LiteralPath $driver).Status -ne 'Valid'){throw 'USBPcap driver signature could not be verified.'}
    $key=Get-Item -LiteralPath $registry
    $filters=$key.GetValue('UpperFilters')
    @{Present=$null -ne $filters;Filters=@($filters)} | ConvertTo-Json | Set-Content -LiteralPath $backup
    if(Test-Path -LiteralPath $binary){
        if((Get-FileHash -LiteralPath $binary).Hash -ne (Get-FileHash -LiteralPath $driver).Hash){throw 'A different USBPcap.sys is already present.'}
    } else {
        Copy-Item -LiteralPath $driver -Destination $binary
    }
    try {
        & sc.exe create USBPcap type= kernel start= demand error= normal binPath= 'System32\drivers\USBPcap.sys'
        if($LASTEXITCODE){throw 'Service creation failed.'}
        New-ItemProperty -LiteralPath $registry -Name UpperFilters -PropertyType MultiString -Value ([string[]](@($filters | Where-Object {$_})+@('USBPcap'))) -Force | Out-Null
        if($AfterReboot -or -not (Restart-Hub)){'USBPCAP_REBOOT_REQUIRED';return}
        if(-not (Get-PnpDevice -InstanceId $device.InstanceId -PresentOnly | Where-Object Status -eq 'OK')){throw 'Keyboard did not reconnect.'}
        'USBPCAP_READY'
    } catch {
        Restore-Filter
        try {$null=Restart-Hub} catch { Write-Warning $_ }
        & sc.exe delete USBPcap
        throw
    }
} finally {
    Stop-Transcript
}



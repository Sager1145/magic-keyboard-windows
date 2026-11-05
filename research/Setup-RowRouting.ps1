$ErrorActionPreference='Stop'
Start-Transcript (Join-Path $PSScriptRoot 'row-routing-setup.log') -Append
try {
    $service='HKLM:\SYSTEM\CurrentControlSet\Services\KeyMagic2'
    $backup=Join-Path $PSScriptRoot 'row-routing-before.json'
    $original=[byte[]](Get-ItemProperty $service).Keymap
    if(-not (Test-Path $backup)) { [ordered]@{Keymap=@($original)} | ConvertTo-Json | Set-Content $backup }
    $pairs=[Collections.Generic.List[byte]]::new()
    for($i=0;$i+1 -lt $original.Length;$i+=2) {
        if($original[$i] -notin @(0x3c,0x3d,0x3e,0x3f)){$pairs.Add($original[$i]);$pairs.Add($original[$i+1])}
    }
    for($i=0;$i -lt 4;$i++) {$pairs.Add([byte](0x3c+$i));$pairs.Add([byte](0x6a+$i))}
    New-ItemProperty $service -Name Keymap -PropertyType Binary -Value ($pairs.ToArray()) -Force | Out-Null
    $devices=@(Get-PnpDevice -PresentOnly | Where-Object InstanceId -like 'USB\VID_05AC&PID_0320&MI_01\*')
    foreach($device in $devices) {
        & pnputil /restart-device $device.InstanceId
        if($LASTEXITCODE -ne 0 -or (Get-PnpDevice -InstanceId $device.InstanceId).Status -ne 'OK') { throw 'Keyboard restart failed.' }
    }
    'ROW_ROUTING_OK'
} catch {
    if($original){New-ItemProperty $service -Name Keymap -PropertyType Binary -Value $original -Force | Out-Null}
    'ROW_ROUTING_ERROR: '+$_.Exception.Message
    exit 1
} finally { Stop-Transcript }

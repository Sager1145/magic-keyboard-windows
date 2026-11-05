$ErrorActionPreference='Stop'
Start-Transcript (Join-Path $PSScriptRoot 'modern-drivers-finish.log') -Append
$changed=$false
try {
    $baseline=Get-Content (Join-Path $PSScriptRoot 'before-install.json') -Raw | ConvertFrom-Json
    $id=$baseline.InstanceId
    if($id -notlike 'USB\VID_05AC&PID_0320&MI_01\*') { throw 'Unexpected device.' }
    foreach($item in @(@('KeyMagic2','KeyboardMagic2'),@('KeyManager','KeyManager'))) {
        $expected=Join-Path $PSScriptRoot ('bootcamp-2022\'+$item[1]+'\'+$item[0]+'.sys')
        $actual=Join-Path $env:windir ('System32\drivers\'+$item[0]+'.sys')
        if((Get-FileHash $expected).Hash -ne (Get-FileHash $actual).Hash) { throw 'Installed driver differs from verified source.' }
    }
    if((Get-Service KeyManager).Status -ne 'Running') { throw 'KeyManager is not running.' }
    $deviceReg='HKLM:\SYSTEM\CurrentControlSet\Enum\'+$id
    $previous=@((Get-ItemProperty $deviceReg).LowerFilters | Where-Object { $_ })
    if($previous -notcontains 'KeyMagic' -or $previous -contains 'KeyMagic2') { throw 'Unexpected existing filter configuration.' }
    [ordered]@{InstanceId=$id;PreviousFilters=$previous} | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'modern-drivers-before.json')
    $next=@($previous | Where-Object { $_ -ne 'KeyMagic' })+'KeyMagic2'
    New-ItemProperty $deviceReg -Name LowerFilters -PropertyType MultiString -Value ([string[]]$next) -Force | Out-Null
    $changed=$true
    & pnputil /restart-device $id
    if($LASTEXITCODE -ne 0) { throw 'Restart failed.' }
    Start-Sleep -Seconds 2
    if((Get-PnpDevice -InstanceId $id).Status -ne 'OK') { throw 'Keyboard did not start.' }
    Get-Service KeyManager,KeyMagic2 | Format-Table Name,Status
    & pnputil /enum-devices /instanceid $id /stack
    'MODERN_DRIVERS_OK'
} catch {
    $failure=$_
    if($changed) {
        New-ItemProperty $deviceReg -Name LowerFilters -PropertyType MultiString -Value ([string[]]$previous) -Force | Out-Null
        & pnputil /restart-device $id
    }
    'MODERN_DRIVERS_ERROR: '+$failure.Exception.Message
    exit 1
} finally { Stop-Transcript }

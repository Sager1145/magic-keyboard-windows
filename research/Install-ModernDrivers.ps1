$ErrorActionPreference='Stop'
Start-Transcript (Join-Path $PSScriptRoot 'modern-drivers.log') -Append
$changed=$false
$managerCreated=$false
try {
    $baseline=Get-Content (Join-Path $PSScriptRoot 'before-install.json') -Raw | ConvertFrom-Json
    $id=$baseline.InstanceId
    if ($id -notlike 'USB\VID_05AC&PID_0320&MI_01\*') { throw 'Unexpected device.' }
    $device=Get-PnpDevice -PresentOnly -InstanceId $id
    $deviceReg='HKLM:\SYSTEM\CurrentControlSet\Enum\'+$id
    $previous=@((Get-ItemProperty $deviceReg).LowerFilters | Where-Object { $_ })
    foreach($service in @('KeyManager','KeyMagic2')) { if(Get-Service $service -ErrorAction SilentlyContinue) { throw "Service $service already exists; refusing to replace it." } }
    $signtool=Join-Path (Split-Path $PSScriptRoot) 'nullfix\sdkbt\bin\10.0.26100.0\x64\signtool.exe'
    $keyboard=Join-Path $PSScriptRoot 'bootcamp-2022\KeyboardMagic2'
    $manager=Join-Path $PSScriptRoot 'bootcamp-2022\KeyManager'
    foreach($part in @(@($keyboard,'Keymagic2'),@($manager,'KeyManager'))) {
        $dir=$part[0];$name=$part[1]
        $sig=Get-AuthenticodeSignature (Join-Path $dir ($name+'.cat'))
        if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Microsoft Windows Hardware Compatibility Publisher') { throw 'Invalid Microsoft driver catalog signature.' }
        & $signtool verify /kp /c (Join-Path $dir ($name+'.cat')) (Join-Path $dir ($name+'.sys'))
        if($LASTEXITCODE -ne 0) { throw "Kernel signature verification failed: $name" }
        & pnputil /add-driver (Join-Path $dir ($name+'.inf'))
        if($LASTEXITCODE -ne 0) { throw "Package staging failed: $name" }
        $target=Join-Path $env:windir ('System32\drivers\'+$name+'.sys')
        if(Test-Path $target) { throw "Refusing to replace existing file: $target" }
        Copy-Item (Join-Path $dir ($name+'.sys')) $target
    }
    Add-Type @'
using System;using System.Runtime.InteropServices;
public static class ModernInf {
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern IntPtr SetupOpenInfFile(string file,string cls,uint style,out uint line);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern bool SetupInstallServicesFromInfSection(IntPtr handle,string section,uint flags);
 [DllImport("setupapi.dll")] public static extern void SetupCloseInfFile(IntPtr handle);
}
'@
    [uint32]$line=0
    $handle=[ModernInf]::SetupOpenInfFile((Join-Path $keyboard 'Keymagic2.inf'),'HIDClass',2,[ref]$line)
    if($handle -eq [IntPtr](-1)) { throw 'Cannot open original Apple keyboard INF.' }
    try {
        if(-not [ModernInf]::SetupInstallServicesFromInfSection($handle,'Dri_HID_Inst.NT.Services',0)) { throw "Service setup error $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
    } finally { [ModernInf]::SetupCloseInfFile($handle) }
    $service='HKLM:\SYSTEM\CurrentControlSet\Services\KeyMagic2'
    New-ItemProperty ($service+'\KeyboardTable') -Name 'VID_05AC&PID_0320' -PropertyType Binary -Value ([byte[]]@(2,0,3,1,1)) -Force | Out-Null
    New-ItemProperty $service -Name OSXFnBehavior -PropertyType Binary -Value ([byte[]]@(0)) -Force | Out-Null
    New-Item ($service+'\Parameters\Wdf') -Force | Out-Null
    New-ItemProperty ($service+'\Parameters\Wdf') -Name KmdfLibraryVersion -PropertyType String -Value '1.15' -Force | Out-Null
    & sc.exe create KeyManager type= kernel start= auto error= normal binPath= '\SystemRoot\System32\drivers\KeyManager.sys' DisplayName= 'Apple KeyManager Event Router'
    if($LASTEXITCODE -ne 0) { throw 'Could not create KeyManager service.' }
    $managerCreated=$true
    & sc.exe start KeyManager
    if($LASTEXITCODE -ne 0) { throw 'Windows did not load the signed KeyManager driver.' }
    $next=@($previous | Where-Object { $_ -ne 'KeyMagic' })+'KeyMagic2'
    [ordered]@{InstanceId=$id;PreviousFilters=$previous;KeyMagic2Hash=(Get-FileHash (Join-Path $keyboard 'Keymagic2.sys')).Hash;KeyManagerHash=(Get-FileHash (Join-Path $manager 'KeyManager.sys')).Hash} | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'modern-drivers-before.json')
    New-ItemProperty $deviceReg -Name LowerFilters -PropertyType MultiString -Value ([string[]]$next) -Force | Out-Null
    $changed=$true
    & pnputil /restart-device $id
    if($LASTEXITCODE -ne 0) { throw 'Keyboard restart failed.' }
    Start-Sleep -Seconds 2
    if((Get-PnpDevice -InstanceId $id).Status -ne 'OK') { throw 'Keyboard failed to start.' }
    Get-Service KeyManager,KeyMagic2 | Format-Table Name,Status
    & pnputil /enum-devices /instanceid $id /stack
    'MODERN_DRIVERS_OK'
} catch {
    $failure=$_
    if($changed) {
        New-ItemProperty $deviceReg -Name LowerFilters -PropertyType MultiString -Value ([string[]]$previous) -Force | Out-Null
        & pnputil /restart-device $id
    }
    if($managerCreated) { & sc.exe stop KeyManager; & sc.exe delete KeyManager }
    'MODERN_DRIVERS_ERROR: '+$failure.Exception.Message
    exit 1
} finally { Stop-Transcript }

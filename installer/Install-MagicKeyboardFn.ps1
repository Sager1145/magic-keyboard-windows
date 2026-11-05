param([switch]$Remove,[switch]$EnableTestSigning,[switch]$NoTray)
# Installs the MagicKeyboardFn driver package and the tray app on any Windows 10/11 x64 PC.
# Run from the package folder in an administrator PowerShell.
#
# The driver is test-signed. Windows loads it only when the signing certificate
# is trusted AND test signing is on, which requires Secure Boot to be off.
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$inf=Join-Path $root 'driver\MagicKeyboardFn.inf'
$cer=Join-Path $root 'driver\MagicKeyboardFn.cer'
$hardwareId='USB\VID_05AC&PID_0320&MI_01'
$appDir=Join-Path $env:ProgramFiles 'MagicKeyboardControls'
$runKey='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'

if(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators')){
    throw 'Run this script in an administrator PowerShell.'
}
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class MagicKbSetup {
 [DllImport("newdev.dll",CharSet=CharSet.Unicode,SetLastError=true)]
 static extern bool UpdateDriverForPlugAndPlayDevices(IntPtr parent,string hardwareId,string infPath,uint flags,out bool reboot);
 // INSTALLFLAG_FORCE: use this INF even if Windows ranks the in-box HID driver higher.
 public static bool Force(string hardwareId,string inf){
  bool reboot;
  if(!UpdateDriverForPlugAndPlayDevices(IntPtr.Zero,hardwareId,inf,1,out reboot))throw new System.ComponentModel.Win32Exception();
  return reboot;
 }
}
'@

function Get-PublishedInf {
    $current=$null
    foreach($line in (& pnputil.exe /enum-drivers)){
        if($line -match ':\s+(oem\d+\.inf)\s*$'){$current=$Matches[1]}
        elseif($line -match ':\s+magickeyboardfn\.inf\s*$' -and $current){$current;$current=$null}
    }
}

if($Remove){
    foreach($published in @(Get-PublishedInf)){
        & pnputil.exe /delete-driver $published /uninstall /force | Out-Host
    }
    Remove-ItemProperty -LiteralPath $runKey -Name MagicKeyboardControls -ErrorAction SilentlyContinue
    Get-Process MagicKeyboardControls -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item -LiteralPath $appDir -Recurse -Force -ErrorAction SilentlyContinue
    # Stop trusting the test certificate the install added.
    $thumb=(New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $cer).Thumbprint
    foreach($store in 'Root','TrustedPublisher'){Remove-Item -LiteralPath "Cert:\LocalMachine\$store\$thumb" -ErrorAction SilentlyContinue}
    & pnputil.exe /scan-devices | Out-Null
    if(bcdedit /enum '{current}' | Select-String '^testsigning\s+Yes'){'Windows test signing is still on. To turn it off: bcdedit /set testsigning off, then restart.'}
    'Removed. The keyboard is back on the Windows HID driver (a restart may be needed).'
    return
}

# 1. Test signing
if(-not (bcdedit /enum '{current}' | Select-String '^testsigning\s+Yes')){
    if(-not $EnableTestSigning){
        throw 'Windows test signing is off. Rerun with -EnableTestSigning (Secure Boot must be off), then restart and run this script again.'
    }
    $secureBoot=$false
    try{$secureBoot=Confirm-SecureBootUEFI}catch{}
    if($secureBoot){throw 'Secure Boot is on, so Windows will not allow test signing. Turn Secure Boot off in the PC firmware first.'}
    & bcdedit.exe /set testsigning on | Out-Host
    if($LASTEXITCODE){throw 'bcdedit could not enable test signing.'}
    'Test signing enabled. Restart Windows, then run this script again to install the driver.'
    return
}

# 2. Trust the package certificate for driver loading and installation
foreach($store in 'Root','TrustedPublisher'){
    Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\LocalMachine\$store" | Out-Null
}

# 3. Stage the package and bind it to any connected keyboard
& pnputil.exe /add-driver $inf | Out-Host
if($LASTEXITCODE -and $LASTEXITCODE -ne 3010){throw "pnputil /add-driver returned $LASTEXITCODE."}
$connected=Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object InstanceId -like "$hardwareId\*"
$reboot=$false
if($connected){
    $reboot=[MagicKbSetup]::Force($hardwareId,$inf)
    # Interface 0 (battery report) gets the Windows HID driver from the same package.
    $aux='USB\VID_05AC&PID_0320&MI_00'
    if(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object InstanceId -like "$aux\*"){
        try{if([MagicKbSetup]::Force($aux,$inf)){$reboot=$true}}catch{"Battery interface not updated: $_"}
    }
}
# The same keyboard paired over Bluetooth.
$bluetoothId='BTHENUM\{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0320'
$paired=Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object InstanceId -like 'BTHENUM\{00001124-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&0320\*'
if($paired){
    try{if([MagicKbSetup]::Force($bluetoothId,$inf)){$reboot=$true}}catch{"Bluetooth connection not updated: $_"}
    $connected=$true
}
# A copy left by the earlier manual install is no longer used.
Remove-Item -LiteralPath (Join-Path $env:windir 'System32\drivers\MagicKeyboardFn.sys') -ErrorAction SilentlyContinue

# 4. The INF installs the tray app into the driver store and registers it to start at sign-in.
#    Remove the copy an earlier version of this script put in Program Files.
Get-Process MagicKeyboardControls -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -LiteralPath $appDir -Recurse -Force -ErrorAction SilentlyContinue

# 5. Windows keeps the previous driver loaded while any keyboard device uses it (a paired
#    Bluetooth keyboard stays present even when switched off). Restart every keyboard
#    device so the new driver loads now; if Windows refuses, a restart is required.
if($reboot){
    $restarted=$true
    $keyboards=Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'USB\VID_05AC&PID_0320&MI_0*' -or $_.InstanceId -like 'BTHENUM\{00001124-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&0320\*' }
    foreach($device in $keyboards){
        & pnputil.exe /restart-device $device.InstanceId | Out-Null
        if($LASTEXITCODE){$restarted=$false}
    }
    if($restarted -and $keyboards){$reboot=$false}
}
if($NoTray){Remove-ItemProperty -LiteralPath $runKey -Name MagicKeyboardControls -ErrorAction SilentlyContinue}
elseif($connected){
    # Open settings as the signed-in user (not elevated); it starts the tray.
    $tray=(Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).MagicKeyboardControls
    if($tray -match '^"([^"]+)"'){Start-Process explorer.exe -ArgumentList "`"$($Matches[1])`""}
}

if(-not $connected){'Driver staged. Connect the USB-C Magic Keyboard, then rerun this script to bind it.'}
elseif($reboot){'Driver installed. Restart Windows to finish: the previous version stays loaded until then.'}
else{'Driver installed and running. Default mappings are active now.'}

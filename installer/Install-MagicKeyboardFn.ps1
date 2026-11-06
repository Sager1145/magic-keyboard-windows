param([switch]$Remove,[switch]$EnableTestSigning,[switch]$NoTray,[switch]$NoPrompt)
# Installs the MagicKeyboardFn driver package and the tray app on any Windows 10/11 x64 PC.
# Run from the package folder in an administrator PowerShell.
#
# The driver is test-signed. Windows loads it only when the signing certificate
# is trusted AND test signing is on, which requires Secure Boot to be off.
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$inf=Join-Path $root 'driver\MagicKeyboardFn.inf'
# 4 ms USB polling: the same driver below the USB composite driver on the keyboard's USB device.
$usbInf=Join-Path $root 'driver\MagicKeyboardUsb.inf'
$cer=Join-Path $root 'driver\MagicKeyboardFn.cer'
# Supported keyboards: 0320 USB-C Magic Keyboard, 0267 Magic Keyboard 2 (2015).
$productIds=@('0320','0267')
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
// Removes MagicKeyboardFn from the LowerFilters of the keyboard's USB device nodes
// (USB\VID_05AC&PID_0320\<serial>, PID_0267), present or not. MagicKeyboardUsb.inf adds it
// there; uninstalling that package should take it away, and this makes sure: a filter left
// behind after the service is gone stops the keyboard's USB device from starting.
// Returns the device IDs it changed. Needs administrator rights.
public static class UsbFilters {
 const int CR_SUCCESS=0,CM_GETIDLIST_FILTER_ENUMERATOR=1,CM_LOCATE_DEVNODE_PHANTOM=1,CM_DRP_LOWERFILTERS=0x13;
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern int CM_Get_Device_ID_List_SizeW(out int length,string filter,int flags);
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern int CM_Get_Device_ID_ListW(string filter,char[] buffer,int length,int flags);
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern int CM_Locate_DevNodeW(out int devInst,string id,int flags);
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern int CM_Get_DevNode_Registry_PropertyW(int devInst,int property,out int type,byte[] buffer,ref int length,int flags);
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern int CM_Set_DevNode_Registry_PropertyW(int devInst,int property,byte[] buffer,int length,int flags);
 public static string[] Clear(){
  var changed=new System.Collections.Generic.List<string>();
  int length;
  if(CM_Get_Device_ID_List_SizeW(out length,"USB",CM_GETIDLIST_FILTER_ENUMERATOR)!=CR_SUCCESS || length<2)return changed.ToArray();
  char[] buffer=new char[length];
  if(CM_Get_Device_ID_ListW("USB",buffer,length,CM_GETIDLIST_FILTER_ENUMERATOR)!=CR_SUCCESS)return changed.ToArray();
  foreach(string id in new string(buffer).Split(new[]{'\0'},StringSplitOptions.RemoveEmptyEntries)){
   string upper=id.ToUpperInvariant();
   if(!(upper.StartsWith(@"USB\VID_05AC&PID_0320\") || upper.StartsWith(@"USB\VID_05AC&PID_0267\")))continue;
   int dev;
   if(CM_Locate_DevNodeW(out dev,id,CM_LOCATE_DEVNODE_PHANTOM)!=CR_SUCCESS)continue;
   int type,size=0;
   CM_Get_DevNode_Registry_PropertyW(dev,CM_DRP_LOWERFILTERS,out type,null,ref size,0);
   if(size<=0)continue;
   byte[] data=new byte[size];
   if(CM_Get_DevNode_Registry_PropertyW(dev,CM_DRP_LOWERFILTERS,out type,data,ref size,0)!=CR_SUCCESS)continue;
   string[] filters=System.Text.Encoding.Unicode.GetString(data,0,size).Split(new[]{'\0'},StringSplitOptions.RemoveEmptyEntries);
   string[] kept=Array.FindAll(filters,f=>!f.Equals("MagicKeyboardFn",StringComparison.OrdinalIgnoreCase));
   if(kept.Length==filters.Length)continue;
   int result;
   if(kept.Length==0)result=CM_Set_DevNode_Registry_PropertyW(dev,CM_DRP_LOWERFILTERS,null,0,0);
   else{byte[] value=System.Text.Encoding.Unicode.GetBytes(string.Join("\0",kept)+"\0\0");result=CM_Set_DevNode_Registry_PropertyW(dev,CM_DRP_LOWERFILTERS,value,value.Length,0);}
   if(result==CR_SUCCESS)changed.Add(id);
  }
  return changed.ToArray();
 }
}
'@

# Start menu entry that opens the settings window (which also restarts the tray app).
$shortcut=Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Orchard Keys.lnk'

# The installed copy of the settings app: the driver store folder holding this package's exe.
function Get-InstalledApp {
    $packaged=Join-Path $root 'driver\MagicKeyboardControls.exe'
    if(-not (Test-Path -LiteralPath $packaged)){return $null}
    $hash=(Get-FileHash -LiteralPath $packaged).Hash
    Get-ChildItem (Join-Path $env:windir 'System32\DriverStore\FileRepository') -Directory -Filter 'magickeyboardfn.inf_amd64_*' -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'MagicKeyboardControls.exe' } |
        Where-Object { (Test-Path -LiteralPath $_) -and (Get-FileHash -LiteralPath $_).Hash -eq $hash } |
        Select-Object -First 1
}

# Asks before restarting Windows (never with -NoPrompt).
function Confirm-Restart([string]$question,[string]$arguments='/r /t 5'){
    if($NoPrompt){return}
    $answer=Read-Host "$question [y/N]"
    if($answer -match '^(y|yes)$'){'Restarting...';& shutdown.exe $arguments.Split(' ')}
}

function Get-PublishedInf([string]$originalName){
    $current=$null
    foreach($line in (& pnputil.exe /enum-drivers)){
        if($line -match ':\s+(oem\d+\.inf)\s*$'){$current=$Matches[1]}
        elseif($line -match (':\s+'+[regex]::Escape($originalName)+'\s*$') -and $current){$current;$current=$null}
    }
}

if($Remove){
    # The USB polling package first, while the driver service still exists, then any
    # LowerFilters entry it left on the keyboard's USB device (a filter whose service is gone
    # stops that device from starting), then the main package.
    foreach($published in @(Get-PublishedInf 'magickeyboardusb.inf')){
        & pnputil.exe /delete-driver $published /uninstall /force | Out-Host
    }
    foreach($id in [UsbFilters]::Clear()){"Removed the driver from $id."}
    foreach($published in @(Get-PublishedInf 'magickeyboardfn.inf')){
        & pnputil.exe /delete-driver $published /uninstall /force | Out-Host
    }
    Remove-ItemProperty -LiteralPath $runKey -Name MagicKeyboardControls -ErrorAction SilentlyContinue
    Get-Process MagicKeyboardControls -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item -LiteralPath $appDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $shortcut -ErrorAction SilentlyContinue
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
    if($secureBoot){
        'Secure Boot is on, so Windows will not allow test signing. Turn Secure Boot off in the PC''s firmware (UEFI) settings, usually under Security or Boot, then run this script again with -EnableTestSigning.'
        Confirm-Restart 'Restart into the firmware settings now? Save your work first.' '/r /fw /t 5'
        return
    }
    & bcdedit.exe /set testsigning on | Out-Host
    if($LASTEXITCODE){throw 'bcdedit could not enable test signing.'}
    'Test signing enabled. Restart Windows, then run this script again (without -EnableTestSigning) to install the driver.'
    Confirm-Restart 'Restart Windows now? Save your work first.'
    return
}

# 2. Trust the package certificate for driver loading and installation
foreach($store in 'Root','TrustedPublisher'){
    Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\LocalMachine\$store" | Out-Null
}

# 3. Stage the package and bind it to any connected keyboard
& pnputil.exe /add-driver $inf | Out-Host
if($LASTEXITCODE -and $LASTEXITCODE -ne 3010){throw "pnputil /add-driver returned $LASTEXITCODE."}
$connected=$false
$reboot=$false
foreach($productId in $productIds){
    $hardwareId="USB\VID_05AC&PID_$productId&MI_01"
    if(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object InstanceId -like "$hardwareId\*"){
        if([MagicKbSetup]::Force($hardwareId,$inf)){$reboot=$true}
        $connected=$true
        # Interface 0 (battery report) gets WinUSB from the same package.
        $aux="USB\VID_05AC&PID_$productId&MI_00"
        if(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object InstanceId -like "$aux\*"){
            try{if([MagicKbSetup]::Force($aux,$inf)){$reboot=$true}}catch{"Battery interface not updated: $_"}
        }
    }
    # The same keyboard paired over Bluetooth.
    $bluetoothId="BTHENUM\{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&$productId"
    $paired=Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object InstanceId -like "BTHENUM\{00001124-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&$productId\*"
    if($paired){
        try{if([MagicKbSetup]::Force($bluetoothId,$inf)){$reboot=$true}}catch{"Bluetooth connection not updated: $_"}
        $connected=$true
    }
}
# 4 ms USB polling (optional: a failure is reported and the install goes on). This INF doesn't
# create the MagicKeyboardFn service (the main INF does, when it installs on a keyboard), so it
# is added only once the service exists: staged without it, it would claim the keyboard's USB
# device at the next plug-in, fail to start it, and the keyboard interface would never appear.
if(-not (Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Services\MagicKeyboardFn')){
    '4 ms USB polling not added yet: connect the keyboard and run this script again.'
}else{
    & pnputil.exe /add-driver $usbInf | Out-Host
    if($LASTEXITCODE -and $LASTEXITCODE -ne 3010){"4 ms USB polling not added (pnputil returned $LASTEXITCODE)."}
    else{
        foreach($productId in $productIds){
            $deviceId="USB\VID_05AC&PID_$productId"
            if(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object InstanceId -like "$deviceId\*"){
                try{if([MagicKbSetup]::Force($deviceId,$usbInf)){$reboot=$true}}catch{"4 ms USB polling not set: $_"}
            }
        }
    }
}
# A copy left by the earlier manual install is no longer used.
Remove-Item -LiteralPath (Join-Path $env:windir 'System32\drivers\MagicKeyboardFn.sys') -ErrorAction SilentlyContinue

# 4. The INF installs the tray app into the driver store and registers it to start at sign-in.
#    Remove the copy an earlier version of this script put in Program Files.
Get-Process MagicKeyboardControls -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -LiteralPath $appDir -Recurse -Force -ErrorAction SilentlyContinue
# Start menu entry "Orchard Keys", pointing at this version (driver store paths change per version).
$app=Get-InstalledApp
if($app){
    $link=(New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
    $link.TargetPath=$app
    $link.WorkingDirectory=Split-Path $app
    $link.IconLocation="$app,0"
    $link.Description='Customize the Magic Keyboard keys (also restarts the tray app)'
    $link.Save()
}

# 5. Windows keeps the previous driver loaded while any keyboard device uses it (a paired
#    Bluetooth keyboard stays present even when switched off). Restart every keyboard
#    device so the new driver loads now; if Windows refuses, a restart is required.
if($reboot){
    $restarted=$true
    $keyboards=Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $id=$_.InstanceId; $productIds | Where-Object { $id -like "USB\VID_05AC&PID_$_&MI_0*" -or $id -like "BTHENUM\{00001124-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&$_\*" } }
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

if(-not $connected){'Driver staged. Connect the Magic Keyboard (USB-C model or Magic Keyboard 2), then rerun this script to bind it.'}
elseif($reboot){
    'Driver installed. Restart Windows to finish: the previous version stays loaded until then.'
    Confirm-Restart 'Restart Windows now? Save your work first.'
}
else{'Driver installed and running. Default mappings are active now.'}

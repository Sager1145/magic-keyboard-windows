param([switch]$Rollback)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$log = Join-Path $root 'install.log'
Start-Transcript -Path $log -Append
try {
    if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script as administrator.' }
    $device = @(Get-PnpDevice -PresentOnly | Where-Object InstanceId -like 'USB\VID_05AC&PID_0320&MI_01\*')
    if ($device.Count -ne 1) { throw 'Expected exactly one connected USB 05AC:0320 keyboard interface 1.' }
    $id = $device[0].InstanceId
    $regPath = 'HKLM:\SYSTEM\CurrentControlSet\Enum\' + $id
    $backupPath = Join-Path $root 'before-install.json'
    $servicePath = 'HKLM:\SYSTEM\CurrentControlSet\Services\KeyMagic'
    function Restore-Filter {
        $saved = Get-Content $backupPath -Raw | ConvertFrom-Json
        if ($saved.InstanceId -ne $id) { throw 'Backup belongs to a different device instance.' }
        if ($saved.HadLowerFilters) {
            New-ItemProperty $regPath -Name LowerFilters -PropertyType MultiString -Value ([string[]]$saved.LowerFilters) -Force | Out-Null
        } else {
            Remove-ItemProperty $regPath -Name LowerFilters -ErrorAction SilentlyContinue
        }
        & pnputil.exe /restart-device $id
        if ($LASTEXITCODE -ne 0) { throw 'Device restart failed during rollback. Reconnect the USB keyboard.' }
        Write-Output 'Original device filters restored.'
    }
    if ($Rollback) {
        if (-not (Test-Path $backupPath)) { throw 'No saved backup exists.' }
        Restore-Filter
        Get-PnpDevice -InstanceId $id | Format-List Status,InstanceId
        exit 0
    }
    if (Test-Path $backupPath) {
        $saved=Get-Content $backupPath -Raw | ConvertFrom-Json
        $current=@((Get-ItemProperty $regPath).LowerFilters | Where-Object { $_ })
        if ($saved.InstanceId -ne $id -or ($current -join '|') -ne (@($saved.LowerFilters) -join '|')) { throw 'Existing backup does not match the current unmodified filter state. Roll back first.' }
    }
    if (Test-Path $servicePath) { throw 'KeyMagic service already exists; refusing to change an existing Apple installation.' }
    $driver = Join-Path $root 'bootcamp-reference\Keymagic.sys'
    $inf = Join-Path $root 'bootcamp-reference\Keymagic64.inf'
    $signtool = Join-Path (Split-Path $root) 'nullfix\sdkbt\bin\10.0.26100.0\x64\signtool.exe'
    $signature = Get-AuthenticodeSignature $driver
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Microsoft Windows Hardware Compatibility Publisher,') { throw 'Microsoft WHQL driver signature verification failed.' }
    $packageSignature = Get-AuthenticodeSignature (Join-Path $root 'AppleKeyboardInstaller64.exe')
    if ($packageSignature.Status -ne 'Valid' -or $packageSignature.SignerCertificate.Subject -notmatch 'O=Apple Inc\.') { throw 'Apple installer signature verification failed.' }
    if ((Get-FileHash $driver -Algorithm SHA256).Hash -ne '916856613AB63405F50F745884D8955A516FE56790C7F599BD59F1E94F9FB5D5') { throw 'Unexpected driver binary.' }
    & $signtool verify /kp $driver
    if ($LASTEXITCODE -ne 0) { throw 'Kernel signing verification failed.' }
    $original = Get-ItemProperty $regPath
    $hadFilters = $null -ne $original.PSObject.Properties['LowerFilters']
    if (-not (Test-Path $backupPath)) {
        [ordered]@{ InstanceId=$id; HadLowerFilters=$hadFilters; LowerFilters=@($original.LowerFilters | Where-Object { $_ }); DriverHash=(Get-FileHash $driver).Hash; Created=(Get-Date).ToString('o') } | ConvertTo-Json | Set-Content $backupPath
    }
    if (-not (Test-Path (Join-Path $root 'device-before.reg'))) {
        & reg.exe export ('HKLM\SYSTEM\CurrentControlSet\Enum\' + $id) (Join-Path $root 'device-before.reg') /y
        if ($LASTEXITCODE -ne 0) { throw 'Could not save device registry backup.' }
    }
    # Stage the unchanged Apple-signed package. It does not claim PID 0320.
    & pnputil.exe /add-driver $inf
    if ($LASTEXITCODE -ne 0) { throw 'Could not stage original Apple package.' }
    Copy-Item $driver "$env:windir\System32\drivers\KeyMagic.sys"
    # Use Apple's original Services section, including its original mapping table.
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class AppleInf {
 [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern IntPtr SetupOpenInfFile(string file, string cls, uint style, out uint line);
 [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern bool SetupInstallServicesFromInfSection(IntPtr inf, string section, uint flags);
 [DllImport("setupapi.dll")] public static extern void SetupCloseInfFile(IntPtr inf);
}
'@
    [uint32]$line=0
    $handle=[AppleInf]::SetupOpenInfFile($inf,'HIDClass',2,[ref]$line)
    if ($handle -eq [IntPtr](-1)) { throw "Cannot open original Apple INF: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
    try {
        if (-not [AppleInf]::SetupInstallServicesFromInfSection($handle,'Dri_HID_Inst.NT.Services',0)) { throw "Service installation failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
    } finally { [AppleInf]::SetupCloseInfFile($handle) }
    # Same report family as Apple's Magic Keyboard 0267; descriptor inspected on hardware.
    $table = Join-Path $servicePath 'KeyboardTable'
    $template = (Get-ItemProperty $table).'VID_05AC&PID_0267'
    if ($null -eq $template) { throw 'Original Magic Keyboard template missing.' }
    # UNKNOWN_LAYOUT=0 depends on an Apple feature query that the 0320 does not
    # resolve through this older driver. NEW_LAYOUT2=3 uses F7-F12 media order.
    $template[2] = 3
    New-ItemProperty $table -Name 'VID_05AC&PID_0320' -PropertyType Binary -Value ([byte[]]$template) -Force | Out-Null
    # Standard F1-F12; Fn selects hardware actions, per the requested preference.
    New-ItemProperty $servicePath -Name OSXFnBehavior -PropertyType Binary -Value ([byte[]]@(0)) -Force | Out-Null
    $wdf = New-Item (Join-Path $servicePath 'Parameters\Wdf') -Force
    New-ItemProperty $wdf.PSPath -Name KmdfLibraryVersion -PropertyType String -Value '1.11' -Force | Out-Null
    $filters=@($original.LowerFilters | Where-Object { $_ }) + 'KeyMagic'
    New-ItemProperty $regPath -Name LowerFilters -PropertyType MultiString -Value ([string[]]$filters) -Force | Out-Null
    try {
        & pnputil.exe /restart-device $id
        if ($LASTEXITCODE -ne 0) { throw 'Keyboard restart failed.' }
        Start-Sleep -Seconds 3
        $check=Get-PnpDevice -InstanceId $id
        $problem=(Get-PnpDeviceProperty -InstanceId $id -KeyName DEVPKEY_Device_ProblemCode).Data
        if ($check.Status -ne 'OK' -or $problem -ne 0) { throw "Keyboard did not start correctly: $($check.Status), problem $problem" }
        & sc.exe query KeyMagic
        if ($LASTEXITCODE -ne 0) { throw 'Cannot query KeyMagic service.' }
        Write-Output 'INSTALL_OK: keyboard started. Physical Fn/function-row testing is still required.'
    } catch {
        $failure=$_
        Restore-Filter
        throw $failure
    }
} catch {
    Write-Output ('INSTALL_ERROR: ' + $_.Exception.Message)
    exit 1
} finally {
    Stop-Transcript
}


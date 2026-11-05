$ErrorActionPreference='Stop'
$log=Join-Path $PSScriptRoot 'controls.log'
function Write-ControlLog([string]$message) { Add-Content $log ((Get-Date).ToString('o')+' '+$message) }
$mutex=[Threading.Mutex]::new($false,'Local\MagicKeyboard0320WindowsControls')
if (-not $mutex.WaitOne(0)) { $mutex.Dispose(); exit }
$events=$null
try {
    Add-Type -Path (Join-Path $PSScriptRoot 'AgentEvents.dll')
    Add-Type -Path (Join-Path $PSScriptRoot 'WindowsControls.dll')
    $events=[AgentEvents]::new()
    $PID | Set-Content (Join-Path $PSScriptRoot 'controls.pid')
    Write-ControlLog 'READY: Fn+F1/F2 Windows brightness; Fn+F10 mute; Fn+F11/F12 default Windows output volume'
    $names=@('brightness-down','brightness-up','volume-down','volume-up','mute')
    while ($true) {
        $action=$events.Wait(1000)
        if ($action -eq [Threading.WaitHandle]::WaitTimeout) { continue }
        try {
            if ($action -lt 2) {
                $current=[int](Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightness | Where-Object Active | Select-Object -First 1 -ExpandProperty CurrentBrightness)
                $next=[Math]::Clamp($current+$(if($action -eq 0){-5}else{5}),0,100)
                & (Join-Path $PSScriptRoot 'Set-WindowsBrightness.ps1') -Value $next
                Write-ControlLog ($names[$action]+' OK target='+$next)
            } elseif ($action -lt 4) {
                [WindowsControls]::AdjustVolume($(if($action -eq 2){-1}else{1}))
                Write-ControlLog ($names[$action]+' OK level='+[WindowsControls]::GetVolume())
            } else {
                [WindowsControls]::ToggleMute()
                Write-ControlLog 'mute OK'
            }
        } catch { Write-ControlLog ($names[$action]+' ERROR '+$_.Exception.Message) }
    }
} catch { Write-ControlLog ('ERROR '+$_.Exception.Message); exit 1 }
finally {
    if ($events) { $events.Dispose() }
    $mutex.ReleaseMutex();$mutex.Dispose()
}

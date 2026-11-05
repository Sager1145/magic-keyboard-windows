$pidFile=Join-Path $PSScriptRoot 'controls.pid'
if (Test-Path $pidFile) {
    $controlPid=[int](Get-Content $pidFile)
    $process=Get-CimInstance Win32_Process -Filter "ProcessId=$controlPid" -ErrorAction SilentlyContinue
    if ($process -and $process.CommandLine -like ('*'+(Join-Path $PSScriptRoot 'Start-MagicControls.ps1')+'*')) {
        Stop-Process -Id $controlPid
    }
}

param([ValidateRange(0,100)][int]$Value)
$ErrorActionPreference='Stop'
$methods=@(Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightnessMethods | Where-Object Active)
if (-not $methods.Count) { throw 'Windows has no active display-brightness control.' }
foreach($monitor in $methods) {
    Invoke-CimMethod -InputObject $monitor -MethodName WmiSetBrightness -Arguments @{Timeout=[uint32]0;Brightness=[byte]$Value} -ErrorAction Stop | Out-Null
}

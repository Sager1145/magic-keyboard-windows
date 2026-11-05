$ErrorActionPreference='Stop'
Start-Transcript (Join-Path $PSScriptRoot 'keyagent-install.log') -Append
try {
    if (Get-Service KeyAgent -ErrorAction SilentlyContinue) { throw 'KeyAgent already exists; refusing to replace it.' }
    $driver=Join-Path $PSScriptRoot 'apple-official-components\agent\KeyAgent.sys'
    if ((Get-FileHash $driver).Hash -ne 'E20CE3D421F306FCBE530B17A0969D5A2DD051099B730C48181F3341C3CFAC8A') { throw 'Unexpected KeyAgent binary.' }
    $sig=Get-AuthenticodeSignature $driver
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Apple Inc\.') { throw 'Apple driver signature is invalid.' }
    $signtool=Join-Path (Split-Path $PSScriptRoot) 'nullfix\sdkbt\bin\10.0.26100.0\x64\signtool.exe'
    & $signtool verify /kp $driver
    if ($LASTEXITCODE -ne 0) { throw 'Kernel-signing check failed.' }
    $target=Join-Path $env:windir 'System32\drivers\KeyAgent.sys'
    if (Test-Path $target) { throw 'A KeyAgent file already exists; refusing to overwrite it.' }
    Copy-Item $driver $target
    & sc.exe create KeyAgent type= kernel start= auto error= normal binPath= '\SystemRoot\System32\drivers\KeyAgent.sys' DisplayName= 'Apple KeyAgent Event Router'
    if ($LASTEXITCODE -ne 0) { throw 'Could not create KeyAgent service.' }
    & sc.exe start KeyAgent
    if ($LASTEXITCODE -ne 0) {
        & sc.exe delete KeyAgent
        Remove-Item -LiteralPath $target
        throw 'Windows refused to load KeyAgent; service and copied file removed.'
    }
    [ordered]@{ CreatedByThisProject=$true; DriverHash=(Get-FileHash $driver).Hash; Date=(Get-Date).ToString('o') } | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'keyagent-installed.json')
    'KEYAGENT_INSTALLED'
    Get-Service KeyAgent | Format-List Name,Status
} catch { 'KEYAGENT_ERROR: '+$_.Exception.Message; exit 1 }
finally { Stop-Transcript }


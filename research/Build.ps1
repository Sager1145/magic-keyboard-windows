$ErrorActionPreference='Stop'
$compiler=Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe (('/out:'+(Join-Path $PSScriptRoot 'MagicKeyboardNativeMode.exe'))) (Join-Path $PSScriptRoot 'NativeMode.cs') (Join-Path $PSScriptRoot 'NativeBrightness.cs')
if($LASTEXITCODE -ne 0) { throw 'Native-mode helper build failed.' }

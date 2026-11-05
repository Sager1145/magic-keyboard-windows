$ErrorActionPreference='Stop'
$compiler=Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$icon=Join-Path $PSScriptRoot 'gui\OrchardKeys.ico' # made by: MagicKeyboardControls.exe --write-icon gui\OrchardKeys.ico
$iconArgs=@(); if(Test-Path $icon){$iconArgs=@('/win32icon:'+$icon)}
$sources=@(Get-ChildItem (Join-Path $PSScriptRoot 'gui') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
& $compiler /nologo /target:winexe /platform:x64 /main:MagicKeyboard.Program /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll ("/lib:"+(Join-Path (Split-Path $compiler) "WPF")) /reference:UIAutomationClient.dll /reference:UIAutomationTypes.dll /reference:WindowsBase.dll @iconArgs ('/out:'+(Join-Path $PSScriptRoot 'MagicKeyboardControls.exe')) @sources
if($LASTEXITCODE -ne 0){throw 'GUI build failed.'}



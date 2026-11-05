@echo off
call D:\BuildEnv\SetupBuildEnv.cmd amd64
cd /d "%~dp0"
msbuild WinAppleKey.vcxproj /p:Configuration=Release /p:Platform=x64 /p:WindowsTargetPlatformVersion=10.0.28000.0 /p:SignMode=Off /p:SkipPackageVerification=true /nologo /v:minimal
if errorlevel 1 exit /b %errorlevel%

rem Driver package: INF + SYS + catalog, test-signed with the local WDK certificate.
rem Machines that install it need that certificate trusted and Windows test signing on.
set CERT=94A13BEE28D788FC4A7FA91C568968C7DBADDC69
set PKG=x64\Release\package
if exist %PKG% rmdir /s /q %PKG%
mkdir %PKG%
copy /y MagicKeyboardFn.inf %PKG%\ >nul
copy /y x64\Release\MagicKeyboardFn.sys %PKG%\ >nul
rem The tray app ships inside the driver package; build it first (Build-Gui.ps1).
copy /y ..\MagicKeyboardControls.exe %PKG%\ >nul
if errorlevel 1 exit /b %errorlevel%
signtool sign /q /fd sha256 /s My /sha1 %CERT% %PKG%\MagicKeyboardFn.sys
if errorlevel 1 exit /b %errorlevel%
"D:\Program Files\Windows Kits\10\bin\10.0.28000.0\x86\Inf2Cat.exe" /driver:%PKG% /os:10_X64 /uselocaltime >nul
if errorlevel 1 exit /b %errorlevel%
signtool sign /q /fd sha256 /s My /sha1 %CERT% %PKG%\MagicKeyboardFn.cat
if errorlevel 1 exit /b %errorlevel%
powershell -NoProfile -Command "Export-Certificate -Cert Cert:\CurrentUser\My\%CERT% -FilePath '%PKG%\MagicKeyboardFn.cer' | Out-Null"
exit /b %errorlevel%

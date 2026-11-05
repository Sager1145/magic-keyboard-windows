Orchard Keys: Fn and function row for the USB-C Magic Keyboard (05AC:0320)
https://github.com/Sager1145/magic-keyboard-windows

The driver is test-signed. Windows loads it only with test signing on, which needs
Secure Boot off. Installing trusts this project's test certificate on this PC.

Install, in an administrator PowerShell inside this folder:

  1. If test signing is off:  .\Install-MagicKeyboardFn.ps1 -EnableTestSigning
     Restart Windows.
  2. .\Install-MagicKeyboardFn.ps1

Or by hand: trust driver\MagicKeyboardFn.cer (Local Machine > Trusted Root
Certification Authorities and Trusted Publishers), then
  pnputil /add-driver driver\MagicKeyboardFn.inf /install

Defaults work as soon as it's installed, with or without the settings app:

  Fn + F1/F2     brightness down/up      Fn + F7/F8/F9   previous / play-pause / next
  Fn + F3        Task View               Fn + F10        mute
  Fn + F4        Search (again: close)   Fn + F11/F12    volume down/up
  Fn + F5        Voice typing (toggle)   Fn tap          switch input language
  Fn + F6        Screenshot              Hold Lock 1 s   lock Windows (time adjustable)

The settings app is installed with the driver and starts at sign-in; click its tray
icon to customize. Changes are stored in the driver.

Uninstall: .\Install-MagicKeyboardFn.ps1 -Remove

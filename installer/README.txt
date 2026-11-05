Orchard Keys: Fn, function row, modifiers and battery for the USB-C Magic Keyboard
(USB 05AC:0320 or Bluetooth), Windows 10/11 x64.
https://github.com/Sager1145/magic-keyboard-windows

SIGNING
The driver is test-signed, not signed by Microsoft. 64-bit Windows loads such a driver
only in test-signing mode, which needs Secure Boot off; trusting the certificate alone is
not enough. Installing turns test signing on (if you agree) and trusts this project's
certificate on this PC. Loading it without test signing would need Microsoft signing.

INSTALL, in an administrator PowerShell inside this folder:

  .\Install-MagicKeyboardFn.ps1 -EnableTestSigning

  - If Secure Boot is on, the script explains how to turn it off and offers to restart
    into your PC's firmware settings.
  - If test signing was off, it turns it on and offers to restart. After the restart, run
    .\Install-MagicKeyboardFn.ps1 again.
  - If test signing is already on, it installs the driver, the settings app and an
    "Orchard Keys" Start menu entry. If Windows must restart to load the new driver
    (for example after an update), it says so and offers to restart.
  - Add -NoPrompt to never be asked about restarting.

Or by hand, with test signing on: trust driver\MagicKeyboardFn.cer (Local Machine >
Trusted Root Certification Authorities and Trusted Publishers), then
  pnputil /add-driver driver\MagicKeyboardFn.inf /install
and restart Windows.

DEFAULTS (work as soon as it's installed, with or without the settings app):

  Fn + F1/F2     brightness down/up      Fn + F7/F8/F9   previous / play-pause / next
  Fn + F3        Task View               Fn + F10        mute
  Fn + F4        Search (again: close)   Fn + F11/F12    volume down/up
  Fn + F5        Voice typing (toggle)   Fn tap          switch input language
  Fn + F6        Screenshot              Hold Lock 1 s   lock Windows (time adjustable)

The settings app starts at sign-in in the system tray; click its icon to customize. If
you quit it, open "Orchard Keys" from the Start menu.

UNINSTALL: .\Install-MagicKeyboardFn.ps1 -Remove

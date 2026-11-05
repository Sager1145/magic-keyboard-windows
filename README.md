# Orchard Keys

**Orchard Keys** makes Apple's **USB-C Magic Keyboard** (USB `05AC:0320`) work like it does on a Mac,
on Windows 10/11 x64, over the **USB-C cable or Bluetooth**. It covers the function row, a
usable **Fn** key, the **Lock** key (top right), the modifier keys and the battery level. A
small kernel driver does the mapping, so everything works the moment it's installed, with or
without the settings app running.

| Key | Default |
| --- | --- |
| Fn + F1 / F2 | Brightness down / up (Windows brightness overlay) |
| Fn + F3 | Task View |
| Fn + F4 | Search: press again to close |
| Fn + F5 | Voice typing: press again to close |
| Fn + F6 | Screenshot (Win+Shift+S) |
| Fn + F7 / F8 / F9 | Previous / play-pause / next |
| Fn + F10 / F11 / F12 | Mute / volume down / up (Windows volume overlay) |
| Fn (tap) | Switch input language |
| Lock (hold 1 s, adjustable) | Lock Windows |

Every key can be remapped, including brightness and volume. Lock has separate **tap**,
**double tap** and **hold** actions, with an adjustable hold time (0.5–5 s); Fn has a tap
action. You can choose whether the row gives F1–F12 or the actions without Fn, and the
settings window shows each key or combination as you press it.

**Modifier keys:** control, option and command on either side can each act as Ctrl, Alt or
Win, for example Command as Ctrl for Mac-style shortcuts. The driver remaps them, so this
works in every app.

Available actions:
- **Media and display:** brightness, volume, mute, media keys.
- **Windows:** Task View, Search, voice typing, Do Not Disturb, Notification Center,
  Quick Settings, language switch, show desktop, screenshot, clipboard history, emoji,
  Calculator, lock screen.
- **Power:** Sleep, or the power button's own action from your power plan.
- **Custom:** any key, any key combination, typed text, open an app / website / folder,
  or a PowerShell command.

## Install

> **The driver is test-signed.** Windows only loads it with **test signing on**, and that
> requires **Secure Boot off** in your PC's firmware. Installing also adds this project's
> test certificate to the machine's trusted root and publisher stores. Only install it if
> you accept that.

Download from [Releases](https://github.com/Sager1145/magic-keyboard-windows/releases):

- **`OrchardKeysSetup.exe`**: run it, choose **Install**. If test signing is off it
  offers to turn it on; restart and run it again.
- **`OrchardKeys-<version>-manual.zip`**: unzip, then in an administrator PowerShell:

  ```powershell
  .\Install-MagicKeyboardFn.ps1 -EnableTestSigning   # only if test signing is off; then restart
  .\Install-MagicKeyboardFn.ps1
  ```

  Or install `driver\MagicKeyboardFn.inf` yourself after trusting `driver\MagicKeyboardFn.cer`
  (Local Machine → Trusted Root and Trusted Publishers):
  `pnputil /add-driver driver\MagicKeyboardFn.inf /install`.

Uninstall with **Uninstall** in Setup, or `.\Install-MagicKeyboardFn.ps1 -Remove`.

The settings app installs with the driver and starts at sign-in. Click its tray icon to
customize keys; changes are written to the driver immediately.

## How it works

The keyboard sends only plain F1–F12 plus a vendor Fn bit. Apple's own Boot Camp driver
works by adding a hidden consumer-control collection to the keyboard and rewriting Fn+F1
into the HID brightness usage, which Windows handles natively. This driver generalizes
that idea:

1. **`MagicKeyboardFn`** ([`driver/`](driver/)) reads each input report. Over USB it is a
   lower filter on the keyboard's USB interface; over Bluetooth it sits below Windows'
   HidBth driver and reads the HID interrupt channel. The keyboard sends the same report
   both ways, so one mapping engine handles both: it remaps modifiers, removes
   action-layer keys and Lock from the report and runs the configured action.
2. Actions go out through a **virtual HID device** (Windows' in-box
   [VHF](https://learn.microsoft.com/windows-hardware/drivers/hid/virtual-hid-framework--vhf-)).
   Consumer usages handle brightness, volume, media and Calculator. System-control usages
   handle the power button and sleep. Keyboard reports handle shortcuts such as Win+Tab.
   Windows treats them like any hardware key.
3. Kernel timers handle Fn tap and Lock tap / double tap / hold, so gestures need no app either.
4. A few things aren't keystrokes: commands, typed text, opening apps, toggles that need
   to know whether Search or voice typing is open, and Do Not Disturb. For these the
   driver hands the event to the **tray app** ([`gui/`](gui/)). Without the tray, Search
   and voice typing fall back to plain Win+S / Win+H, and Do Not Disturb opens
   Notification Center.

The tray app stores your profile and writes it to the driver over `\\.\MagicKeyboardFn`.
The driver keeps it in the registry, so mappings survive restarts without the app.

The battery level comes from the keyboard's report 0x90: over USB from interface 0 (bound
to WinUSB, since Windows' HID parser rejects that interface's descriptor), over Bluetooth
from its HID battery collection.

## Privacy and security

- Nothing records key presses. The driver passes the tray app only modifiers, F1–F12, Fn
  and Lock (other keys are removed), and the tray app forwards held keys only to an open
  settings window, in memory.
- Only the user at the physical console can talk to the driver: other sessions (fast user
  switching, Remote Desktop) can't read presses or change mappings, and only one program
  at a time can read events.
- The tray app runs unelevated; it refuses to start as administrator, because it runs
  commands from your per-user settings file.
- The driver is **test-signed**: installing turns on Windows test signing and trusts this
  project's certificate. Uninstall removes the certificate; it tells you how to turn test
  signing off.

## Build

Requirements: the Windows EWDK mounted at `D:` and the in-box .NET Framework 4 compiler.

```powershell
.\Build-Package.ps1   # builds the tray, driver package (test-signed) and dist\ release assets
```

`driver\Build.cmd` signs with a local WDK test certificate (thumbprint set in the script).
Use your own certificate when building.

`research/` keeps the earlier exploration: Apple driver tests, USB capture probes and the
first user-mode prototypes.

## License

MIT. See [LICENSE](LICENSE) and [NOTICE.md](NOTICE.md) for the projects this builds on.

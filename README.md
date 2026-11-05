# Orchard Keys

**Orchard Keys** makes Apple's **USB-C Magic Keyboard** behave on Windows the way it does on a
Mac: brightness, media and volume on the function row, a working **Fn** key, the **Lock** key,
Mac-style modifier keys and the battery level, over the **USB-C cable or Bluetooth**.

A small kernel driver does the mapping, so the keys work as soon as it's installed, at the
sign-in screen, in every app and without the settings app running. The settings app, in
the system tray, is for customizing.

- **Keyboard:** Apple Magic Keyboard with USB-C (USB `05AC:0320`, Bluetooth `004C:0320`).
  The Touch ID and numeric-keypad models aren't supported yet.
- **Windows:** Windows 10 or 11, 64-bit (x64).
- **Latest version:** [Releases](https://github.com/Sager1145/magic-keyboard-windows/releases).

## What you get

| Key | Default action |
| --- | --- |
| F1 / F2 | Brightness down / up, with the Windows brightness overlay |
| F3 | Task View |
| F4 | Search; press again to close it |
| F5 | Voice typing; press again to close it |
| F6 | Screenshot (Snipping Tool, Win+Shift+S) |
| F7 / F8 / F9 | Previous track / play-pause / next track |
| F10 / F11 / F12 | Mute / volume down / volume up, with the Windows volume overlay |
| fn, tapped on its own | Switch input language |
| Lock, held for 1 second | Lock Windows |
| control, option, command | Ctrl, Alt, Win |

By default you hold **Fn** for these actions and the keys give F1–F12 on their own, just
like the macOS default. One switch reverses that, so the actions come first and Fn gives
F1–F12.

Everything is customizable:

- **Any action on any key.** The actions:
  - **Media and display:** brightness, volume, mute, the media keys.
  - **Windows:** Task View, Search, voice typing, Do Not Disturb, Notification Center,
    Quick Settings, input language, show desktop, screenshot, clipboard history, emoji panel,
    Calculator, lock screen.
  - **Power:** sleep, or your power button's action.
  - **Your own:** a key, a key combination, typed text, an app, a website, a folder, or a
    PowerShell command.
- **Lock** has separate tap, double-tap and hold actions, with a hold time from 0.5 to 5 s.
- **Modifier keys:** control, option and command on each side can act as Ctrl, Alt or Win.
  For Mac-style shortcuts, set command to Ctrl, so that command+C copies.
- **Live view:** while the settings window is open, it shows the keys you hold and what
  they will do.
- **Battery:** the charge shows in the settings window, in the tray icon's tooltip and, over
  Bluetooth, in Windows' own Bluetooth device list. You also get a warning at 10%.

## Install

> **Read this first.** The driver is **test-signed**, not signed by Microsoft. Windows loads
> it only with **test signing on**, which requires **Secure Boot off** in your PC's firmware
> settings. Installing also makes this PC trust this project's test certificate. This lowers
> Windows' protection against untrusted drivers, so install it only if you're comfortable
> with that.

### With Setup (recommended)

1. Download **`OrchardKeysSetup.exe`** from
   [Releases](https://github.com/Sager1145/magic-keyboard-windows/releases).
2. Connect the keyboard with the cable or pair it over Bluetooth.
3. Run Setup and choose **Install**.
   - If test signing is off, Setup offers to turn it on. Restart Windows and run Setup
     again.
   - If Secure Boot is on, Setup stops. Turn Secure Boot off in your PC's firmware settings
     first.
4. If Setup says so, restart Windows. A paired Bluetooth keyboard keeps the previous driver
   loaded until then.

The settings app starts with Windows and lives in the system tray. Click its icon to
customize the keys.

### Manually

Download **`OrchardKeys-<version>-manual.zip`**, unzip it, and run this in an administrator
PowerShell in that folder:

```powershell
.\Install-MagicKeyboardFn.ps1 -EnableTestSigning   # only if test signing is off; then restart Windows
.\Install-MagicKeyboardFn.ps1
```

To do it without the script:
1. Trust `driver\MagicKeyboardFn.cer` in the Local Machine **Trusted Root Certification
   Authorities** and **Trusted Publishers** stores.
2. Run `pnputil /add-driver driver\MagicKeyboardFn.inf /install`.

### Updating

Install the new version over the old one, the same way. When an update can't take effect
right away, the installer says so; restart Windows to finish.

### Uninstall

Choose **Uninstall** in Setup, or run `.\Install-MagicKeyboardFn.ps1 -Remove`. Either one
removes the driver, the settings app and the trusted test certificate. Test signing stays on
until you turn it off with `bcdedit /set testsigning off` (as administrator) and restart.

## Troubleshooting

- **The keys do nothing after installing:** restart Windows. Over Bluetooth, the old driver
  stays loaded until a restart.
- **The settings window says "driver not running":** check that test signing is on (look for
  "Test Mode" at the bottom right of the desktop), then reconnect the keyboard or restart.
- **Search, voice typing and Do Not Disturb don't toggle:** these need the tray app. Without
  it, F4 and F5 still open Search and voice typing but can't close them. The app starts
  when you sign in; if you quit it, sign out and back in.
- **No battery level in Windows' Bluetooth list:** Windows shows it after the keyboard
  reports its level, which happens when it connects and when the tray app checks.

## How it works

The keyboard itself only ever sends plain F1–F12 plus one Apple-specific Fn bit. Apple's
Boot Camp driver gets brightness keys by rewriting those reports. Orchard Keys does the
same, for every key:

1. **The driver** (`MagicKeyboardFn`, in [`driver/`](driver/)) sits in the keyboard's driver
   stack and reads each key report before Windows sees it.
   - **Over USB**, it filters the keyboard's USB interface.
   - **Over Bluetooth**, it sits below Windows' Bluetooth HID driver and reads the
     keyboard's HID packets.
   - The keyboard sends the same report both ways, so one mapping engine handles both. It
     remaps the modifiers, removes the keys that trigger actions, and runs those actions.
2. **The actions** go out through a virtual keyboard, made with Windows' own
   [Virtual HID Framework](https://learn.microsoft.com/windows-hardware/drivers/hid/virtual-hid-framework--vhf-).
   Brightness, volume, media keys, power and shortcuts are ordinary keys to Windows, so they
   work everywhere, including the sign-in screen.
3. **Taps, double taps and holds** are timed in the driver, so they need no app either.
4. **The tray app** ([`gui/`](gui/)) handles what isn't a keystroke: commands, typed text,
   opening things, toggles that need to know whether Search or voice typing is open, and
   Do Not Disturb. It also saves your settings and sends them to the driver, which keeps
   them in the registry.
5. **The battery level** comes from the keyboard's own battery report (report 0x90).
   - **Over USB**, the app reads it from the keyboard's second USB interface, which runs
     on WinUSB because Windows' HID driver rejects that interface's report descriptor.
   - **Over Bluetooth**, the driver also passes it to Windows' Bluetooth battery display.

## Privacy and security

- **Nothing records your typing.** The driver tells the tray app only about the function row,
  Fn, Lock and modifier keys, and only when they change. The app shows held keys only in an
  open settings window and never writes key presses to disk.
- **Only the user at the screen can use the driver.** Users in other sessions (fast user
  switching, Remote Desktop) can't read key presses or change the mappings, and only one
  program at a time can receive events.
- **The tray app runs unelevated.** It refuses to start as administrator, because it runs
  commands from your settings file.
- **Test signing:** see the warning under [Install](#install). Uninstalling removes the
  certificate.

## Build

You need the [Windows EWDK](https://learn.microsoft.com/windows-hardware/drivers/download-the-wdk)
ISO mounted (any drive letter, or set `EWDK` to its root) and the .NET Framework 4 compiler
that comes with Windows. From the repository root:

```powershell
.\Build-Package.ps1 -Version 1.1.0   # tray app, signed driver package, release assets in dist\
```

`driver\Build.cmd` signs with a local WDK test certificate whose thumbprint is set in the
script. Use your own certificate when you build.

| Folder | Contents |
| --- | --- |
| [`driver/`](driver/) | The kernel driver, its INF and build script |
| [`gui/`](gui/) | The settings and tray app (C#, .NET Framework) |
| [`installer/`](installer/) | Setup and the PowerShell installer |
| [`research/`](research/) | Earlier exploration: driver tests, USB capture probes, first prototypes |

## Credits and license

MIT; see [LICENSE](LICENSE). The driver started from
[WinAppleKey](https://github.com/samartzidis/WinAppleKey), and the Do Not Disturb toggle uses
the method from [WinDndToggle](https://github.com/HalZheng/WinDndToggle); see
[NOTICE.md](NOTICE.md). Apple, Magic Keyboard and Boot Camp are trademarks of Apple Inc.;
this project isn't affiliated with Apple.

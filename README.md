# Orchard Keys

**Orchard Keys** makes Apple's **USB-C Magic Keyboard** behave on Windows the way it does on a
Mac: brightness, media and volume on the function row, a working **Fn** key, the **Lock** key,
Mac-style modifier keys and the battery level, over the **USB-C cable or Bluetooth**.

A small kernel driver does the mapping, so the keys work as soon as it's installed, at the
sign-in screen, in every app and without the settings app running. The settings app, in
the system tray, is for customizing.

- **Keyboard:** Apple Magic Keyboard with USB-C (USB `05AC:0320`, Bluetooth `004C:0320`)
  and Magic Keyboard 2 (2015, A1644; USB `05AC:0267`, Bluetooth `004C:0267`). On the Magic
  Keyboard 2, the **Eject** key does the Lock key's job. The Touch ID and numeric-keypad
  models aren't supported yet.
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
- **Battery:** the tray icon shows the keyboard's charge as a number over a level bar (green
  while charging, red at 20% or less), so it can't be mistaken for the PC's battery. The
  settings window shows the level and whether it's charging or plugged in, and Windows'
  Settings > Bluetooth & devices shows the level over the cable or Bluetooth. You also get a
  warning at 10%.
- **Fast USB polling:** over the cable, Windows reads the keyboard every 4 ms (250 Hz) instead of
  8 ms (125 Hz). That is as fast as the keyboard sends reports; polling faster adds nothing.
  Over Bluetooth the keyboard keeps its own default rate.

## Install

> **Read this first.** The driver is **test-signed**, not signed by Microsoft. 64-bit Windows
> loads a driver like this only in **test-signing mode**, which requires **Secure Boot off**
> in your PC's firmware settings. Trusting the certificate alone isn't enough: that only
> stops the "unknown publisher" warning. The installers turn test signing on (when you agree)
> and make this PC trust this project's certificate. This lowers Windows' protection against
> untrusted drivers, so install only if you're comfortable with that. Loading on any PC
> without test signing would need Microsoft (attestation) signing.

### With Setup (recommended)

1. Download **`OrchardKeysSetup.exe`** from
   [Releases](https://github.com/Sager1145/magic-keyboard-windows/releases).
2. Connect the keyboard with the cable or pair it over Bluetooth.
3. Run Setup and choose **Install**. Setup sorts out the rest:
   - **Secure Boot on:** Setup explains the setting and offers to restart straight into your
     PC's firmware settings, where you turn Secure Boot off. Then run Setup again.
   - **Test signing off:** Setup turns it on and offers to restart. After the restart it
     finishes the install by itself when you sign in.
   - **The previous driver still loaded** (for example after an update over Bluetooth):
     Setup offers to restart Windows.
   - Otherwise the keys work right away.

The settings app starts with Windows and lives in the system tray. Click its icon to
customize the keys. If you quit it, open **Orchard Keys** from the Start menu.

### Manually

Download **`OrchardKeys-<version>-manual.zip`**, unzip it, and run this in an administrator
PowerShell in that folder:

```powershell
.\Install-MagicKeyboardFn.ps1 -EnableTestSigning
```

It handles the same cases as Setup and offers to restart when needed (add `-NoPrompt` to
skip the questions). If it turned test signing on, run it again after the restart.

To do it without the script, with test signing on:
1. Trust `driver\MagicKeyboardFn.cer` in the Local Machine **Trusted Root Certification
   Authorities** and **Trusted Publishers** stores.
2. Run `pnputil /add-driver driver\MagicKeyboardFn.inf /install`, then restart Windows.

### Updating

Install the new version over the old one, the same way. When the update needs a restart,
the installer offers one.

### Uninstall

Choose **Uninstall** in Setup, or run `.\Install-MagicKeyboardFn.ps1 -Remove`. Either one
removes the driver, the settings app, the Start menu entry and the trusted test certificate.
Test signing stays on until you turn it off with `bcdedit /set testsigning off` (as
administrator) and restart.

## Troubleshooting

- **The keys do nothing after installing:** restart Windows if the installer asked you to.
  A paired Bluetooth keyboard keeps the old driver loaded until a restart.
- **The settings window says "driver not running":** check that test signing is on ("Test
  Mode" shows at the bottom right of the desktop). Running Setup again fixes it.
- **Search, voice typing and Do Not Disturb don't toggle:** these need the tray app. Without
  it, F4 and F5 still open Search and voice typing but can't close them. Open **Orchard
  Keys** from the Start menu to start it again.
- **No battery level in Windows' Bluetooth list:** over Bluetooth, Windows shows it after the
  keyboard reports its level on its own, which it does when it connects and when the level
  changes. The Bluetooth entry shows no level while the keyboard isn't connected over
  Bluetooth (for example while it's on the cable).

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
     on WinUSB because Windows' HID driver rejects that interface's report descriptor, and
     hands it to the driver, which writes it to the USB device for Windows' battery display.
   - **Over Bluetooth**, the driver keeps the report the keyboard sends on its own and
     writes it to Windows' battery display; the app asks the driver for it. Nothing asks the
     keyboard for the report over Bluetooth: Windows' Bluetooth HID driver can crash on that
     request (`hidbth!HidBthGetReportFlowCompletion`).
6. **Fast polling**: a second INF (`MagicKeyboardUsb.inf`) puts the same driver below
   Windows' USB composite driver, where it lowers the keyboard's interrupt endpoint interval
   from 8 ms to 4 ms in the descriptors Windows reads. Bluetooth is unchanged.

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
.\Build-Package.ps1 -Version 1.2.1   # tray app, signed driver package, release assets in dist\
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

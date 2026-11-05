# MagicKeyboardFn driver

A WDM lower filter for the USB interface `USB\VID_05AC&PID_0320&MI_01` (USB-C Magic
Keyboard) and `USB\VID_05AC&PID_0267&MI_01` (Magic Keyboard 2, 2015), and for their
Bluetooth connections, installed together with Windows' in-box `vhf` (Virtual HID Framework)
filter and `HidUsb` (or `HidBth`). Both models send the same 10-byte report. The only
difference is the top-right key in byte 9: Lock is bit 3 on the USB-C model and Eject is
bit 0 on the Magic Keyboard 2. `AddDevice` picks the right bit from the hardware ID
(`DEVICE_EXTENSION.LockMask`), so Eject works as the Lock key.

- `Driver.c`: filter plumbing, the `\\.\MagicKeyboardFn` control device, the event ring for
  the tray app, IOCTLs and VHF start/stop. The WDM skeleton started from
  [WinAppleKey](https://github.com/samartzidis/WinAppleKey) (MIT).
- `Mapping.c`: the mapping engine. For each keyboard input report (ID 1) it:
  - finds the function-row keys, Fn (byte 9 bit 1) and Lock (byte 9 bit 3, or Eject at
    bit 0 on the Magic Keyboard 2),
  - removes action-layer keys and Lock from the report in place,
  - emits the mapped action through a virtual HID device with three reports: keyboard (1),
    consumer control (2), system control (3),
  - tracks tap / double tap / hold with a 20 ms kernel timer that runs only while a press
    is in progress,
  - stores its configuration in its persistent-state registry key (`Config`). Windows keeps
    the service's Parameters key read-only to drivers. Built-in defaults apply when the
    value is absent.
- `Driver.h`: the shared interface: IOCTLs, `MAGICKB_CONFIG`, `MAGICKB_EVENT`.
  `gui/Driver.cs` and `gui/DriverCapture.cs` must match it.

`IOCTL_MAGICKB_SET_CONFIG` validates and persists a profile. `GET_CONFIG` reads it back.
`READ_EVENT` delivers physical reports (for the live display) and app actions. An app
action with a fallback shortcut taps that shortcut instead when no tray app is reading.

## Build

`Build.cmd` builds with the mounted EWDK (found on any drive, or set `EWDK`). It writes `x64\Release\package`: the INF, the
SYS, the tray app and a catalog, all test-signed with the local WDK certificate, plus the
public `.cer`. Run `..\Build-Package.ps1` to build the tray app first and produce the
release assets.

The package installs on any PC that trusts the certificate and has test signing on, which
requires Secure Boot off. Installing without test signing would need a Microsoft-signed
(attestation) package.

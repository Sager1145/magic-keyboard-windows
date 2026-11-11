# Changelog

## 1.3.0

### New

- **Remap any modifier key, fn included.** fn, left control, and option and command on each
  side can each act as Ctrl, Alt, Win, Shift, Fn, the Menu (context-menu "list") key, the
  Copilot key, or nothing. For example, swap fn and control: the key acting as Fn holds the
  function-row actions and does fn's tap action. The driver does the remapping, so it works
  in every app and at the sign-in screen.
- Menu and Copilot go out on the driver's virtual keyboard; Copilot sends Left Shift + Win +
  F23, as a Copilot key does.
- The settings window shows an **Acts as** list for each modifier key and for fn, and the
  keycaps show what each key acts as.

### Changed

- Driver profile version 3: `FnMap` takes the unused field after `LockHoldMs`, and the
  modifier map accepts the new targets. Profiles saved by earlier versions are migrated, so
  existing settings keep working. The settings app needs this driver version.

## 1.2.3

Bug fixes found in a second round of code reviews of 1.2.2, and clearer connection status
in the settings app. Key mappings are unchanged.

### Fixed

- **"Not connected" is shown in words.** With no keyboard connected, the narrow settings
  window showed only a gray icon, and the wide one could read "USB connected" in gray while
  the driver was still loaded. Both now say **Not connected** (or **Driver off** when the
  keyboard is there but the driver isn't running). The tray icon's tooltip says "keyboard
  not connected" too, the next time the tray app checks the battery.
- **No unmapped key press after a reconnect.** The note the driver keeps while Windows waits
  for a report reply (added in 1.2.2) was meant to be cleared when the keyboard's driver
  starts, but that line never ran. It also survived a second failed read after a
  disconnect. Either way, if Bluetooth reused the channel number on reconnect, the first
  key report could reach Windows unmapped (for example, F1 instead of brightness). The note
  is now cleared on start and on every failed read.
- **Ordinary key input is left alone.** The driver looked at the start of every Bluetooth
  packet to spot report replies. It now does that only while a reply is expected, as in
  1.2.1.
- **Removal waits for battery work.** On removal the driver now waits for an in-flight
  battery update before passing the removal on, so that update can't touch the device as
  Windows tears it down.

### Checked

- Reviewed by six independent code reviews.
- Installed and tested on x64 over USB and Bluetooth.

Upgrading from 1.2.2 replaces the driver. Restart Windows if the installer asks.

## 1.2.2

Bug fixes for the Bluetooth connection and the Fn and Lock keys. Nothing changes in the
settings app or in the key mappings.

### Fixed

- **Battery level no longer drops out of Settings over Bluetooth.** Windows' Bluetooth HID
  driver also uses the keyboard's control channel, for example to answer an app's request
  for a report. The driver treated every packet on that channel like key input, so:
  - a timed-out or cancelled request counted as the keyboard disconnecting, and the driver
    removed the battery level from Settings until the keyboard sent a new one;
  - a requested report could be rewritten and handled as a key press.

  The driver now notes when a report request goes out (a HID GET_REPORT) and passes its
  reply, or its failure, through untouched. Windows' own handling of that reply is exactly
  as it would be without Orchard Keys. The note is cleared when the link drops, when the
  keyboard is removed, and when the driver starts, so a reconnect can never hide a real key
  press.
- **No stray Lock or Fn action after removing the keyboard.** If the keyboard was unplugged
  or went out of range while Fn or Lock was held, the driver kept thinking the key was down.
  Its gesture timer could then restart and fire the Lock key's hold action (for example
  locking Windows) with no keyboard attached. The held-key state is now cleared on removal,
  and the timer stops once no keyboard is attached, so no timer outlives the keyboard or the
  driver.

### Checked

- Reviewed by six independent code reviews; two follow-up findings were fixed before release.
- Installed and tested on x64 over USB and Bluetooth.

Upgrading from 1.2.1 replaces the driver. Restart Windows if the installer asks.

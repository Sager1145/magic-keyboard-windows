# Changelog

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

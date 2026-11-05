# Third-party notices

## WinAppleKey

The driver's WDM filter skeleton (`driver/Driver.c`: device attach, PnP, power and
remove-lock handling) and project files started from
[WinAppleKey](https://github.com/samartzidis/WinAppleKey).

> MIT License
>
> Copyright (c) 2021 George Samartzidis
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this
> software and associated documentation files (the "Software"), to deal in the Software
> without restriction, including without limitation the rights to use, copy, modify,
> merge, publish, distribute, sublicense, and/or sell copies of the Software, and to
> permit persons to whom the Software is furnished to do so, subject to the following
> conditions:
>
> The above copyright notice and this permission notice shall be included in all copies
> or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
> INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
> PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
> HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
> CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR
> THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## WinDndToggle

The Do Not Disturb toggle in `gui/ActionRunner.cs` calls the undocumented
`QuietHoursSettings` COM service using the interface layout documented by
[WinDndToggle](https://github.com/HalZheng/WinDndToggle).

> MIT License
>
> Copyright (c) 2026 WinDndToggle contributors
>
> (Same MIT permission notice and warranty disclaimer as above.)

## Protocol references

[Linux hid-apple](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-apple.c)
was used as protocol documentation for the Apple Fn bit. No Linux code is included.
Apple's KeyMagic2 driver was studied to learn how it presents brightness and volume
as HID consumer usages; none of Apple's code or binaries are included.

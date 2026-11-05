"""Read-only inventory for the connected USB-C Magic Keyboard. No key capture."""
import json
from pathlib import Path
import hid

rows = []
for device in hid.enumerate(0x05AC, 0x0320):
    row = {k: v for k, v in device.items() if k != 'serial_number'}
    row['path'] = row['path'].decode()
    handle = hid.device()
    try:
        handle.open_path(device['path'])
        row['report_descriptor_hex'] = bytes(handle.get_report_descriptor()).hex()
    except OSError as exc:
        row['error'] = str(exc)
    finally:
        handle.close()
    rows.append(row)
output = json.dumps(rows, indent=2)
Path(__file__).with_name('hid-inventory.json').write_text(output)
print(output)

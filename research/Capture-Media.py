"""Capture only consumer-control reports (no keyboard/typing collection)."""
import hid
import time
from pathlib import Path

handles=[]
out=Path(__file__).with_name('consumer-reports.log')
def log(message):
    line=time.strftime('%H:%M:%S ')+message
    with out.open('a') as f: f.write(line+'\n')
    print(line,flush=True)
try:
    for d in hid.enumerate(0x05ac,0x0320):
        if d['usage_page']!=12: continue
        h=hid.device(); h.open_path(d['path']); h.set_nonblocking(True)
        handles.append((h,d['path'].decode().split('#')[1]))
    log('READY consumer collections: '+str(len(handles)))
    end=time.monotonic()+300
    while time.monotonic()<end:
        for h,label in handles:
            report=h.read(64)
            if report: log(label+' '+bytes(report).hex(' '))
        time.sleep(.005)
finally:
    for h,_ in handles: h.close()

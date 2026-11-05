import ctypes as c
from pathlib import Path
msi=c.WinDLL('msi')
msi.MsiOpenDatabaseW.argtypes=[c.c_wchar_p,c.c_void_p,c.POINTER(c.c_uint)]
msi.MsiDatabaseOpenViewW.argtypes=[c.c_uint,c.c_wchar_p,c.POINTER(c.c_uint)]
msi.MsiRecordGetStringW.argtypes=[c.c_uint,c.c_uint,c.c_wchar_p,c.POINTER(c.c_uint)]
db=c.c_uint()
assert msi.MsiOpenDatabaseW(str(Path(__file__).with_name('apple-official-components')/'BootCamp.msi'),None,c.byref(db))==0
for table in ['Media','File','Registry','ServiceInstall']:
    view=c.c_uint()
    if msi.MsiDatabaseOpenViewW(db,f'SELECT * FROM `{table}`',c.byref(view)): continue
    msi.MsiViewExecute(view,0)
    while True:
        rec=c.c_uint()
        if msi.MsiViewFetch(view,c.byref(rec)): break
        values=[]
        for i in range(1,msi.MsiRecordGetFieldCount(rec)+1):
            buf=c.create_unicode_buffer(4096); n=c.c_uint(len(buf))
            assert msi.MsiRecordGetStringW(rec,i,buf,c.byref(n))==0
            values.append(buf.value)
        if table=='Media' or any('keyagent' in s.lower() or 'bootcamp.exe' in s.lower() for s in values): print(table,values)
        msi.MsiCloseHandle(rec)
    msi.MsiCloseHandle(view)
msi.MsiCloseHandle(db)

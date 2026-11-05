"""Fetch only selected files from Apple's ZIP using byte ranges, without installing it."""
import io
import urllib.request
import zipfile
from pathlib import Path

URL='https://download.info.apple.com/Mac_OS_X/031-30883-20150812-ea1d13dc-4130-11e5-ba25-920911ba098f/bootcamp5.1.5722.zip'
class RemoteZip(io.RawIOBase):
    def __init__(self):
        with urllib.request.urlopen(urllib.request.Request(URL,method='HEAD'),timeout=30) as response:
            self.length=int(response.headers['Content-Length'])
        self.pos=0
    def seekable(self): return True
    def readable(self): return True
    def tell(self): return self.pos
    def seek(self,offset,whence=0):
        self.pos=offset if whence==0 else self.pos+offset if whence==1 else self.length+offset
        return self.pos
    def read(self,n=-1):
        if n<0: n=self.length-self.pos
        if not n: return b''
        end=min(self.length-1,self.pos+n-1)
        request=urllib.request.Request(URL,headers={'Range':f'bytes={self.pos}-{end}'})
        with urllib.request.urlopen(request,timeout=60) as response:
            if response.status!=206: raise RuntimeError('Server did not honor byte range')
            data=response.read()
        self.pos+=len(data)
        return data

destination=Path(__file__).with_name('apple-official-components')
destination.mkdir(exist_ok=True)
with zipfile.ZipFile(RemoteZip()) as archive:
    for entry in archive.infolist():
        if 'keyagent' in entry.filename.lower() or entry.filename.lower().endswith('/bootcamp.msi'):
            print(entry.filename,entry.file_size,flush=True)
            path=destination/Path(entry.filename).name
            path.write_bytes(archive.read(entry))
            print('Fetched',path,flush=True)

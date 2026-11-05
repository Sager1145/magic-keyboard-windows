using System;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

class Km2StateProbe {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFile(string p,uint a,uint s,IntPtr sec,uint c,uint f,IntPtr t);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle h,uint code,IntPtr input,uint isz,IntPtr output,uint osz,out uint ret,IntPtr ov);

    static readonly string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "km2-state-probe.log");
    static void Log(string v){ try { File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + v + Environment.NewLine); } catch {} }

    static void Main() {
        if (File.Exists(log)) File.Delete(log);
        // GET-style IOCTLs observed in KeyMagic2 dispatch (each returns a small value)
        uint[] codes = {
            0xB4032007, 0xB403200B, 0xB403200F, 0xB4032013, 0xB4032017, 0xB403201B,
            0xB4032043, 0xB4032047, 0xB403201C, 0xB4032048
        };
        byte[] last = new byte[codes.Length];
        for (int i = 0; i < last.Length; i++) last[i] = 0xFF;

        using (SafeFileHandle h = CreateFile(@"\\.\AppleKeyMagic2Keyboard", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
            if (h.IsInvalid) { Log("OPEN FAILED err=" + Marshal.GetLastWin32Error()); return; }
            Log("OPENED AppleKeyMagic2Keyboard");
            DateTime end = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < end) {
                for (int i = 0; i < codes.Length; i++) {
                    IntPtr outBuf = Marshal.AllocHGlobal(16);
                    try {
                        uint ret;
                        bool ok = DeviceIoControl(h, codes[i], IntPtr.Zero, 0, outBuf, 16, out ret, IntPtr.Zero);
                        if (ok) {
                            byte[] bytes = new byte[ret > 16 ? 16 : ret];
                            Marshal.Copy(outBuf, bytes, 0, bytes.Length);
                            // log on change only
                            bool changed = bytes.Length != 1 || bytes[0] != last[i];
                            if (changed) {
                                last[i] = bytes.Length == 1 ? bytes[0] : (byte)0xFF;
                                Log(string.Format("IOCTL 0x{0:X8} -> {1} (err={2})", codes[i], BitConverter.ToString(bytes), Marshal.GetLastWin32Error()));
                            }
                        } else {
                            int err = Marshal.GetLastWin32Error();
                            if (last[i] != 0xEE) { last[i] = 0xEE; Log(string.Format("IOCTL 0x{0:X8} FAILED err={1}", codes[i], err)); }
                        }
                    } finally { Marshal.FreeHGlobal(outBuf); }
                }
                Thread.Sleep(250);
            }
        }
        Log("DONE");
    }
}

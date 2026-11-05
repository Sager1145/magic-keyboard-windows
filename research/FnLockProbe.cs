using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class FnLockProbe : Form {
    [StructLayout(LayoutKind.Sequential)] struct RID { public ushort page, usage; public uint flags; public IntPtr target; }
    [DllImport("user32.dll", SetLastError=true)] static extern bool RegisterRawInputDevices(RID[] devices, uint count, uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint header);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder data, ref uint size);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public UIntPtr dwExtraInfo; }

    static readonly string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fn-lock-probe.log");
    readonly System.Windows.Forms.Timer stop = new System.Windows.Forms.Timer();
    HookProc proc;
    IntPtr hook;

    static void Log(string v) { try { File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + v + Environment.NewLine); } catch {} }

    public FnLockProbe() {
        ShowInTaskbar = false; Opacity = 0; Width = 1; Height = 1; Text = "FnLockProbe";
        Load += delegate {
            RID[] devices = {
                new RID{page=1,     usage=6, flags=0x100, target=Handle},   // keyboard collection
                new RID{page=12,    usage=1, flags=0x100, target=Handle},   // consumer collections
                new RID{page=0xFF00,usage=6, flags=0x100, target=Handle},   // vendor collection Col03
                new RID{page=0xFF00,usage=3, flags=0x100, target=Handle}    // vendor Fn usage (may not match a TLC)
            };
            bool ok = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(typeof(RID)));
            Log("RegisterRawInputDevices => " + ok + " err=" + Marshal.GetLastWin32Error());
            proc = Hook;
            hook = SetWindowsHookEx(13, proc, GetModuleHandle(null), 0);
            Log("WH_KEYBOARD_LL hook=" + (hook != IntPtr.Zero ? "ok" : "FAIL err=" + Marshal.GetLastWin32Error()));
            Log("READY: press Fn, Lock, and F1-F12. 10 minute window.");
            stop.Interval = 600000; stop.Tick += delegate { Close(); }; stop.Start();
        };
    }

    IntPtr Hook(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0) {
            KBDLLHOOKSTRUCT k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            uint msg = (uint)wParam;
            bool up = msg == 0x0101 || msg == 0x0105; // WM_KEYUP / WM_SYSKEYUP
            Log("HOOK vk=0x" + k.vkCode.ToString("X2") + " scan=0x" + k.scanCode.ToString("X2") + " flags=0x" + k.flags.ToString("X8") + (up ? " UP" : " DOWN"));
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    protected override void WndProc(ref Message message) {
        if (message.Msg == 0xFF) {
            int header = IntPtr.Size == 8 ? 24 : 16; uint size = 0;
            GetRawInputData(message.LParam, 0x10000003, IntPtr.Zero, ref size, (uint)header);
            if (size >= header + 8 && size < 4096) {
                IntPtr data = Marshal.AllocHGlobal((int)size);
                try {
                    if (GetRawInputData(message.LParam, 0x10000003, data, ref size, (uint)header) == size) {
                        int type = Marshal.ReadInt32(data);
                        IntPtr device = Marshal.ReadIntPtr(data, 8);
                        uint chars = 0; GetRawInputDeviceInfo(device, 0x20000007, null, ref chars);
                        StringBuilder name = new StringBuilder((int)chars + 1); GetRawInputDeviceInfo(device, 0x20000007, name, ref chars);
                        string dev = name.ToString();
                        if (type == 1 && size >= header + 16) {
                            int vk = (ushort)Marshal.ReadInt16(data, header + 6);
                            int scan = (ushort)Marshal.ReadInt16(data, header);
                            int flags = (ushort)Marshal.ReadInt16(data, header + 2);
                            Log("RAW-KBD vk=0x" + vk.ToString("X2") + " scan=0x" + scan.ToString("X2") + " flags=0x" + flags.ToString("X4") + " dev=" + dev);
                        } else if (type == 2 && size >= header + 8) {
                            int length = Marshal.ReadInt32(data, header);
                            int count = Marshal.ReadInt32(data, header + 4);
                            int bytes = length * count;
                            if (bytes >= 0 && bytes <= 128 && header + 8 + bytes <= size) {
                                byte[] report = new byte[bytes]; Marshal.Copy(IntPtr.Add(data, header + 8), report, 0, bytes);
                                Log("RAW-HID dev=" + dev + " len=" + length + " count=" + count + " data=" + BitConverter.ToString(report));
                            }
                        }
                    }
                } finally { Marshal.FreeHGlobal(data); }
            }
        }
        base.WndProc(ref message);
    }

    [STAThread] static void Main() {
        if (File.Exists(log)) File.Delete(log);
        Application.Run(new FnLockProbe());
        Log("STOPPED");
    }
}

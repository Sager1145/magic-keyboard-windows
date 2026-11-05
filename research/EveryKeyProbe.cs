using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class EveryKeyProbe : Form {
    [StructLayout(LayoutKind.Sequential)] struct RID { public ushort page, usage; public uint flags; public IntPtr target; }
    [DllImport("user32.dll", SetLastError=true)] static extern bool RegisterRawInputDevices(RID[] devices, uint count, uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint header);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder data, ref uint size);

    static readonly string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "every-key-probe.log");
    static readonly object gate = new object();
    static void Log(string v){ lock(gate){ try { File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + v + Environment.NewLine); } catch {} } }

    readonly System.Windows.Forms.Timer stop = new System.Windows.Forms.Timer();

    public EveryKeyProbe() {
        ShowInTaskbar = false; Opacity = 0; Width = 1; Height = 1; Text = "EveryKeyProbe";
        Load += delegate {
            RID[] devices = {
                new RID{page=1,     usage=6, flags=0x100, target=Handle},   // keyboard collection (Col01)
                new RID{page=12,    usage=1, flags=0x100, target=Handle},   // consumer collections (Col02, Col04)
                new RID{page=0xFF00,usage=6, flags=0x100, target=Handle}    // vendor collection (Col03)
            };
            bool ok = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(typeof(RID)));
            Log("RegisterRawInputDevices => " + ok + " err=" + Marshal.GetLastWin32Error());
            Log("READY: press every key (Fn, Lock, F1-F12, Fn+F1..F12, Esc, arrows, letters). 3 minute window.");
            stop.Interval = 180000; stop.Tick += delegate { Close(); }; stop.Start();
        };
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
                            bool up = (flags & 1) != 0;
                            Log("KB vk=0x" + vk.ToString("X2") + " (" + vk + ") scan=0x" + scan.ToString("X2") + (up ? " UP" : " DOWN") + " dev=" + dev);
                        } else if (type == 2 && size >= header + 8) {
                            int length = Marshal.ReadInt32(data, header);
                            int count = Marshal.ReadInt32(data, header + 4);
                            int bytes = length * count;
                            if (bytes >= 0 && bytes <= 128 && header + 8 + bytes <= size) {
                                byte[] report = new byte[bytes]; Marshal.Copy(IntPtr.Add(data, header + 8), report, 0, bytes);
                                Log("HID len=" + length + " count=" + count + " data=" + BitConverter.ToString(report) + " dev=" + dev);
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
        Application.Run(new EveryKeyProbe());
        Log("STOPPED");
    }
}

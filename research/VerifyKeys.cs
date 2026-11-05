// Device-scoped, read-only Raw Input verification. Never logs ordinary typing.
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class VerifyKeys : Form {
    [StructLayout(LayoutKind.Sequential)] struct RID { public ushort page, usage; public uint flags; public IntPtr target; }
    [DllImport("user32.dll", SetLastError=true)] static extern bool RegisterRawInputDevices(RID[] devices, uint count, uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint header);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder data, ref uint size);
    static readonly string log=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"key-verification.log");
    readonly Timer stop=new Timer();
    readonly HashSet<string> seen=new HashSet<string>();
    int incoming;
    static void Log(string value) { File.AppendAllText(log,DateTime.Now.ToString("o")+" "+value+Environment.NewLine); }
    public VerifyKeys() {
        ShowInTaskbar=false; Opacity=0; Width=1; Height=1;
        Load += delegate {
            RID[] devices={new RID{page=1,usage=6,flags=0x100,target=Handle},new RID{page=12,usage=1,flags=0x100,target=Handle}};
            if (!RegisterRawInputDevices(devices,2,(uint)Marshal.SizeOf(typeof(RID)))) throw new System.ComponentModel.Win32Exception();
            Log("READY: 05AC:0320 F-keys/media only; exits after 5 minutes.");
            stop.Interval=300000; stop.Tick += delegate { Close(); }; stop.Start();
        };
    }
    protected override void WndProc(ref Message message) {
        if(message.Msg==0xFF) {
            if(incoming++<3) Log("WM_INPUT arrived");
            uint header=(uint)(IntPtr.Size==8?24:16),size=0;
            uint result=GetRawInputData(message.LParam,0x10000003,IntPtr.Zero,ref size,header);
            if(incoming<=3) Log("Raw size="+size+" result="+result+" header="+header);
            if(size>=header+8 && size<4096) {
                IntPtr data=Marshal.AllocHGlobal((int)size);
                try {
                    if(GetRawInputData(message.LParam,0x10000003,data,ref size,header)==size) {
                        IntPtr device=Marshal.ReadIntPtr(data,8); uint chars=0;
                        GetRawInputDeviceInfo(device,0x20000007,null,ref chars);
                        StringBuilder name=new StringBuilder((int)chars+1);
                        GetRawInputDeviceInfo(device,0x20000007,name,ref chars);
                        if(seen.Add(name.ToString())) Log("Device observed: "+name+" handle="+device.ToString("X")+" infoError="+Marshal.GetLastWin32Error());
                        if(name.Length==0 || name.ToString().ToUpperInvariant().Contains("VID_05AC&PID_0320")) {
                            int offset=(int)header;
                            if(Marshal.ReadInt32(data)==2) {
                                int bytes=Marshal.ReadInt32(data,offset)*Marshal.ReadInt32(data,offset+4);
                                if(bytes>=0 && bytes<=64 && offset+8+bytes<=size) {
                                    byte[] report=new byte[bytes]; Marshal.Copy(IntPtr.Add(data,offset+8),report,0,bytes);
                                    Log("Consumer HID "+BitConverter.ToString(report));
                                }
                                base.WndProc(ref message); return;
                            }
                            if(Marshal.ReadInt32(data)!=1) { base.WndProc(ref message); return; }
                            int vk=(ushort)Marshal.ReadInt16(data,offset+6);
                            if((vk>=0x70 && vk<=0x87)||(vk>=0xAD && vk<=0xB3)||vk==0xFF) {
                                int flags=(ushort)Marshal.ReadInt16(data,offset+2);
                                Log(String.Format("VK={0:X2} scan={1:X2} {2}",vk,(ushort)Marshal.ReadInt16(data,offset),(flags&1)!=0?"UP":"DOWN"));
                            }
                        }
                    }
                } finally { Marshal.FreeHGlobal(data); }
            }
        }
        base.WndProc(ref message);
    }
    [STAThread] static void Main() { Application.Run(new VerifyKeys()); Log("STOPPED"); }
}

using System;using System.IO;using System.Threading;using System.Runtime.InteropServices;using Microsoft.Win32.SafeHandles;
class RowDiagnostic {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string p,uint a,uint s,IntPtr sec,uint c,uint f,IntPtr t);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle h,uint code,IntPtr input,uint size,IntPtr o,uint os,out uint r,IntPtr v);
 static void Main() {
  uint[] codes={0x201f,0x2043,0x204b,0x2053,0x205b,0x206f,0x2073,0x208b,0x208f,0x2093,0x2097,0x209b,0x209f,0x20b3,0x20bb,0x20c3,0x20cb,0x20d3,0x20db,0x20df,0x20e3,0x20eb,0x20f3,0x20fb,0x2103,0x210b,0x2113,0x211b,0x2123,0x212b,0x2133};
  AutoResetEvent[] events=new AutoResetEvent[codes.Length];
  string log=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"row-diagnostic.log");
  using(SafeFileHandle h=CreateFile(@"\\.\KeyManager",0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
   if(h.IsInvalid) throw new System.ComponentModel.Win32Exception();
   for(int i=0;i<codes.Length;i++) {events[i]=new AutoResetEvent(false);uint r;if(!DeviceIoControl(h,0xB4030000|codes[i],events[i].SafeWaitHandle.DangerousGetHandle(),0,IntPtr.Zero,0,out r,IntPtr.Zero)) throw new System.ComponentModel.Win32Exception();}
   File.AppendAllText(log,DateTime.Now.ToString("o")+" READY\n");
   DateTime end=DateTime.UtcNow.AddMinutes(5);
   while(DateTime.UtcNow<end) {int i=WaitHandle.WaitAny(events,1000);if(i!=WaitHandle.WaitTimeout)File.AppendAllText(log,DateTime.Now.ToString("o")+" "+codes[i].ToString("X")+"\n");}
  }
  foreach(var e in events)e.Dispose();
 }
}

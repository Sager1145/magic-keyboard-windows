using System;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public sealed class AgentEvents : IDisposable {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle device,uint code,IntPtr input,uint inputSize,IntPtr output,uint outputSize,out uint returned,IntPtr overlapped);
 SafeFileHandle device;
 AutoResetEvent[] events;
 public static readonly uint[] Registration={0xB403205B,0xB4032053,0xB403204B,0xB4032043,0xB4032073};
 public static readonly uint[] Notification={0xB403205C,0xB4032054,0xB403204C,0xB4032044,0xB4032024};
 public AgentEvents() {
  device=Open(); events=new AutoResetEvent[Registration.Length];
  try { for(int i=0;i<events.Length;i++) {
   events[i]=new AutoResetEvent(false); uint returned;
   if(!DeviceIoControl(device,Registration[i],events[i].SafeWaitHandle.DangerousGetHandle(),0,IntPtr.Zero,0,out returned,IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
  }} catch { Dispose();throw; }
 }
 static SafeFileHandle Open() {
  SafeFileHandle handle=CreateFile(@"\\.\KeyManager",0,3,IntPtr.Zero,3,0,IntPtr.Zero);
  if(handle.IsInvalid) { int error=Marshal.GetLastWin32Error();handle.Dispose();throw new System.ComponentModel.Win32Exception(error); }
  return handle;
 }
 public int Wait(int milliseconds) { return WaitHandle.WaitAny(events,milliseconds); }
 public static void Notify(int action) {
  using(SafeFileHandle handle=Open()) {uint returned;
   if(!DeviceIoControl(handle,Notification[action],IntPtr.Zero,0,IntPtr.Zero,0,out returned,IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
  }
 }
 public void Dispose() {
  // Close the file first: the driver uses FileObject to remove event registrations.
  if(device!=null) device.Dispose();
  if(events!=null) foreach(AutoResetEvent e in events) if(e!=null) e.Dispose();
 }
}

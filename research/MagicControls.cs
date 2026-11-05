// Bridge Apple's signed KeyAgent event router to Windows' default controls.
using System;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

class MagicControls {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle device,uint code,IntPtr input,uint inputSize,IntPtr output,uint outputSize,out uint returned,IntPtr overlapped);
 static readonly string log=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"controls.log");
 static void Log(string value) { lock(log) File.AppendAllText(log,DateTime.Now.ToString("o")+" "+value+Environment.NewLine); }
 // Extracted from original KeyMagic/KeyAgent binaries. Registration uses
 // METHOD_NEITHER and expects the event HANDLE itself in Type3InputBuffer.
 static readonly uint[] registrations={0xB403205B,0xB4032053,0xB403204B,0xB4032043,0xB4032073};
 static readonly uint[] notifications={0xB403205C,0xB4032054,0xB403204C,0xB4032044,0xB4032024};
 static readonly string[] names={"brightness-down","brightness-up","volume-down","volume-up","mute"};
 public static void Main(string[] args) {
  try { Run(args); } catch(Exception error) { Log("ERROR "+error.GetType().Name+": "+error.Message); Environment.ExitCode=1; }
 }
 static void Run(string[] args) {
  using(Mutex mutex=new Mutex(false,"Local\\MagicKeyboard0320WindowsControls")) {
   bool testing=args.Length>0 && args[0]=="notify-test";
   if(!testing && !mutex.WaitOne(0)) return;
   using(SafeFileHandle device=CreateFile(@"\\.\KeyAgent",0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
    if(device.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    if(args.Length>0 && args[0]=="notify-test") {
     int action=Array.IndexOf(names,args[1]);if(action<0) throw new ArgumentException("Unknown action");
     uint returned;if(!DeviceIoControl(device,notifications[action],IntPtr.Zero,0,IntPtr.Zero,0,out returned,IntPtr.Zero)) throw new System.ComponentModel.Win32Exception();
     return;
    }
    AutoResetEvent[] events=new AutoResetEvent[registrations.Length];
    for(int i=0;i<events.Length;i++) {
     events[i]=new AutoResetEvent(false); uint returned;
     if(!DeviceIoControl(device,registrations[i],events[i].SafeWaitHandle.DangerousGetHandle(),0,IntPtr.Zero,0,out returned,IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    Log("READY: Fn brightness/volume -> Windows default display and default audio endpoint");
    while(true) {
     int action=WaitHandle.WaitAny(events);
     try {
      if(action<2) WindowsControls.AdjustBrightness(action==0?-1:1);
      else if(action<4) WindowsControls.AdjustVolume(action==2?-1:1);
      else WindowsControls.ToggleMute();
      Log(names[action]+" OK");
     } catch(Exception error) { Log(names[action]+" FAILED "+error.Message); }
    }
   }
  }
 }
}

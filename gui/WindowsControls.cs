using System;
using System.Management;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

public static class WindowsControls {
 [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class DeviceEnumerator {}
 [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IEnum {
  [PreserveSig] int EnumAudioEndpoints(int flow,uint mask,out IntPtr result);
  [PreserveSig] int GetDefaultAudioEndpoint(int flow,int role,out IDevice device);
 }
 [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDevice {
  [PreserveSig] int Activate(ref Guid id,uint context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)] out object instance);
 }
 [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IVolume {
  [PreserveSig] int RegisterControlChangeNotify(IntPtr p);
  [PreserveSig] int UnregisterControlChangeNotify(IntPtr p);
  [PreserveSig] int GetChannelCount(out uint count);
  [PreserveSig] int SetMasterVolumeLevel(float level,ref Guid context);
  [PreserveSig] int SetMasterVolumeLevelScalar(float level,ref Guid context);
  [PreserveSig] int GetMasterVolumeLevel(out float level);
  [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
  [PreserveSig] int SetChannelVolumeLevel(uint channel,float level,ref Guid context);
  [PreserveSig] int SetChannelVolumeLevelScalar(uint channel,float level,ref Guid context);
  [PreserveSig] int GetChannelVolumeLevel(uint channel,out float level);
  [PreserveSig] int GetChannelVolumeLevelScalar(uint channel,out float level);
  [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute,ref Guid context);
  [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
 }
 static IVolume Volume() {
  IEnum e=(IEnum)new DeviceEnumerator(); IDevice d=null; object v=null;
  try {
   Marshal.ThrowExceptionForHR(e.GetDefaultAudioEndpoint(0,1,out d));
   Guid id=typeof(IVolume).GUID;
   Marshal.ThrowExceptionForHR(d.Activate(ref id,23,IntPtr.Zero,out v));
   return (IVolume)v;
  } finally { if(d!=null) Marshal.ReleaseComObject(d); Marshal.ReleaseComObject(e); }
 }
 public static float GetVolume() { IVolume v=Volume(); try {float level;Marshal.ThrowExceptionForHR(v.GetMasterVolumeLevelScalar(out level));return level;}finally{Marshal.ReleaseComObject(v);} }
 public static void SetVolume(float level) { IVolume v=Volume(); try {Guid context=Guid.Empty;Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(Math.Max(0,Math.Min(1,level)),ref context));}finally{Marshal.ReleaseComObject(v);} }
 public static void AdjustVolume(int direction) { SetVolume(GetVolume()+direction*.02f); }
 public static void ToggleMute() { IVolume v=Volume(); try {bool mute;Guid context=Guid.Empty;Marshal.ThrowExceptionForHR(v.GetMute(out mute));Marshal.ThrowExceptionForHR(v.SetMute(!mute,ref context));}finally{Marshal.ReleaseComObject(v);} }
 public static int GetBrightness() {
  using(ManagementObjectSearcher search=new ManagementObjectSearcher("root\\WMI","SELECT * FROM WmiMonitorBrightness WHERE Active=True"))
  using(ManagementObjectCollection monitors=search.Get()) foreach(ManagementObject monitor in monitors) using(monitor) return Convert.ToInt32(monitor["CurrentBrightness"]);
  throw new InvalidOperationException("No active laptop brightness interface.");
 }
 public static void SetBrightness(int level) {
  string script=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Set-WindowsBrightness.ps1");
  string hostFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"brightness-host.txt");
  string host=File.Exists(hostFile)?File.ReadAllText(hostFile).Trim():"pwsh.exe";
  ProcessStartInfo info=new ProcessStartInfo(host,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\" -Value "+Math.Max(0,Math.Min(100,level)));
  info.UseShellExecute=false;info.CreateNoWindow=true;info.RedirectStandardError=true;info.RedirectStandardOutput=true;
  using(Process process=Process.Start(info)) {
   string error=process.StandardError.ReadToEnd();process.StandardOutput.ReadToEnd();process.WaitForExit();
   if(process.ExitCode!=0) throw new InvalidOperationException("Windows brightness command failed: "+error);
  }
 }
 public static void AdjustBrightness(int direction) { SetBrightness(GetBrightness()+direction*5); }
 public static void Main(string[] args) {
  try { Run(args); }
  catch(Exception e) { Console.WriteLine("ERROR type="+e.GetType().FullName+" HRESULT="+e.HResult.ToString("X")); try { Console.WriteLine(e.Message); } catch {} Environment.ExitCode=1; }
 }
 static void Run(string[] args) {
  if(args.Length==0 || args[0]=="probe") {
   Console.WriteLine("Laptop brightness: "+GetBrightness()); Console.WriteLine("Master volume: "+GetVolume());
  } else if(args[0]=="roundtrip") {
   int b=GetBrightness();float v=GetVolume();SetBrightness(b);SetVolume(v);
   Console.WriteLine("Same-value setter verification: brightness="+GetBrightness()+", volume="+GetVolume());
  } else if(args[0]=="brightness-up") AdjustBrightness(1);
  else if(args[0]=="brightness-down") AdjustBrightness(-1);
  else if(args[0]=="volume-up") AdjustVolume(1);
  else if(args[0]=="volume-down") AdjustVolume(-1);
  else if(args[0]=="mute") ToggleMute();
  else throw new ArgumentException("Unknown command.");
 }
}

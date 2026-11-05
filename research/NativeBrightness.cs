using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class NativeBrightness {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle device,uint code,IntPtr input,uint inputSize,IntPtr output,uint outputSize,out uint returned,IntPtr overlapped);
 public static void Enable() {
  using(SafeFileHandle device=CreateFile(@"\\.\AppleKeyMagic2Keyboard",0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
   if(device.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   IntPtr setting=Marshal.AllocHGlobal(4);
   try {
    Marshal.WriteInt32(setting,1);uint returned;
    if(!DeviceIoControl(device,0xB4032048,setting,4,IntPtr.Zero,0,out returned,IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   } finally { Marshal.FreeHGlobal(setting); }
  }
 }
}

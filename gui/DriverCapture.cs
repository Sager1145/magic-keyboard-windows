using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MagicKeyboard {
 // Drains MAGICKB_EVENTs from the MagicKeyboardFn driver (driver\Driver.h): physical
 // reports for the live display and app actions the driver cannot perform itself.
 sealed class DriverCapture : IDisposable {
  const uint ReadEvent=0x9A4B6004; // CTL_CODE(0x9A4B,0x801,METHOD_BUFFERED,FILE_READ_ACCESS)
  const int EventSize=20;
  [DllImport("kernel32.dll",SetLastError=true)]
  static extern bool DeviceIoControl(SafeFileHandle device,uint code,IntPtr input,int inputSize,byte[] output,int outputSize,out int returned,IntPtr overlapped);
  [DllImport("kernel32.dll",SetLastError=true)]
  static extern bool CancelIoEx(SafeFileHandle file,IntPtr overlapped);
  readonly Action<PhysicalKeys> report;
  readonly Action<int,int> app;
  readonly Action<string> state;
  readonly SafeFileHandle device;
  volatile bool stopped;
  public DriverCapture(Action<PhysicalKeys> report,Action<int,int> app,Action<string> state){
   this.report=report;this.app=app;this.state=state;
   device=Driver.Open(0x80000000);
   new Thread(Run){IsBackground=true,Name="Magic Keyboard Fn driver"}.Start();
  }
  void Run(){
   byte[] buffer=new byte[EventSize];
   while(!stopped){
    int returned;
    if(!DeviceIoControl(device,ReadEvent,IntPtr.Zero,0,buffer,buffer.Length,out returned,IntPtr.Zero)){
     int error=Marshal.GetLastWin32Error();
     if(!stopped)state("Fn driver disconnected · "+new Win32Exception(error).Message);
     return;
    }
    if(returned!=EventSize)continue;
    PhysicalKeys keys;
    if(buffer[4]==1 && PhysicalKeys.ParseReport(buffer,8,out keys))report(keys);
    else if(buffer[4]==2 && buffer[5]<14 && buffer[6]<3)app(buffer[5],buffer[6]);
   }
  }
  public void Dispose(){
   stopped=true;
   if(!device.IsInvalid)CancelIoEx(device,IntPtr.Zero);
   device.Dispose();
  }
 }
}

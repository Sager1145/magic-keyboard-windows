using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MagicKeyboard {
 // Reads the Magic Keyboard's battery: input report 0x90, [0x90][status][charge 0-100].
 // USB: interface 0 runs WinUSB, so the tray app asks for it with a HID class GET_REPORT.
 // Bluetooth: the driver keeps the report the keyboard sends on its own while connected, and
 // the tray app asks the driver (never the keyboard; see Read).
 public struct BatteryReading {
  // Charging comes from the same report as Percent. PluggedIn: read over the cable, which
  // powers the keyboard, so a full battery there shows "plugged in" rather than nothing.
  public bool Valid,Charging,PluggedIn;public int Percent;
  public string State{get{return !Valid?"":Charging?"charging":PluggedIn?"plugged in":"on battery";}}
  public override string ToString(){return Valid?Percent+"% · "+State:"";}
 }
 public static class Battery {
  static readonly Guid HidClass=new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");

  [DllImport("setupapi.dll",SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(ref Guid cls,IntPtr enumerator,IntPtr parent,int flags);
  [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr info,ref Guid cls,int index,ref InterfaceData data);
  [DllImport("setupapi.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set,ref InterfaceData data,IntPtr detail,int size,out int required,IntPtr info);
  [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
  [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
  [StructLayout(LayoutKind.Sequential)] struct InterfaceData {public int Size;public Guid Class;public int Flags;public IntPtr Reserved;}

  // How the keyboard is connected: "usb", "bluetooth", or null when it isn't. USB wins
  // when both are up (the keyboard types over the cable then). Matches the USB-C Magic
  // Keyboard family: 0320, 0321 (Touch ID), 0322 (numeric keypad), and the Magic Keyboard 2
  // (2015): 0267.
  public static string Link(){
   bool bluetooth=false;
   foreach(string path in ApplePaths()){
    string lower=path.ToLowerInvariant();
    foreach(string pid in new[]{"0320","0321","0322","0267"}){
     if(lower.Contains("vid_05ac&pid_"+pid))return "usb";
     if(lower.Contains("004c_pid&"+pid))bluetooth=true;
    }
   }
   return bluetooth?"bluetooth":null;
  }

  // HID collection paths of Apple devices: USB vendor 05AC, Bluetooth vendor 004C.
  static IEnumerable<string> ApplePaths(){
   foreach(string path in InterfacePaths(HidClass)){
    string lower=path.ToLowerInvariant();
    if(lower.Contains("vid_05ac") || lower.Contains("004c_pid"))yield return path;
   }
  }
  // Paths of present devices exposing a device interface class.
  static IEnumerable<string> InterfacePaths(Guid interfaceClass){
   Guid cls=interfaceClass;
   IntPtr set=SetupDiGetClassDevs(ref cls,IntPtr.Zero,IntPtr.Zero,0x12); // DIGCF_PRESENT|DIGCF_DEVICEINTERFACE
   if(set==new IntPtr(-1))yield break;
   try{
    InterfaceData data=new InterfaceData{Size=Marshal.SizeOf(typeof(InterfaceData))};
    for(int i=0;SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref cls,i,ref data);i++){
     int size;SetupDiGetDeviceInterfaceDetail(set,ref data,IntPtr.Zero,0,out size,IntPtr.Zero);
     IntPtr detail=Marshal.AllocHGlobal(size);
     try{
      Marshal.WriteInt32(detail,IntPtr.Size==8?8:6); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
      if(!SetupDiGetDeviceInterfaceDetail(set,ref data,detail,size,out size,IntPtr.Zero))continue;
      yield return Marshal.PtrToStringUni(IntPtr.Add(detail,4));
     }finally{Marshal.FreeHGlobal(detail);}
    }
   }finally{SetupDiDestroyDeviceInfoList(set);}
  }

  // USB: interface 0 runs WinUSB (driver\MagicKeyboardFn.inf), since Windows' HID parser
  // rejects its report descriptor. Ask for input report 0x90 with a HID class request.
  static readonly Guid AuxInterface=new Guid("8C4D7C2A-4B6E-4F7B-9E3A-6A1F0D5B2E91");
  [DllImport("winusb.dll",SetLastError=true)] static extern bool WinUsb_Initialize(SafeFileHandle device,out IntPtr usb);
  [DllImport("winusb.dll")] static extern bool WinUsb_Free(IntPtr usb);
  [DllImport("winusb.dll",SetLastError=true)] static extern bool WinUsb_ControlTransfer(IntPtr usb,SetupPacket setup,byte[] buffer,int length,out int transferred,IntPtr overlapped);
  [StructLayout(LayoutKind.Sequential,Pack=1)] struct SetupPacket {public byte RequestType,Request;public ushort Value,Index,Length;}

  // GET_REPORT(Input, 0x90) on interface 0. Returns the raw report, or null.
  public static byte[] UsbReport(){
   foreach(string path in InterfacePaths(AuxInterface)){
    using(SafeFileHandle device=CreateFile(path,0xC0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero)){ // FILE_FLAG_OVERLAPPED, as WinUSB requires
     if(device.IsInvalid)continue;
     IntPtr usb;
     if(!WinUsb_Initialize(device,out usb))continue;
     try{
      byte[] report=new byte[64];int length;
      SetupPacket get=new SetupPacket{RequestType=0xA1,Request=0x01,Value=0x0190,Index=0,Length=(ushort)report.Length};
      if(!WinUsb_ControlTransfer(usb,get,report,report.Length,out length,IntPtr.Zero) || length<3)continue;
      Array.Resize(ref report,length);return report;
     }finally{WinUsb_Free(usb);}
    }
   }
   return null;
  }
  // [0x90][status: bit0 present, bit1 charging, bit2 discharging][charge 0-100]
  static BatteryReading FromUsb(byte[] report){
   BatteryReading r=new BatteryReading();
   if(report==null || report[0]!=0x90 || report[2]<1 || report[2]>100)return r;
   r.Percent=report[2];r.Charging=(report[1]&2)!=0;r.Valid=true;return r;
  }

  // The keyboard's battery: USB first (WinUSB on interface 0), then the last report the
  // keyboard itself sent over Bluetooth, which the driver keeps while the link is up. Never
  // a GET_REPORT over Bluetooth: HidBth can crash Windows on one
  // (hidbth!HidBthGetReportFlowCompletion dereferences a missing MDL).
  public static BatteryReading Read(){
   BatteryReading usb=FromUsb(UsbReport());
   if(usb.Valid){usb.PluggedIn=true;return usb;}
   BatteryReading r=new BatteryReading();
   byte[] bt=null;
   try{bt=Driver.BluetoothBattery();}catch(Exception){}
   if(bt!=null && bt[0]>=1 && bt[0]<=100){r.Percent=bt[0];r.Charging=(bt[1]&2)!=0;r.Valid=true;}
   return r;
  }
 }
}

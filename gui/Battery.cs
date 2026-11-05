using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MagicKeyboard {
 // Reads the Magic Keyboard's battery. Apple keyboards report it in input report 0x90
 // ([id][status][charge]) of a vendor collection that the host has to poll with
 // GET_REPORT. The fields are found by usage, not offset: Battery System page 0x85,
 // AbsoluteStateOfCharge 0x65 (percent) and Charging 0x44. Same method as Linux hid-apple.
 // Works over USB (vendor 05AC, interface 0) and Bluetooth (vendor 004C).
 public struct BatteryReading {
  public bool Valid,Charging;public int Percent;
  public override string ToString(){return Valid?Percent+"%"+(Charging?" · charging":""):"";}
 }
 public static class Battery {
  const ushort BatteryPage=0x85,Charge=0x65,Charging=0x44;
  const int HidpInput=0,HidpSuccess=0x00110000;
  static readonly Guid HidClass=new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");

  [DllImport("setupapi.dll",SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(ref Guid cls,IntPtr enumerator,IntPtr parent,int flags);
  [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr info,ref Guid cls,int index,ref InterfaceData data);
  [DllImport("setupapi.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set,ref InterfaceData data,IntPtr detail,int size,out int required,IntPtr info);
  [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
  [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
  [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle device,out IntPtr data);
  [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
  [DllImport("hid.dll")] static extern bool HidD_GetInputReport(SafeFileHandle device,byte[] report,int length);
  [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,byte[] caps);
  [DllImport("hid.dll")] static extern int HidP_GetSpecificValueCaps(int type,ushort page,ushort link,ushort usage,byte[] caps,ref ushort length,IntPtr data);
  [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int type,ushort page,ushort link,ushort usage,out uint value,IntPtr data,byte[] report,int length);
  [DllImport("hid.dll")] static extern int HidP_GetUsages(int type,ushort page,ushort link,ushort[] usages,ref int length,IntPtr data,byte[] report,int length2);
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

  // The keyboard's battery: USB first, then any Apple HID collection (Bluetooth).
  public static BatteryReading Read(){
   BatteryReading usb=FromUsb(UsbReport());
   if(usb.Valid)return usb;
   foreach(string path in ApplePaths()){
    BatteryReading r=ReadPath(path);
    if(r.Valid)return r;
   }
   return new BatteryReading();
  }

  static BatteryReading ReadPath(string path){
   BatteryReading r=new BatteryReading();
   SafeFileHandle device=CreateFile(path,0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
   if(device.IsInvalid){device.Dispose();device=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);}
   using(device){
    if(device.IsInvalid)return r;
    IntPtr data;
    if(!HidD_GetPreparsedData(device,out data))return r;
    try{
     ushort count=1;byte[] valueCaps=new byte[72];
     if(HidP_GetSpecificValueCaps(HidpInput,BatteryPage,0,Charge,valueCaps,ref count,data)!=HidpSuccess || count==0)return r;
     byte[] caps=new byte[64];HidP_GetCaps(data,caps);
     byte[] report=new byte[Math.Max(3,(int)BitConverter.ToUInt16(caps,4))];
     report[0]=valueCaps[2]; // report ID (0x90)
     if(!HidD_GetInputReport(device,report,report.Length))return r;
     uint percent;
     if(HidP_GetUsageValue(HidpInput,BatteryPage,0,Charge,out percent,data,report,report.Length)!=HidpSuccess)return r;
     if(percent<1 || percent>100)return r; // 0 or out of range: no reading yet
     ushort[] on=new ushort[16];int length=on.Length;
     if(HidP_GetUsages(HidpInput,BatteryPage,0,on,ref length,data,report,report.Length)==HidpSuccess)
      for(int i=0;i<length;i++)if(on[i]==Charging)r.Charging=true;
     r.Percent=(int)percent;r.Valid=true;
    }finally{HidD_FreePreparsedData(data);}
   }
   return r;
  }
 }
}

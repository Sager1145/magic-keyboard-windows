using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MagicKeyboard {
 // Talks to the MagicKeyboardFn driver (driver\Driver.h). The driver owns the
 // function row, Fn and Lock; this compiles a profile into its MAGICKB_CONFIG.
 public static class Driver {
  public const string Path=@"\\.\MagicKeyboardFn";
  const uint GetConfig=0x9A4B6008; // CTL_CODE(0x9A4B,0x802,METHOD_BUFFERED,FILE_READ_ACCESS)
  const uint SetConfig=0x9A4BA00C; // CTL_CODE(0x9A4B,0x803,METHOD_BUFFERED,FILE_WRITE_ACCESS)
  const uint SetBattery=0x9A4BA010; // CTL_CODE(0x9A4B,0x804,METHOD_BUFFERED,FILE_WRITE_ACCESS)
  const uint GetBattery=0x9A4B6014; // CTL_CODE(0x9A4B,0x805,METHOD_BUFFERED,FILE_READ_ACCESS)

  // The last battery report the keyboard sent over Bluetooth while connected, as
  // [level 1-100][status: bit1 charging], or null when there is none (not connected over
  // Bluetooth, no report yet, or an older driver).
  public static byte[] BluetoothBattery(){
   using(SafeFileHandle handle=Open(0x80000000)){
    byte[] battery=new byte[2];int returned;
    if(!DeviceIoControl(handle,GetBattery,null,0,battery,battery.Length,out returned,IntPtr.Zero) || returned!=2)return null;
    return battery;
   }
  }
  public const int ConfigSize=96;
  // HID modifier bit of each physical modifier key, in Configuration.Modifiers order.
  static readonly int[] ModifierBits={0,2,3,7,6};
  public const byte None=0,Key=1,Chord=2,Consumer=3,SystemControl=4,App=5;
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
  static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)]
  static extern bool DeviceIoControl(SafeFileHandle device,uint code,byte[] input,int inputSize,byte[] output,int outputSize,out int returned,IntPtr overlapped);

  public static SafeFileHandle Open(uint access) {
   SafeFileHandle handle=CreateFile(Path,access,3,IntPtr.Zero,3,0,IntPtr.Zero);
   if(!handle.IsInvalid)return handle;
   int error=Marshal.GetLastWin32Error();
   handle.Dispose();
   throw new Win32Exception(error,error==2?"The MagicKeyboardFn driver is not running for a connected keyboard.":new Win32Exception(error).Message);
  }

  // Passes the battery level read over USB to the driver, which shows it in Settings >
  // Bluetooth & devices on the keyboard's USB device (writing a device property needs the
  // kernel or an administrator). Over Bluetooth the driver reads the level itself.
  public static void PublishBattery(int percent){
   if(percent<1 || percent>100)return;
   using(SafeFileHandle handle=Open(0xC0000000)){
    int returned;
    if(!DeviceIoControl(handle,SetBattery,new[]{(byte)percent},1,null,0,out returned,IntPtr.Zero))
     throw new Win32Exception(Marshal.GetLastWin32Error());
   }
  }

  // Sends the profile to the driver, which applies and persists it. Returns RequireFn.
  public static bool Apply(Configuration c) {
   byte[] config=Build(c);
   using(SafeFileHandle handle=Open(0xC0000000)) {
    int returned;
    if(!DeviceIoControl(handle,SetConfig,config,config.Length,null,0,out returned,IntPtr.Zero))
     throw new Win32Exception(Marshal.GetLastWin32Error());
    byte[] active=new byte[ConfigSize];
    if(!DeviceIoControl(handle,GetConfig,null,0,active,active.Length,out returned,IntPtr.Zero) || returned!=ConfigSize)
     throw new Win32Exception(Marshal.GetLastWin32Error());
    for(int i=0;i<ConfigSize;i++)if(active[i]!=config[i])throw new InvalidOperationException("The driver did not keep the profile.");
   }
   return c.RequireFn;
  }

  public static byte[] Build(Configuration c) {
   byte[] b=new byte[ConfigSize];
   BitConverter.GetBytes(2).CopyTo(b,0);
   BitConverter.GetBytes(c.RequireFn?0:1).CopyTo(b,4);
   BitConverter.GetBytes((ushort)300).CopyTo(b,8);
   BitConverter.GetBytes((ushort)650).CopyTo(b,10);
   BitConverter.GetBytes((ushort)c.LockHoldMs).CopyTo(b,12);
   for(int i=0;i<12;i++)Put(b,16+i*4,c.Keys[i]);
   for(int g=0;g<3;g++){
    Put(b,64+g*4,c.GetBinding(12,g));
    if(g==0)Put(b,76,c.GetBinding(13,0)); // Fn itself has only a tap action
   }
   for(int i=0;i<8;i++)b[88+i]=(byte)i;
   for(int m=0;m<ModifierBits.Length;m++){
    int side=ModifierBits[m]&4;
    b[88+ModifierBits[m]]=(byte)(side+(c.Modifiers[m]=="control"?0:c.Modifiers[m]=="option"?2:3));
   }
   return b;
  }

  static void Put(byte[] b,int offset,Binding binding){
   byte type,mods;ushort usage;
   Translate(binding,out type,out mods,out usage);
   b[offset]=type;b[offset+1]=mods;BitConverter.GetBytes(usage).CopyTo(b,offset+2);
  }

  // Maps a binding to a native HID action. Anything Windows cannot receive as
  // HID input (commands, text, apps, Do Not Disturb) becomes an app action.
  public static void Translate(Binding b,out byte type,out byte mods,out ushort usage){
   type=None;mods=0;usage=0;
   switch(b.Action){
    case "none":return;
    case "brightness-down":type=Consumer;usage=0x70;return;
    case "brightness-up":type=Consumer;usage=0x6F;return;
    case "previous-track":type=Consumer;usage=0xB6;return;
    case "play-pause":type=Consumer;usage=0xCD;return;
    case "next-track":type=Consumer;usage=0xB5;return;
    case "mute":type=Consumer;usage=0xE2;return;
    case "volume-down":type=Consumer;usage=0xEA;return;
    case "volume-up":type=Consumer;usage=0xE9;return;
    case "calculator":type=Consumer;usage=0x192;return;
    case "power":type=SystemControl;usage=0x81;return;
    case "sleep":type=SystemControl;usage=0x82;return;
    case "task-view":ChordOf("Win+Tab",out type,out mods,out usage);return;
    // Toggles need the tray app (it can see whether Search is open); the driver taps the shortcut without it.
    case "search":ChordOf("Win+S",out type,out mods,out usage);type=App;return;
    case "do-not-disturb":ChordOf("Win+N",out type,out mods,out usage);type=App;return;
    case "voice-typing":ChordOf("Win+H",out type,out mods,out usage);type=App;return;

    case "notifications":ChordOf("Win+N",out type,out mods,out usage);return;
    case "quick-settings":ChordOf("Win+A",out type,out mods,out usage);return;
    case "screenshot":ChordOf("Win+Shift+S",out type,out mods,out usage);return;
    case "show-desktop":ChordOf("Win+D",out type,out mods,out usage);return;
    case "clipboard-history":ChordOf("Win+V",out type,out mods,out usage);return;
    case "emoji":ChordOf("Win+OemPeriod",out type,out mods,out usage);return;
    case "lock":ChordOf("Win+L",out type,out mods,out usage);return;
    case "input-method":ChordOf("Win+Space",out type,out mods,out usage);return;
    case "shortcut":if(TryChord(Shortcut.Parse(b.Value),out mods,out usage)){type=Chord;return;}break;
    case "key":{
     int vk=Shortcut.SingleKey(b.Value);
     if(Media.TryGetValue(vk,out usage)){type=Consumer;return;}
     if(TryChord(new[]{(ushort)vk},out mods,out usage)){type=Key;return;}
     break;
    }
   }
   type=App;mods=0;usage=0;
  }
  static void ChordOf(string text,out byte type,out byte mods,out ushort usage){
   if(!TryChord(Shortcut.Parse(text),out mods,out usage))throw new InvalidOperationException(text);
   type=Chord;
  }
  static bool TryChord(ushort[] keys,out byte mods,out ushort usage){
   mods=0;usage=0;
   foreach(ushort vk in keys){
    switch(vk){
     case 0x11:mods|=0x01;break;
     case 0x10:mods|=0x02;break;
     case 0x12:mods|=0x04;break;
     case 0x5B:mods|=0x08;break;
     default:{int hid=Usage(vk);if(hid==0)return false;usage=(ushort)hid;break;}
    }
   }
   return true;
  }
  static readonly Dictionary<int,ushort> Media=new Dictionary<int,ushort>{
   {0xAD,0xE2},{0xAE,0xEA},{0xAF,0xE9},{0xB0,0xB5},{0xB1,0xB6},{0xB2,0xB7},{0xB3,0xCD}
  };
  // Windows virtual-key code to HID keyboard usage (page 0x07); 0 when unmapped.
  public static int Usage(int vk){
   if(vk>=0x41 && vk<=0x5A)return 0x04+vk-0x41;
   if(vk>=0x31 && vk<=0x39)return 0x1E+vk-0x31;
   if(vk>=0x70 && vk<=0x7B)return 0x3A+vk-0x70;
   if(vk>=0x7C && vk<=0x87)return 0x68+vk-0x7C;
   if(vk>=0x61 && vk<=0x69)return 0x59+vk-0x61;
   switch(vk){
    case 0x30:return 0x27;case 0x0D:return 0x28;case 0x1B:return 0x29;case 0x08:return 0x2A;
    case 0x09:return 0x2B;case 0x20:return 0x2C;case 0xBD:return 0x2D;case 0xBB:return 0x2E;
    case 0xDB:return 0x2F;case 0xDD:return 0x30;case 0xDC:return 0x31;case 0xBA:return 0x33;
    case 0xDE:return 0x34;case 0xC0:return 0x35;case 0xBC:return 0x36;case 0xBE:return 0x37;
    case 0xBF:return 0x38;case 0x14:return 0x39;case 0x2C:return 0x46;case 0x91:return 0x47;
    case 0x13:return 0x48;case 0x2D:return 0x49;case 0x24:return 0x4A;case 0x21:return 0x4B;
    case 0x2E:return 0x4C;case 0x23:return 0x4D;case 0x22:return 0x4E;case 0x27:return 0x4F;
    case 0x25:return 0x50;case 0x28:return 0x51;case 0x26:return 0x52;case 0x90:return 0x53;
    case 0x6F:return 0x54;case 0x6A:return 0x55;case 0x6D:return 0x56;case 0x6B:return 0x57;
    case 0x60:return 0x62;case 0x6E:return 0x63;case 0x5D:return 0x65;
   }
   return 0;
  }
 }
}

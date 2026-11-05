using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MagicKeyboard {
 sealed class PressGesture {
  const int DoubleWindow=300;
  readonly int longThreshold;
  readonly bool fireWhileHeld;
  bool fired;
  public PressGesture(int longThreshold=650,bool fireWhileHeld=false){this.longThreshold=longThreshold;this.fireWhileHeld=fireWhileHeld;}
  bool down, cancelled, waiting, second;
  int pressed, released;
  public void Reset(){down=cancelled=waiting=second=fired=false;}
  public void Update(bool active,bool chord,int now,bool hasDouble,bool hasLong,Action<int> emit) {
   if(chord && (active || down)){cancelled=true;waiting=false;second=false;}
   if(waiting && !second && unchecked(now-released)>=DoubleWindow){waiting=false;emit(0);}
   if(active && !down){
    down=true;pressed=now;fired=false;cancelled=chord;second=waiting;
   }
   if(down && !cancelled && !fired && hasLong && (fireWhileHeld || !active) &&
      unchecked(now-pressed)>=longThreshold){
    waiting=false;second=false;fired=true;emit(2);
   }
   if(active || !down)return;
   down=false;
   if(cancelled || fired)return;
   if(second){waiting=false;second=false;emit(1);}
   else if(hasDouble){waiting=true;released=now;}
   else emit(0);
  }
 }

 struct PhysicalKeys {
  public int Row;
  public bool Fn, Lock, Other;
  public byte Mods; // HID modifier byte: Ctrl, Shift, Alt, Win (left bits 0-3, right bits 4-7)
  public string Extra; // other held keys, named, e.g. "A + Space"
  public byte[] Usages; // other held keys (HID keyboard usages)
  // Function keys held: F1-F12 bits 0-11, Lock bit 12, Fn bit 13.
  public int Held{get{return Row|(Lock?1<<12:0)|(Fn?1<<13:0);}}
  // "Ctrl + Fn + F4", in the order modifiers, Fn, other keys, F1-F12, Lock.
  public string Describe(){
   var parts=new System.Collections.Generic.List<string>();
   int m=Mods|(Mods>>4);
   if((m&1)!=0)parts.Add("Ctrl");if((m&4)!=0)parts.Add("Alt");if((m&2)!=0)parts.Add("Shift");if((m&8)!=0)parts.Add("Win");
   if(Fn)parts.Add("Fn");
   if(!String.IsNullOrEmpty(Extra))parts.Add(Extra);
   for(int i=0;i<12;i++)if((Row&(1<<i))!=0)parts.Add("F"+(i+1));
   if(Lock)parts.Add("Lock");
   return String.Join(" + ",parts);
  }
  public static string Names(byte[] usages){var n=new System.Collections.Generic.List<string>();foreach(byte u in usages)n.Add(UsageName(u));return String.Join(" + ",n);}
  static string UsageName(int u){
   if(u>=0x04 && u<=0x1D)return ((char)('A'+u-0x04)).ToString();
   if(u>=0x1E && u<=0x26)return ((char)('1'+u-0x1E)).ToString();
   switch(u){
    case 0x27:return "0";case 0x28:return "Enter";case 0x29:return "Esc";case 0x2A:return "Backspace";case 0x2B:return "Tab";
    case 0x2C:return "Space";case 0x4C:return "Delete";case 0x4F:return "Right";case 0x50:return "Left";case 0x51:return "Down";case 0x52:return "Up";
   }
   return "Key "+u.ToString("X2");
  }
  public static bool Parse(byte[] packet,int address,out PhysicalKeys keys){
   keys=new PhysicalKeys();
   if(packet.Length<37 || BitConverter.ToUInt32(packet,10)!=0 || (packet[16]&1)==0 ||
      BitConverter.ToUInt16(packet,19)!=address || (packet[21]&0x80)==0 || packet[22]!=1 ||
      BitConverter.ToUInt32(packet,23)!=10)return false;
   int start=BitConverter.ToUInt16(packet,0);
   return start>=27 && ParseReport(packet,start,out keys);
  }
  // Decodes one 10-byte keyboard input report (ID 1), whether it came from USBPcap or the Fn driver.
  public static bool ParseReport(byte[] packet,int start,out PhysicalKeys keys){
   keys=new PhysicalKeys();
   if(start<0 || start+10>packet.Length || packet[start]!=1)return false;
   keys.Mods=packet[start+1];
   keys.Other=packet[start+1]!=0 || (packet[start+9]&5)!=0;
   var extra=new System.Collections.Generic.List<byte>();
   for(int i=3;i<9;i++){
    int usage=packet[start+i];
    if(usage>=0x3A && usage<=0x45)keys.Row|=1<<(usage-0x3A);
    else if(usage!=0){keys.Other=true;extra.Add((byte)usage);}
   }
   keys.Usages=extra.ToArray();keys.Extra=Names(keys.Usages);
   keys.Fn=(packet[start+9]&2)!=0;
   keys.Lock=(packet[start+9]&8)!=0;
   return true;
  }
 }
}

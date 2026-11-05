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
   keys.Other=packet[start+1]!=0 || (packet[start+9]&5)!=0;
   for(int i=3;i<9;i++){
    int usage=packet[start+i];
    if(usage>=0x3A && usage<=0x45)keys.Row|=1<<(usage-0x3A);
    else if(usage!=0)keys.Other=true;
   }
   keys.Fn=(packet[start+9]&2)!=0;
   keys.Lock=(packet[start+9]&8)!=0;
   return true;
  }
 }
}

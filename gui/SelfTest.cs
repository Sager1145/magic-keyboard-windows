using System;using System.IO;using System.Linq;using System.Web.Script.Serialization;
namespace MagicKeyboard {
 static class SelfTest {
  public static void RunDriver() {
   string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"driver-mode-test.txt");
   try {
    Configuration settings=Configuration.Load();
    try {
     Configuration test=Configuration.Create();
     if(!Driver.Apply(test))throw new Exception("F-keys-first mode failed.");
     test.RequireFn=false;
     if(Driver.Apply(test))throw new Exception("Actions-first mode failed.");
    } finally {
     Driver.Apply(settings);
    }
    File.WriteAllText(report,"PASS: MagicKeyboardFn accepted, persisted and returned both function-row modes. Saved profile restored.");
   } catch(Exception e) {
    File.WriteAllText(report,"FAIL: "+e);
    Environment.ExitCode=1;
   }
  }
  static void TestPhysicalInput(){
   byte[] packet=new byte[37];
   packet[0]=27;packet[16]=1;packet[19]=4;packet[21]=0x81;packet[22]=1;packet[23]=10;
   packet[27]=1;packet[30]=0x3C;packet[31]=0x45;packet[36]=10;
   PhysicalKeys keys;
   if(!PhysicalKeys.Parse(packet,4,out keys)||!keys.Fn||!keys.Lock||keys.Other||keys.Row!=((1<<2)|(1<<11)))
    throw new Exception("Physical key bits did not decode.");
   if(PhysicalKeys.Parse(packet,5,out keys))throw new Exception("Another USB address was accepted.");
   packet[32]=4;
   if(!PhysicalKeys.Parse(packet,4,out keys)||!keys.Other)throw new Exception("Fn chord cancellation flag missing.");
   byte[] driverRead=new byte[16];
   driverRead[0]=7;driverRead[4]=1;driverRead[7]=0x3A;driverRead[13]=2;
   if(!PhysicalKeys.ParseReport(driverRead,4,out keys)||!keys.Fn||keys.Lock||keys.Other||keys.Row!=1)
    throw new Exception("Fn driver report did not decode like the USB packet.");
   driverRead[4]=2;
   if(PhysicalKeys.ParseReport(driverRead,4,out keys))throw new Exception("Non-keyboard driver report was accepted.");
   var gesture=new PressGesture();
   var events=new System.Collections.Generic.List<int>();
   Action<bool,bool,int> send=(down,chord,time)=>gesture.Update(down,chord,time,true,true,events.Add);
   send(true,false,0);send(false,false,20);send(false,false,320);
   send(true,false,400);send(false,false,420);send(true,false,480);send(false,false,500);
   send(true,false,600);send(false,false,1300);
   send(true,false,1400);send(true,true,2200);send(false,false,2250);send(false,false,2600);
   send(true,false,2700);send(false,false,2720);send(false,true,2750);send(false,false,3020);
   if(!events.SequenceEqual(new[]{0,1,2,0}))throw new Exception("Unexpected physical key gestures.");
   gesture=new PressGesture(1000,true);events.Clear();
   send=(down,chord,time)=>gesture.Update(down,chord,time,false,true,events.Add);
   send(true,false,0);send(false,false,30);
   send(true,false,100);send(true,false,1099);
   if(!events.SequenceEqual(new[]{0}))throw new Exception("Lock hold fired before one second.");
   send(true,false,1100);send(true,false,1300);send(false,false,1400);
   if(!events.SequenceEqual(new[]{0,2}))throw new Exception("Lock hold did not fire exactly once.");
   Configuration oldProfile=Configuration.Create();
   oldProfile.Version=1;oldProfile.Keys[12]=new Binding("lock");
   oldProfile.Gestures=null;oldProfile.Validate();
   if(oldProfile.Keys[12].Action!="none" || oldProfile.GetBinding(12,2).Action!="lock")throw new Exception("Old Lock defaults did not migrate.");
   oldProfile.SetBinding(12,2,new Binding("sleep"));
   if(oldProfile.Clone().GetBinding(12,2).Action!="sleep")throw new Exception("Gesture profile round trip failed.");
  }
  // Mirrors DefaultConfig() in driver\Mapping.c: the driver must behave the same with or without the app.
  static void TestDriverProfile(){
   byte[] expected=new byte[Driver.ConfigSize];
   expected[0]=3;expected[14]=8; // version 3; FnMap = MAGICKB_MAP_FN
   expected[8]=0x2C;expected[9]=1;expected[10]=0x8A;expected[11]=2;expected[12]=0xE8;expected[13]=3;
   ushort[] consumer={0x70,0x6F,0,0,0,0,0xB6,0xCD,0xB5,0xE2,0xEA,0xE9};
   byte[] chord={0,0,0x2B,0x16,0x0B,0x16};
   for(int i=0;i<12;i++){
    int o=16+i*4;
    if(consumer[i]!=0){expected[o]=3;expected[o+2]=(byte)consumer[i];expected[o+3]=(byte)(consumer[i]>>8);}
    else{expected[o]=2;expected[o+1]=8;expected[o+2]=chord[i];}
   }
   expected[16+3*4]=5;expected[16+4*4]=5; // Search, voice typing: tray app, Win+S / Win+H fallback
   expected[16+5*4+1]=0x0A; // Screenshot: Win+Shift+S
   expected[64+8]=2;expected[64+9]=8;expected[64+10]=0x0F; // Lock long: Win+L
   expected[76]=2;expected[77]=8;expected[78]=0x2C; // Fn single: Win+Space
   for(int i=0;i<8;i++)expected[88+i]=(byte)i; // modifiers as labeled
   byte[] actual=Driver.Build(Configuration.Create());
   for(int i=0;i<expected.Length;i++)if(actual[i]!=expected[i])throw new Exception("Default profile differs from the driver at byte "+i+".");
   Configuration mac=Configuration.Create();mac.Modifiers[0]="command";mac.Modifiers[2]="control";mac.Modifiers[3]="control";
   byte[] swapped=Driver.Build(mac);
   if(swapped[88]!=3||swapped[91]!=0||swapped[95]!=4||swapped[94]!=6)throw new Exception("Modifier remap did not build.");
   // Swap fn and control; right option as Menu, right command as Copilot, left option off.
   Configuration any=Configuration.Create();
   any.Modifiers[0]="fn";any.Modifiers[Configuration.FnModifier]="control";any.Modifiers[4]="menu";any.Modifiers[3]="copilot";any.Modifiers[1]="none";
   any.Validate();
   byte[] remapped=Driver.Build(any);
   if(remapped[88]!=8||remapped[14]!=0||remapped[94]!=9||remapped[95]!=10||remapped[90]!=11)throw new Exception("Fn / Menu / Copilot / none remap did not build.");
   any.Modifiers[2]="shift";if(Driver.Build(any)[91]!=1)throw new Exception("Shift target did not build.");
   // A profile saved before fn could be remapped has five modifier entries; fn is added as Fn.
   Configuration old=Configuration.Create();old.Modifiers=new[]{"command","option","control","command","option"};old.Validate();
   if(old.Modifiers.Length!=6||old.Modifiers[Configuration.FnModifier]!="fn"||old.Modifiers[0]!="command")throw new Exception("Five-entry modifier profile did not migrate.");
   byte type,mods;ushort usage;
   Driver.Translate(new Binding("shortcut","Ctrl+Shift+F5"),out type,out mods,out usage);
   if(type!=Driver.Chord||mods!=3||usage!=0x3E)throw new Exception("Shortcut did not translate to HID.");
   Driver.Translate(new Binding("key","VolumeUp"),out type,out mods,out usage);
   if(type!=Driver.Consumer||usage!=0xE9)throw new Exception("Media key did not translate to a consumer usage.");
   Driver.Translate(new Binding("sleep"),out type,out mods,out usage);
   if(type!=Driver.SystemControl||usage!=0x82)throw new Exception("Sleep did not translate to system control.");
   Driver.Translate(new Binding("powershell","Get-Date"),out type,out mods,out usage);
   if(type!=Driver.App)throw new Exception("PowerShell should be an app action.");
   Configuration actionsFirst=Configuration.Create();actionsFirst.RequireFn=false;
   if(Driver.Build(actionsFirst)[4]!=1)throw new Exception("Actions-first flag missing.");
  }
  public static void Run(){
   string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gui-self-test.txt");
   try{
    TestPhysicalInput();
    Configuration c=Configuration.Create();c.Validate();if(c.Keys.Length!=14)throw new Exception("Missing bindings.");
    if(c.Keys[13].Action!="input-method")throw new Exception("Fn default should switch input method.");
    if(Shortcut.SingleKey("F5")!=0x74)throw new Exception("Single-key parse failed.");
    ushort[] chord=Shortcut.Parse("Win+Ctrl+Shift+S");if(!chord.SequenceEqual(new ushort[]{0x5B,0x11,0x10,0x53}))throw new Exception("Modifier parsing failed.");
    bool invalid=false;try{Shortcut.Parse("Ctrl+Alt");}catch(ArgumentException){invalid=true;}if(!invalid)throw new Exception("Incomplete shortcut accepted.");
    invalid=false;try{Shortcut.Parse("Ctrl+Ctrl+K");}catch(ArgumentException){invalid=true;}if(!invalid)throw new Exception("Duplicate modifier accepted.");
    string command="Write-Output 'quoted $text'\r\nGet-Date";c.Keys[7]=new Binding("powershell",command);c.Validate();
    Configuration copy=new JavaScriptSerializer().Deserialize<Configuration>(new JavaScriptSerializer().Serialize(c));copy.Validate();if(copy.Keys[7].Value!=command)throw new Exception("PowerShell text changed in JSON roundtrip.");
    copy.RequireFn=false;if(c.RequireFn!=true)throw new Exception("Clone mutated original configuration.");
    TestDriverProfile();
    File.WriteAllText(report,"PASS: all 14 defaults (Fn + F1-F12 + Lock); input-method Fn default; single-key and shortcut parsing; multiline command JSON; configuration independence; driver profile matches built-in defaults; HID translation; USB packet decoding and gesture timing.\r\n");
   }catch(Exception e){File.WriteAllText(report,"FAIL: "+e);Environment.ExitCode=1;}
  }
 }
}







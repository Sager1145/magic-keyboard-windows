// Maintains Apple's native HID brightness mode across hotplug and sleep.
// Windows handles brightness, volume and their overlays. No key hooks or WMI.
using System;
using System.IO;
using System.Threading;

class NativeMode {
 static readonly string log=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"native-mode.log");
 static void Main(string[] args) {
  using(Mutex mutex=new Mutex(false,"Local\\MagicKeyboard0320NativeMode")) {
   if(!mutex.WaitOne(0)) return;
   bool? ready=null;
   while(true) {
    try {
     NativeBrightness.Enable();
     if(ready!=true) Write("Native HID brightness enabled; Windows handles brightness/volume and overlays.");
     ready=true;
    } catch(Exception e) {
     if(ready!=false) Write("Waiting for Apple keyboard driver: "+e.Message);
     ready=false;
    }
    if(args.Length>0 && args[0]=="--once") return;
    Thread.Sleep(2000);
   }
  }
 }
 static void Write(string message) {
  try { File.AppendAllText(log,DateTime.Now.ToString("o")+" "+message+Environment.NewLine); } catch(IOException) {}
 }
}

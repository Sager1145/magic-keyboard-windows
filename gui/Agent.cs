using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace MagicKeyboard {
 // Tray companion for the MagicKeyboardFn driver ("MagicKeyboardControls.exe --agent").
 // The driver performs every HID-expressible mapping on its own; this keeps the
 // driver in sync with settings.json and runs the app-only actions it posts.
 sealed class AgentWindow : Form {
  public const string MutexName="Local\\MagicKeyboardAgent";
  public static readonly string StatusFile=Path.Combine(Configuration.Folder,"agent-status.txt");
  Configuration saved;ActionRunner runner;NotifyIcon tray;DriverCapture capture;
  readonly bool observe;bool started;
  string driverText="Starting";
  BatteryReading battery;bool lowWarned;System.Threading.Timer batteryPoll;int polling;
  IntPtr usbNotifications;
  [StructLayout(LayoutKind.Sequential)] struct DeviceFilter {
   public int Size, Type, Reserved;
   public Guid Class;
   public short Name;
  }
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
  static extern IntPtr RegisterDeviceNotification(IntPtr window,ref DeviceFilter filter,int flags);
  [DllImport("user32.dll")]
  static extern bool UnregisterDeviceNotification(IntPtr registration);
  [DllImport("wtsapi32.dll")] static extern bool WTSRegisterSessionNotification(IntPtr window,int flags);
  [DllImport("wtsapi32.dll")] static extern bool WTSUnRegisterSessionNotification(IntPtr window);
  [DllImport("kernel32.dll")] static extern int WTSGetActiveConsoleSessionId();
  IntPtr hidNotifications;
  // The driver serves only the session at the physical console; with several users signed
  // in, the agent of the session in front of the keyboard holds the driver.
  static bool AtConsole(){return WTSGetActiveConsoleSessionId()==Process.GetCurrentProcess().SessionId;}
  // An elevated agent would run commands from a settings file any normal process can edit.
  // (With UAC off every process is elevated, so there is no lower level to protect.)
  public static bool Elevated(){
   object lua=Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System","EnableLUA",1);
   if(lua is int && (int)lua==0)return false;
   using(var id=System.Security.Principal.WindowsIdentity.GetCurrent())return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
  }
  PhysicalKeys physical;
  readonly System.Windows.Forms.Timer retry=new System.Windows.Forms.Timer{Interval=2000};
  readonly System.Windows.Forms.Timer reload=new System.Windows.Forms.Timer{Interval=300};
  FileSystemWatcher watcher;
  public AgentWindow(bool observe){
   this.observe=observe;
   ShowInTaskbar=false;FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;Location=new Point(-32000,-32000);Size=new Size(1,1);
  }
  // Never show a window; the handle receives device notifications.
  protected override void SetVisibleCore(bool value){
   if(!IsHandleCreated)CreateHandle();
   if(!started){started=true;Start();}
   base.SetVisibleCore(false);
  }
  static Configuration LoadProfile(){try{return Configuration.Load();}catch(Exception){return Configuration.Create();}}
  void Start(){
   saved=LoadProfile();
   runner=new ActionRunner(text=>{}); // results are not recorded
   var filter=new DeviceFilter{
    Size=Marshal.SizeOf(typeof(DeviceFilter)),Type=5,
    Class=new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED")
   };
   usbNotifications=RegisterDeviceNotification(Handle,ref filter,0);
   filter.Class=new Guid("4D1E55B2-F16F-11CF-88CB-001111000030"); // HID: the Bluetooth connection
   hidNotifications=RegisterDeviceNotification(Handle,ref filter,0);
   WTSRegisterSessionNotification(Handle,0);
   retry.Tick+=(s,e)=>{if(capture==null && AtConsole())Connect();};retry.Start();
   // Battery: a Bluetooth battery device reports its level at connect and notifies on each
   // change. This keyboard only answers when asked, so read it at connect and on wake, then
   // check often enough to catch each 1% step: every minute while charging (about 1% a
   // minute), every 5 minutes on battery (well under 1% an hour). The display only changes
   // when the level does.
   batteryPoll=new System.Threading.Timer(s=>PollBattery(),null,1000,System.Threading.Timeout.Infinite);
   Microsoft.Win32.SystemEvents.PowerModeChanged+=OnPowerMode;
   Connect();
   Directory.CreateDirectory(Configuration.Folder);
   watcher=new FileSystemWatcher(Configuration.Folder,"settings.json"){NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName,SynchronizingObject=this};
   watcher.Changed+=(s,e)=>{reload.Stop();reload.Start();};
   watcher.Created+=(s,e)=>{reload.Stop();reload.Start();};
   watcher.Renamed+=(s,e)=>{reload.Stop();reload.Start();};
   watcher.EnableRaisingEvents=true;
   reload.Tick+=(s,e)=>{reload.Stop();Reload();};
   Icon icon;try{icon=Theme.TrayIcon();}catch(Exception){icon=SystemIcons.Application;}
   tray=new NotifyIcon{Icon=icon,Visible=true,Text=Theme.ProductName};
   ContextMenuStrip menu=Theme.Menu();
   ToolStripItem open=menu.Items.Add("Customize keys…",null,(s,e)=>OpenSettings());open.Font=new Font(menu.Font,FontStyle.Bold);
   menu.Items.Add("Reconnect keyboard",null,(s,e)=>Connect());
   menu.Items.Add(new ToolStripSeparator());
   menu.Items.Add("Quit tray (keys keep working)",null,(s,e)=>Close());
   foreach(ToolStripItem item in menu.Items){item.ForeColor=Theme.Ink;item.Padding=new Padding(4,5,4,5);}
   tray.ContextMenuStrip=menu;
   tray.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left)OpenSettings();};
  }
  // Brings the open settings window forward, or starts it.
  static void OpenSettings(){if(!LiveKeys.ShowSettings())Process.Start(Application.ExecutablePath);}
  void Post(Action action){if(IsDisposed)return;try{BeginInvoke(action);}catch(InvalidOperationException){}}
  void Reload(){
   try{saved=Configuration.Load();Push();}
   catch(Exception e){driverText="Profile not reloaded · "+e.Message;WriteStatus();}
  }
  void Push(){
   try{
    bool requireFn=Driver.Apply(saved);
    driverText=(requireFn?"F1–F12 first · hold Fn for actions":"Actions first · hold Fn for F1–F12")+"  |  Driver mappings active";
   }catch(Exception e){driverText="Driver unavailable · "+e.Message;}
   WriteStatus();
  }
  // The status file holds driver state and the battery (percent;charging;plugged in), never
  // keys or actions.
  void WriteStatus(){
   string level=battery.Valid?battery.Percent+";"+(battery.Charging?1:0)+";"+(battery.PluggedIn?1:0):"";
   try{File.WriteAllText(StatusFile,driverText+"\n"+level);}catch(IOException){}catch(UnauthorizedAccessException){}
  }
  // Runs on a pool thread: HID GET_REPORT is a blocking USB / Bluetooth round trip.
  void PollBattery(){
   if(Interlocked.Exchange(ref polling,1)!=0)return;
   try{
    BatteryReading r;
    try{r=Battery.Read();}catch(Exception){r=new BatteryReading();}
    // Over USB, hand the level to the driver for Settings on every read: a replugged
    // keyboard gets a new driver instance that has shown nothing yet, and the driver skips
    // writing a level it already shows. An older driver without the request just fails it.
    if(r.Valid && r.PluggedIn){try{Driver.PublishBattery(r.Percent);}catch(Exception){}}
    Post(()=>OnBattery(r));
    try{batteryPoll.Change(r.Valid && !r.Charging?BatteryIdleMs:BatteryChargingMs,System.Threading.Timeout.Infinite);}catch(ObjectDisposedException){}
   }finally{Interlocked.Exchange(ref polling,0);}
  }
  const int BatteryChargingMs=60000,BatteryIdleMs=300000;
  void ReadBatterySoon(int delayMs){if(batteryPoll!=null)try{batteryPoll.Change(delayMs,System.Threading.Timeout.Infinite);}catch(ObjectDisposedException){}}
  void OnPowerMode(object sender,Microsoft.Win32.PowerModeChangedEventArgs e){if(e.Mode==Microsoft.Win32.PowerModes.Resume)ReadBatterySoon(5000);}
  void OnBattery(BatteryReading r){
   bool changed=r.Valid!=battery.Valid || r.Percent!=battery.Percent || r.Charging!=battery.Charging || r.PluggedIn!=battery.PluggedIn;
   battery=r;
   if(tray!=null){
    // Names the keyboard so it isn't mistaken for the PC's battery (NotifyIcon text max 63).
    tray.Text=r.Valid?"Magic Keyboard battery "+r:Theme.ProductName;
    if(changed){
     Icon old=tray.Icon,icon;
     try{icon=r.Valid?Theme.BatteryTrayIcon(r.Percent,r.Charging):Theme.TrayIcon();}catch(Exception){icon=null;}
     if(icon!=null){tray.Icon=icon;if(old!=null)old.Dispose();}
    }
   }
   if(r.Valid && !r.Charging && r.Percent<=10 && !lowWarned && tray!=null){
    lowWarned=true;
    tray.ShowBalloonTip(8000,"Magic Keyboard battery low",r.Percent+"% left. Connect the cable to charge.",ToolTipIcon.Warning);
   }
   if(r.Valid && (r.Charging || r.Percent>15))lowWarned=false;
   if(changed)WriteStatus();
  }
  void Disconnect(string why){
   if(capture!=null){capture.Dispose();capture=null;}
   driverText=why;WriteStatus();
  }
  void Connect(){
   if(capture!=null){capture.Dispose();capture=null;}
   if(!AtConsole()){driverText="Waiting · another user is at the keyboard";WriteStatus();return;}
   physical=new PhysicalKeys();
   DriverCapture source=null;
   try{
    source=new DriverCapture(
     keys=>Post(()=>{if(capture==source)OnPhysical(keys);}),
     (key,gesture)=>Post(()=>{if(capture==source)OnApp(key,gesture);}),
     text=>Post(()=>{if(capture!=source)return;capture.Dispose();capture=null;driverText=text;WriteStatus();}));
   }catch(System.ComponentModel.Win32Exception e){driverText="Driver unavailable · "+e.Message;WriteStatus();return;}
   capture=source;
   Push();
  }
  void OnApp(int key,int gesture){
   Binding binding=saved.GetBinding(key,gesture);
   if(observe)return;
   runner.Execute(key,binding);
  }
  // Passes presses that involve Fn, F1-F12 or Lock to the settings window while it is open,
  // as a window message. Nothing is stored, and plain typing is never passed on.
  int lastKey=-1;
  void OnPhysical(PhysicalKeys keys){
   PhysicalKeys was=physical;physical=keys;
   if(keys.Held==0 && was.Held==0)return;
   int fresh=keys.Held & ~was.Held;
   if(fresh!=0){
    int row=fresh&0xFFF;
    if(row!=0){lastKey=0;while((row&(1<<lastKey))==0)lastKey++;}
    else lastKey=(fresh&(1<<12))!=0?12:13;
   }
   if(keys.Held==0)lastKey=-1;
   IntPtr window=LiveKeys.Window();
   if(window!=IntPtr.Zero)LiveKeys.Send(window,keys,lastKey);
  }
  // The keyboard arriving over USB (vendor 05AC) or Bluetooth (vendor 004C).
  static bool IsKeyboard(string device){
   if(device==null)return false;
   string d=device.ToUpperInvariant();
   // 0320 USB-C Magic Keyboard, 0267 Magic Keyboard 2 (2015)
   return d.Contains("VID_05AC&PID_0320") || d.Contains("004C_PID&0320") ||
          d.Contains("VID_05AC&PID_0267") || d.Contains("004C_PID&0267");
  }
  protected override void WndProc(ref Message message){
   if(message.Msg==0x219 && message.LParam!=IntPtr.Zero &&
      message.WParam.ToInt32()==0x8000 && Marshal.ReadInt32(message.LParam,4)==5){
    if(IsKeyboard(Marshal.PtrToStringUni(IntPtr.Add(message.LParam,28)))){Connect();ReadBatterySoon(3000);}
   }
   if(message.Msg==0x2B1){ // WM_WTSSESSION_CHANGE
    int change=message.WParam.ToInt32();
    if(change==2)Disconnect("Waiting · another user is at the keyboard"); // WTS_CONSOLE_DISCONNECT
    else if(change==1){Connect();ReadBatterySoon(3000);} // WTS_CONSOLE_CONNECT
   }
   base.WndProc(ref message);
  }
  protected override void Dispose(bool disposing){
   if(disposing){
    if(watcher!=null)watcher.Dispose();
    Microsoft.Win32.SystemEvents.PowerModeChanged-=OnPowerMode;
    if(batteryPoll!=null)batteryPoll.Dispose();
    if(usbNotifications!=IntPtr.Zero){UnregisterDeviceNotification(usbNotifications);usbNotifications=IntPtr.Zero;}
    if(hidNotifications!=IntPtr.Zero){UnregisterDeviceNotification(hidNotifications);hidNotifications=IntPtr.Zero;}
    if(IsHandleCreated)WTSUnRegisterSessionNotification(Handle);
    if(capture!=null){capture.Dispose();capture=null;}
    retry.Dispose();reload.Dispose();
    if(tray!=null){tray.Visible=false;tray.Dispose();}
    driverText="Tray closed · app-only actions paused, driver mappings active";WriteStatus();
   }
   base.Dispose(disposing);
  }
  public static bool Running(){
   bool created;
   using(Mutex m=new Mutex(false,MutexName,out created)){
    try{if(!m.WaitOne(0))return true;}
    catch(AbandonedMutexException){} // a crashed agent leaves the mutex abandoned, now owned here
    m.ReleaseMutex();return false;
   }
  }
  public static void EnsureRunning(){
   if(!Running() && !Elevated())Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--agent"){UseShellExecute=false});
  }
 }
}

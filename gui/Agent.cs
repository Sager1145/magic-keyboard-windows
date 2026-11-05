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
  string driverText="Starting",actionText="",inputText="No function-key input yet";
  int liveHeld,liveKey=-1,liveSeq;bool liveFn;
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
   runner=new ActionRunner(text=>Post(()=>{actionText=text;WriteStatus();}));
   var filter=new DeviceFilter{
    Size=Marshal.SizeOf(typeof(DeviceFilter)),Type=5,
    Class=new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED")
   };
   usbNotifications=RegisterDeviceNotification(Handle,ref filter,0);
   retry.Tick+=(s,e)=>{if(capture==null)Connect();};retry.Start();
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
  static void OpenSettings(){Process.Start(Application.ExecutablePath);}
  void Post(Action action){if(IsDisposed)return;try{BeginInvoke(action);}catch(InvalidOperationException){}}
  void Reload(){
   try{saved=Configuration.Load();actionText="Profile reloaded";Push();}
   catch(Exception e){actionText="Profile not reloaded · "+e.Message;WriteStatus();}
  }
  void Push(){
   try{
    bool requireFn=Driver.Apply(saved);
    driverText=(requireFn?"F1–F12 first · hold Fn for actions":"Actions first · hold Fn for F1–F12")+"  |  Driver mappings active";
   }catch(Exception e){driverText="Driver unavailable · "+e.Message;}
   WriteStatus();
  }
  void WriteStatus(){
   // Line 4 feeds the settings window's live display: held keys;last key;Fn held;press count.
   string live=liveHeld+";"+liveKey+";"+(liveFn?1:0)+";"+liveSeq;
   try{File.WriteAllText(StatusFile,driverText+"\n"+actionText+"\n"+inputText+"\n"+live);}catch(IOException){}catch(UnauthorizedAccessException){}
  }
  void Connect(){
   if(capture!=null){capture.Dispose();capture=null;}
   physical=new PhysicalKeys();
   DriverCapture source=null;
   try{
    source=new DriverCapture(
     keys=>Post(()=>{if(capture==source)OnPhysical(keys);}),
     (key,gesture)=>Post(()=>{if(capture==source)OnApp(key,gesture);}),
     text=>Post(()=>{if(capture!=source)return;capture.Dispose();capture=null;inputText=text;WriteStatus();}));
   }catch(System.ComponentModel.Win32Exception e){driverText="Driver unavailable · "+e.Message;WriteStatus();return;}
   capture=source;
   Push();
  }
  void OnApp(int key,int gesture){
   Binding binding=saved.GetBinding(key,gesture);
   if(observe){actionText="App action (observe only): "+Actions.Name(binding);WriteStatus();return;}
   runner.Execute(key,binding);
  }
  // Reports presses that involve Fn, F1-F12 or Lock (with whatever else is held) for the
  // settings window. Plain typing is never recorded.
  void OnPhysical(PhysicalKeys keys){
   PhysicalKeys was=physical;physical=keys;
   int fresh=keys.Held & ~was.Held;
   bool changed=keys.Held!=0 && (fresh!=0 || keys.Mods!=was.Mods || keys.Extra!=was.Extra);
   liveHeld=keys.Held;
   if(!changed){if(keys.Held!=was.Held)WriteStatus();return;} // a release: only the highlight changes
   if(fresh!=0){
    int row=fresh&0xFFF;
    if(row!=0){liveKey=0;while((row&(1<<liveKey))==0)liveKey++;}
    else liveKey=(fresh&(1<<12))!=0?12:13;
   }
   liveFn=keys.Fn;liveSeq++;
   string pressed=keys.Describe();
   inputText="Fn driver · "+pressed;
   WriteStatus();
   if(observe)File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"physical-verification.log"),pressed+Environment.NewLine);
  }
  protected override void WndProc(ref Message message){
   if(message.Msg==0x219 && message.LParam!=IntPtr.Zero &&
      message.WParam.ToInt32()==0x8000 && Marshal.ReadInt32(message.LParam,4)==5){
    string device=Marshal.PtrToStringUni(IntPtr.Add(message.LParam,28));
    if(device!=null && device.IndexOf("VID_05AC&PID_0320",StringComparison.OrdinalIgnoreCase)>=0)Connect();
   }
   base.WndProc(ref message);
  }
  protected override void Dispose(bool disposing){
   if(disposing){
    if(watcher!=null)watcher.Dispose();
    if(usbNotifications!=IntPtr.Zero){UnregisterDeviceNotification(usbNotifications);usbNotifications=IntPtr.Zero;}
    if(capture!=null){capture.Dispose();capture=null;}
    retry.Dispose();reload.Dispose();
    if(tray!=null){tray.Visible=false;tray.Dispose();}
    actionText="Tray closed · app-only actions paused, driver mappings active";WriteStatus();
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
   if(!Running())Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--agent"){UseShellExecute=false});
  }
 }
}

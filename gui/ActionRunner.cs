using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Automation;
using System.Threading;

namespace MagicKeyboard {
 public static class Shortcut {
  public static ushort[] Parse(string text) {
   if(String.IsNullOrWhiteSpace(text))throw new ArgumentException("Enter a key combination, for example Ctrl+Alt+K or Win+Shift+S.");
   List<ushort> keys=new List<ushort>();int ordinary=0;
   foreach(string item in text.Split('+')) {
    string s=item.Trim();ushort key;
    switch(s.ToLowerInvariant()) {
     case "ctrl":case "control":key=0x11;break;
     case "alt":key=0x12;break;
     case "shift":key=0x10;break;
     case "win":case "windows":key=0x5B;break;
     default:
      Keys parsed;
      if(!Enum.TryParse<Keys>(s,true,out parsed) || (int)parsed<1 || (int)parsed>255)throw new ArgumentException("Unrecognized key: "+s);
      key=(ushort)parsed;ordinary++;break;
    }
    if(keys.Contains(key))throw new ArgumentException("A key appears twice in the combination.");keys.Add(key);
   }
   if(ordinary!=1)throw new ArgumentException("Use modifiers plus one main key.");
   return keys.ToArray();
  }
  public static ushort SingleKey(string text) {
   if(String.IsNullOrWhiteSpace(text))throw new ArgumentException("Enter a single key, for example K, F5, Escape, or VolumeUp.");
   Keys parsed;
   if(!Enum.TryParse<Keys>(text.Trim(),true,out parsed) || (int)parsed<1 || (int)parsed>255)throw new ArgumentException("Unrecognized key: "+text);
   return (ushort)parsed;
  }
 }
 public static class Input {
  public static readonly UIntPtr Marker=new UIntPtr(0x4D4B3033);
  [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT{public ushort vk,scan;public uint flags,time;public UIntPtr extra;}
  [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT{public int dx,dy;public uint data,flags,time;public UIntPtr extra;}
  [StructLayout(LayoutKind.Explicit)] struct UNION{[FieldOffset(0)]public KEYBDINPUT keyboard;[FieldOffset(0)]public MOUSEINPUT mouse;}
  [StructLayout(LayoutKind.Sequential)] struct INPUT{public uint type;public UNION u;}
  [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] inputs,int size);
  [DllImport("user32.dll")] static extern bool LockWorkStation();
  static INPUT Key(ushort vk,bool up){return new INPUT{type=1,u=new UNION{keyboard=new KEYBDINPUT{vk=vk,flags=(uint)(up?2:0),extra=Marker}}};}
  public static void KeyEvent(int vk,bool up){INPUT[] seq={Key((ushort)vk,up)};if(SendInput(1,seq,Marshal.SizeOf(typeof(INPUT)))!=1)throw new System.ComponentModel.Win32Exception();}
  public static void Chord(string value){ushort[] keys=Shortcut.Parse(value);List<INPUT> seq=new List<INPUT>();foreach(ushort k in keys)seq.Add(Key(k,false));for(int i=keys.Length-1;i>=0;i--)seq.Add(Key(keys[i],true));if(SendInput((uint)seq.Count,seq.ToArray(),Marshal.SizeOf(typeof(INPUT)))!=seq.Count)throw new System.ComponentModel.Win32Exception();}
  public static void Tap(int vk){
   INPUT[] seq={Key((ushort)vk,false),Key((ushort)vk,true)};
   if(SendInput(2,seq,Marshal.SizeOf(typeof(INPUT)))!=2)throw new System.ComponentModel.Win32Exception();
  }
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int length);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int length);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out int processId);
  public static void ToggleSearch(){
   IntPtr foreground=GetForegroundWindow();
   int processId;
   GetWindowThreadProcessId(foreground,out processId);
   if(processId!=0){
    try {
     using(Process process=Process.GetProcessById(processId)){
      if(process.ProcessName.Equals("SearchHost",StringComparison.OrdinalIgnoreCase) &&
         GetForegroundWindow()==foreground){
       Tap(0x1B);
       return;
      }
     }
    } catch(ArgumentException){}
   }
   Chord("Win+S");
  }
  public static void ToggleTaskView(){
   IntPtr foreground=GetForegroundWindow();
   var windowClass=new StringBuilder(128);
   GetClassName(foreground,windowClass,windowClass.Capacity);
   string name=windowClass.ToString();
   if(name=="MultitaskingViewFrame"){Tap(0x1B);return;}
   if(name=="XamlExplorerHostIslandWindow"){
    var title=new StringBuilder(128);
    GetWindowText(foreground,title,title.Capacity);
    string text=title.ToString();
    if(text=="Task View" || text=="工作檢視" || text=="任务视图" || FocusIsInTaskView()){Tap(0x1B);return;}
   }
   Chord("Win+Tab");
  }
  static bool FocusIsInTaskView(){
   try {
    AutomationElement item=AutomationElement.FocusedElement;
    for(int depth=0;item!=null && depth<8;depth++){
     if(item.Current.AutomationId=="TaskViewTimelineRoot")return true;
     item=TreeWalker.RawViewWalker.GetParent(item);
    }
   } catch(ElementNotAvailableException){}
   return false;
  }
  // Do Not Disturb through the shell's QuietHoursSettings COM service, the same one the
  // taskbar bell uses (interface layout from WinDndToggle by HalZheng, MIT; see NOTICE).
  // Slot 3 get_UserSelectedProfile(out LPWSTR, CoTaskMem), slot 4 put_UserSelectedProfile(LPCWSTR).
  const string DndOff="Microsoft.QuietHoursProfile.Unrestricted",DndOn="Microsoft.QuietHoursProfile.PriorityOnly";
  [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid,IntPtr outer,uint context,ref Guid iid,out IntPtr instance);
  [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr memory);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ReleaseFn(IntPtr self);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetProfileFn(IntPtr self,out IntPtr profile);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int PutProfileFn(IntPtr self,IntPtr profile);
  static Delegate Slot(IntPtr self,int index,Type type){return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self),index*IntPtr.Size),type);}
  static IntPtr QuietHours(){
   Guid clsid=new Guid("F53321FA-34F8-4B7F-B9A3-361877CB94CF"),iid=new Guid("6BFF4732-81EC-4FFB-AE67-B6C1BC29631F");
   IntPtr service;int hr=CoCreateInstance(ref clsid,IntPtr.Zero,4,ref iid,out service);
   if(hr!=0)throw new InvalidOperationException("Windows Do Not Disturb service is unavailable (0x"+hr.ToString("X8")+").");
   return service;
  }
  public static bool DoNotDisturb(){
   IntPtr service=QuietHours();
   try{
    IntPtr profile;int hr=((GetProfileFn)Slot(service,3,typeof(GetProfileFn)))(service,out profile);
    if(hr!=0||profile==IntPtr.Zero)throw new InvalidOperationException("Could not read Do Not Disturb (0x"+hr.ToString("X8")+").");
    try{return Marshal.PtrToStringUni(profile)!=DndOff;}finally{CoTaskMemFree(profile);}
   }finally{((ReleaseFn)Slot(service,2,typeof(ReleaseFn)))(service);}
  }
  public static bool ToggleDoNotDisturb(){
   bool on=!DoNotDisturb();
   IntPtr service=QuietHours(),text=Marshal.StringToHGlobalUni(on?DndOn:DndOff);
   try{
    int hr=((PutProfileFn)Slot(service,4,typeof(PutProfileFn)))(service,text);
    if(hr!=0)throw new InvalidOperationException("Windows rejected the Do Not Disturb change (0x"+hr.ToString("X8")+").");
   }finally{Marshal.FreeHGlobal(text);((ReleaseFn)Slot(service,2,typeof(ReleaseFn)))(service);}
   return on;
  }

  // Voice typing lives in the Windows input experience (TextInputHost). Its CoreWindow is
  // cloaked while no input panel is showing, so an uncloaked one means the panel is open.
  delegate bool EnumWindowsProc(IntPtr window,IntPtr parameter);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback,IntPtr parameter);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
  static IntPtr OpenInputPanel(){
   var hosts=new HashSet<int>(Process.GetProcessesByName("TextInputHost").Select(p=>p.Id));
   IntPtr found=IntPtr.Zero;
   EnumWindows((window,parameter)=>{
    int processId;GetWindowThreadProcessId(window,out processId);
    if(!hosts.Contains(processId)||!IsWindowVisible(window))return true;
    var windowClass=new StringBuilder(64);GetClassName(window,windowClass,windowClass.Capacity);
    int cloaked;
    if(windowClass.ToString()=="Windows.UI.Core.CoreWindow" && DwmGetWindowAttribute(window,14,out cloaked,4)==0 && cloaked==0){found=window;return false;}
    return true;
   },IntPtr.Zero);
   return found;
  }
  public static void ToggleVoiceTyping(){
   IntPtr panel=OpenInputPanel();
   if(panel==IntPtr.Zero){Chord("Win+H");return;}
   try{
    var buttons=AutomationElement.FromHandle(panel).FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
    foreach(AutomationElement button in buttons){
     string id=button.Current.AutomationId;
     if(id.IndexOf("Close",StringComparison.OrdinalIgnoreCase)<0 && id.IndexOf("Dismiss",StringComparison.OrdinalIgnoreCase)<0)continue;
     object invoke;
     if(button.TryGetCurrentPattern(InvokePattern.Pattern,out invoke)){((InvokePattern)invoke).Invoke();return;}
    }
   }catch(ElementNotAvailableException){}
   // No close button found: dismiss with Escape.
   SetForegroundWindow(panel);
   Tap(0x1B);
  }
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
  public static void Lock(){if(!LockWorkStation())throw new System.ComponentModel.Win32Exception();}
  public static void TypeText(string text) {
   if(String.IsNullOrEmpty(text))return;
   List<INPUT> seq=new List<INPUT>();
   foreach(char ch in text) {
    seq.Add(new INPUT{type=1,u=new UNION{keyboard=new KEYBDINPUT{vk=0,scan=ch,flags=4,extra=Marker}}});
    seq.Add(new INPUT{type=1,u=new UNION{keyboard=new KEYBDINPUT{vk=0,scan=ch,flags=6,extra=Marker}}});
   }
   if(seq.Count>0 && SendInput((uint)seq.Count,seq.ToArray(),Marshal.SizeOf(typeof(INPUT)))!=seq.Count)throw new System.ComponentModel.Win32Exception();
  }
 }
 public sealed class ActionRunner {
  readonly Action<string> status;
  readonly HashSet<int> running=new HashSet<int>();
  int desiredBrightness=-1;DateTime brightnessTime=DateTime.MinValue;
  public ActionRunner(Action<string> callback){status=callback;}
  public void Execute(int index,Binding binding) {
   Binding copy=new Binding(binding.Action,binding.Value);
   lock(running){if(running.Contains(index))return;running.Add(index);}
   Task.Run(()=>{try{Run(copy);status(KeyName(index)+": "+Actions.Name(copy));}catch(Exception e){status("Action failed: "+e.Message);}finally{lock(running)running.Remove(index);}});
  }
  static string KeyName(int index){return index==13?"Fn":index==12?"Lock":"F"+(index+1);}
  void Run(Binding b){switch(b.Action){
   case "none":return;
   case "shortcut":Input.Chord(b.Value);return;
   case "powershell":PowerShell(b.Value);return;
   case "task-view":Input.ToggleTaskView();return;
   case "search":Input.ToggleSearch();return;
   case "voice-typing":Input.ToggleVoiceTyping();return;
   case "do-not-disturb":Input.ToggleDoNotDisturb();return;
   case "notifications":Input.Chord("Win+N");return;
   case "quick-settings":Input.Chord("Win+A");return;
   case "screenshot":Input.Chord("Win+Shift+S");return;
   case "show-desktop":Input.Chord("Win+D");return;
   case "calculator":Process.Start("calc.exe");return;
   case "lock":Input.Lock();return;
   case "previous-track":Input.Tap(0xB1);return;
   case "play-pause":Input.Tap(0xB3);return;
   case "next-track":Input.Tap(0xB0);return;
   case "mute":Input.Tap(0xAD);return;
   case "volume-down":Input.Tap(0xAE);return;
   case "volume-up":Input.Tap(0xAF);return;
   case "brightness-down":Brightness(-5);return;
   case "brightness-up":Brightness(5);return;
   case "input-method":Input.Chord("Win+Space");return;
   case "clipboard-history":Input.Chord("Win+V");return;
   case "emoji":Input.Chord("Win+OemPeriod");return;
      case "sleep":PowerActions.Sleep(PowerState.Suspend);return;
   case "power":PowerActions.PowerButton();return;
   case "key":Input.Tap(Shortcut.SingleKey(b.Value));return;
   case "text":Input.TypeText(b.Value);return;
   case "app":Process.Start(b.Value);return;
   case "url":Process.Start(b.Value);return;
   case "folder":Process.Start(b.Value);return;
   default:throw new ArgumentException("Unknown action");}}
  // Only reached from "Try it": key presses send brightness as HID usages through the driver.
  void Brightness(int delta) {
   lock(this) {
    if(desiredBrightness<0 || (DateTime.UtcNow-brightnessTime).TotalSeconds>2)desiredBrightness=WindowsControls.GetBrightness();
    desiredBrightness=Math.Max(0,Math.Min(100,desiredBrightness+delta));brightnessTime=DateTime.UtcNow;
    WindowsControls.SetBrightness(desiredBrightness);
   }
  }  static void PowerShell(string command) {
   string encoded=Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
   ProcessStartInfo info=new ProcessStartInfo("powershell.exe","-NoLogo -NoProfile -NonInteractive -EncodedCommand "+encoded){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
   using(Process p=Process.Start(info)){
    Task<string> output=p.StandardOutput.ReadToEndAsync(),error=p.StandardError.ReadToEndAsync();
    // A command that hangs would block its key forever: give it 2 minutes.
    if(!p.WaitForExit(120000)){try{p.Kill();}catch(InvalidOperationException){}throw new TimeoutException("PowerShell command timed out after 2 minutes.");}
    Task.WaitAll(output,error);
    if(p.ExitCode!=0)throw new InvalidOperationException("PowerShell exited "+p.ExitCode+": "+error.Result.Substring(0,Math.Min(500,error.Result.Length)));
   }
  }
 }
}






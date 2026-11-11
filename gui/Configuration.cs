using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace MagicKeyboard {
 public sealed class Binding {
  public string Action {get;set;}
  public string Value {get;set;}
  public Binding() {Action="none";Value="";}
  public Binding(string action,string value="") {Action=action;Value=value;}
 }
 public sealed class Configuration {
  public int Version {get;set;}
  public bool RequireFn {get;set;}
  public bool Enabled {get;set;}
  public int LockVirtualKey {get;set;}
  public int LockScanCode {get;set;}
  public int LockHoldMs {get;set;} // how long Lock is held for its hold action
  public const int MinLockHoldMs=500,MaxLockHoldMs=5000;
  public Binding[] Keys {get;set;}
  // What each modifier key (and fn) acts as: one of ModifierTargets.
  // Order: left control, left option, left command, right command, right option, fn.
  public string[] Modifiers {get;set;}
  public static readonly string[] ModifierDefaults={"control","option","command","command","option","fn"};
  // control = Ctrl, option = Alt, command = Win, fn = the Fn layer, menu = the context-menu
  // ("list") key, copilot = the Windows Copilot key, none = nothing.
  public static readonly string[] ModifierTargets={"control","option","command","shift","fn","menu","copilot","none"};
  public const int FnModifier=5; // index of fn in Modifiers
  public Binding[] Gestures {get;set;}
  public static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MagicKeyboardControls");
  public static readonly string FileName=Path.Combine(Folder,"settings.json");
  public static readonly string[] Defaults={"brightness-down","brightness-up","task-view","search","voice-typing","screenshot","previous-track","play-pause","next-track","mute","volume-down","volume-up","none","input-method"};
  public static readonly string[] Labels={"Brightness down","Brightness up","Task View","Windows Search","Voice typing","Screenshot","Previous track","Play / pause","Next track","Mute","Volume down","Volume up","Do nothing","Language / input method"};
  // Display order for the row list: Fn first, then F1-F12, then Lock. Maps display index -> data index.
  public static readonly int[] Order={13,0,1,2,3,4,5,6,7,8,9,10,11,12};
  public static readonly string[] RowLabels={"Fn","F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12","Lock"};
  public static Configuration Create() {
   Configuration c=new Configuration{Version=3,RequireFn=true,Enabled=false,LockVirtualKey=0x5F,LockScanCode=-1,LockHoldMs=1000,Keys=new Binding[14],Gestures=EmptyGestures(),Modifiers=(string[])ModifierDefaults.Clone()};
   for(int i=0;i<14;i++)c.Keys[i]=DefaultBinding(i,0);c.SetBinding(12,2,DefaultBinding(12,2));return c;
  }
  public static Binding DefaultBinding(int key,int gesture){
   return new Binding(gesture==0?Defaults[key]:key==12 && gesture==2?"lock":"none");
  }
  static Binding[] EmptyGestures(){return new[]{new Binding(),new Binding(),new Binding(),new Binding()};}
  public Binding GetBinding(int key,int gesture){return gesture==0?Keys[key]:Gestures[(key-12)*2+gesture-1];}
  public void SetBinding(int key,int gesture,Binding binding){
   if(gesture==0)Keys[key]=binding;
   else Gestures[(key-12)*2+gesture-1]=binding;
  }
  public void Validate() {
   if(Gestures==null)Gestures=EmptyGestures();
   if(Gestures.Length!=4)throw new InvalidDataException("Invalid Fn or Lock gesture profile.");
   if(Version<1 || Version>3 || Keys==null || Keys.Length!=14) throw new InvalidDataException("The profile needs Fn, F1-F12 and Lock bindings.");
   foreach(Binding b in new List<Binding>(Keys).Concat(Gestures)) {
    if(b==null || !Actions.Contains(b.Action)) throw new InvalidDataException("Unknown action in profile.");
    if(b.Value==null)b.Value="";
    if(b.Action=="shortcut") Shortcut.Parse(b.Value);
    if(b.Action=="key") Shortcut.SingleKey(b.Value);
    if((b.Action=="powershell"||b.Action=="text"||b.Action=="app"||b.Action=="url"||b.Action=="folder") && String.IsNullOrWhiteSpace(b.Value)) throw new InvalidDataException("This action needs a value (command, text, or path).");
   }
   if(Version==1){
    if(Keys[12].Action=="lock" && GetBinding(12,2).Action=="none"){
     Keys[12]=DefaultBinding(12,0);
     SetBinding(12,2,DefaultBinding(12,2));
    }
    Version=2;
   }
   if(Version==2){
    // Windows has no Do Not Disturb key, so F6 now defaults to Screenshot.
    if(Keys[5].Action=="do-not-disturb")Keys[5]=DefaultBinding(5,0);
    Version=3;
   }
   // Fn itself has only a tap action.
   SetBinding(13,1,new Binding());SetBinding(13,2,new Binding());
   if(LockHoldMs==0)LockHoldMs=1000;
   // Profiles before fn could be remapped have five entries: keep them and add fn.
   if(Modifiers!=null && Modifiers.Length==ModifierDefaults.Length-1)Modifiers=Modifiers.Concat(new[]{ModifierDefaults[FnModifier]}).ToArray();
   if(Modifiers==null || Modifiers.Length!=ModifierDefaults.Length)Modifiers=(string[])ModifierDefaults.Clone();
   foreach(string m in Modifiers)if(Array.IndexOf(ModifierTargets,m)<0)throw new InvalidDataException("Unknown modifier key mapping.");
   if(LockHoldMs<MinLockHoldMs || LockHoldMs>MaxLockHoldMs) throw new InvalidDataException("Lock hold time must be 0.5 to 5 seconds.");
   if(LockVirtualKey<0 || LockVirtualKey>255) throw new InvalidDataException("Invalid learned Lock key.");
  }
  public static Configuration Load() {
   if(!File.Exists(FileName))return Create();
   Configuration c=new JavaScriptSerializer().Deserialize<Configuration>(File.ReadAllText(FileName));c.Validate();return c;
  }
  public Configuration Clone() {JavaScriptSerializer j=new JavaScriptSerializer();return j.Deserialize<Configuration>(j.Serialize(this));}
  public void Save() {
   Validate();Directory.CreateDirectory(Folder);
   string temp=FileName+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this));
   if(File.Exists(FileName))File.Replace(temp,FileName,FileName+".bak");else File.Move(temp,FileName);
  }
 }
 public static class Actions {
  public static readonly string[] Ids={"brightness-down","brightness-up","task-view","search","voice-typing","do-not-disturb","previous-track","play-pause","next-track","mute","volume-down","volume-up","lock","input-method","notifications","quick-settings","screenshot","calculator","show-desktop","clipboard-history","emoji","sleep","power","key","text","app","url","folder","shortcut","powershell","none"};
  public static readonly string[] Names={"Brightness down","Brightness up","Task View","Windows Search","Voice typing","Do Not Disturb","Previous track","Play / pause","Next track","Mute","Volume down","Volume up","Lock screen","Switch input language","Notification Center","Quick Settings","Screenshot / Snipping Tool","Calculator","Show desktop","Clipboard history","Emoji panel","Sleep","Power button (power plan action)","Press a key","Type text","Open an app","Open a website","Open a folder","Keyboard shortcut","Run PowerShell command","Do nothing"};
  public static bool Contains(string action) {return Array.IndexOf(Ids,action)>=0;}
  public static string Name(Binding b) {int i=Array.IndexOf(Ids,b.Action);return i<0?"Unknown":Names[i];}
 }
}







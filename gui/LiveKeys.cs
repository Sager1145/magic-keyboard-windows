using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MagicKeyboard {
 // Live key display: the tray app (the driver's only event reader) posts the keys being
 // held to the settings window while that window is open. Nothing is written anywhere,
 // and without an open window the presses go nowhere.
 //   wParam: held keys (F1-F12 bits 0-11, Lock 12, Fn 13) | (newest key + 1) << 14 | modifiers << 20
 //   lParam: up to 6 other held key usages, one per byte
 static class LiveKeys {
  public static readonly int Message=RegisterWindowMessage("OrchardKeys.LiveKeys");
  const string Mark="OrchardKeys.Settings";
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string windowClass,string title);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SetProp(IntPtr window,string name,IntPtr value);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window,string name);
  [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);

  // Called by the settings window once its handle exists.
  public static void MarkSettings(IntPtr window){SetProp(window,Mark,new IntPtr(1));}
  [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr window,out int processId);
  // The open settings window, or zero. Only a window of this same program file counts, so
  // another process can't pose as the settings window to receive key presses.
  public static IntPtr Window(){
   for(IntPtr w=FindWindowEx(IntPtr.Zero,IntPtr.Zero,null,Theme.ProductName);w!=IntPtr.Zero;w=FindWindowEx(IntPtr.Zero,w,null,Theme.ProductName))
    if(GetProp(w,Mark)!=IntPtr.Zero && SameProgram(w))return w;
   return IntPtr.Zero;
  }
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
  [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
  // Brings an open settings window to the front (restoring it if minimized). False if none.
  public static bool ShowSettings(){
   IntPtr window=Window();
   if(window==IntPtr.Zero)return false;
   if(IsIconic(window))ShowWindow(window,9); // SW_RESTORE
   SetForegroundWindow(window);
   return true;
  }
  static bool SameProgram(IntPtr window){
   int id;GetWindowThreadProcessId(window,out id);
   try{
    using(System.Diagnostics.Process p=System.Diagnostics.Process.GetProcessById(id))
     return String.Equals(p.MainModule.FileName,System.Windows.Forms.Application.ExecutablePath,StringComparison.OrdinalIgnoreCase);
   }catch(Exception){return false;}
  }
  public static void Send(IntPtr window,PhysicalKeys keys,int newest){
   long w=(long)(keys.Held&0x3FFF)|((long)((newest+1)&0x1F)<<14)|((long)keys.Mods<<20);
   long l=0;
   for(int i=0;i<6 && keys.Usages!=null && i<keys.Usages.Length;i++)l|=(long)keys.Usages[i]<<(8*i);
   PostMessage(window,Message,new IntPtr(w),new IntPtr(l));
  }
  public static PhysicalKeys Decode(IntPtr wParam,IntPtr lParam,out int newest){
   long w=wParam.ToInt64(),l=lParam.ToInt64();
   int held=(int)(w&0x3FFF);
   newest=(int)((w>>14)&0x1F)-1;
   PhysicalKeys keys=new PhysicalKeys{Row=held&0xFFF,Lock=(held&(1<<12))!=0,Fn=(held&(1<<13))!=0,Mods=(byte)(w>>20)};
   var usages=new System.Collections.Generic.List<byte>();
   for(int i=0;i<6;i++){byte u=(byte)(l>>(8*i));if(u!=0)usages.Add(u);}
   keys.Usages=usages.ToArray();
   keys.Extra=PhysicalKeys.Names(keys.Usages);
   return keys;
  }
 }
}

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MagicKeyboard {
 static class PowerActions {
  static Guid ButtonSettings=new Guid("4f971e89-eebd-4455-a8de-9e59040e7347");
  static Guid PowerButtonSetting=new Guid("7648efa3-dd9c-4e3e-b566-50f929386280");

  [DllImport("powrprof.dll")] static extern int PowerGetActiveScheme(IntPtr root,out IntPtr scheme);
  [DllImport("powrprof.dll")] static extern int PowerReadACValueIndex(IntPtr root,IntPtr scheme,ref Guid subgroup,ref Guid setting,out uint value);
  [DllImport("powrprof.dll")] static extern int PowerReadDCValueIndex(IntPtr root,IntPtr scheme,ref Guid subgroup,ref Guid setting,out uint value);
  [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr value);

  public static void Sleep(PowerState state){
   if(!Application.SetSuspendState(state,false,false))
    throw new InvalidOperationException("Windows could not enter "+state+".");
  }

  public static void PowerButton(){
   IntPtr scheme;
   int error=PowerGetActiveScheme(IntPtr.Zero,out scheme);
   if(error!=0)throw new Win32Exception(error);
   uint action;
   try {
    switch(SystemInformation.PowerStatus.PowerLineStatus){
     case PowerLineStatus.Online:
      error=PowerReadACValueIndex(IntPtr.Zero,scheme,ref ButtonSettings,ref PowerButtonSetting,out action);break;
     case PowerLineStatus.Offline:
      error=PowerReadDCValueIndex(IntPtr.Zero,scheme,ref ButtonSettings,ref PowerButtonSetting,out action);break;
     default:throw new InvalidOperationException("Windows did not report whether the computer is plugged in.");
    }
    if(error!=0)throw new Win32Exception(error);
   } finally {LocalFree(scheme);}
   switch(action){
    case 0:return;
    case 1:Sleep(PowerState.Suspend);return;
    case 2:Sleep(PowerState.Hibernate);return;
    case 3:Process.Start("shutdown.exe","/s /t 0");return;
    default:throw new InvalidOperationException("Unsupported Windows power button action "+action+".");
   }
  }
 }
}

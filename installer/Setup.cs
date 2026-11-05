using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Diagnostics;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MagicKeyboardSetup {
 // One-file installer: carries the MagicKeyboardFn driver package and the settings tray
 // as embedded resources and installs them like installer\Install-MagicKeyboardFn.ps1.
 sealed class SetupForm:Form {
  static readonly string[] Files={"MagicKeyboardFn.inf","MagicKeyboardFn.sys","magickeyboardfn.cat","MagicKeyboardFn.cer","MagicKeyboardControls.exe"};
  const string HardwareId=@"USB\VID_05AC&PID_0320&MI_01",AuxHardwareId=@"USB\VID_05AC&PID_0320&MI_00";
  const string BluetoothId=@"BTHENUM\{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0320";
  const string RunKey=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
  readonly TextBox log;readonly Button install,uninstall;readonly Label headline;

  [DllImport("newdev.dll",CharSet=CharSet.Unicode,SetLastError=true)]
  static extern bool UpdateDriverForPlugAndPlayDevices(IntPtr parent,string hardwareId,string infPath,uint flags,out bool reboot);
  [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);

  static bool Dark(){try{object v=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1);return v is int && (int)v==0;}catch(Exception){return false;}}

  public SetupForm(){
   bool dark=Dark();
   Color back=dark?Color.FromArgb(32,32,32):Color.FromArgb(243,243,243),card=dark?Color.FromArgb(43,43,43):Color.White,ink=dark?Color.FromArgb(240,240,240):Color.FromArgb(27,27,27),
    muted=dark?Color.FromArgb(160,160,160):Color.FromArgb(96,96,96),accent=dark?Color.FromArgb(96,205,255):Color.FromArgb(0,95,184),accentInk=dark?Color.Black:Color.White;
   Text="Orchard Keys Setup";try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch(Exception){}Font=new Font("Segoe UI",9.75f);BackColor=back;ForeColor=ink;Size=new Size(620,470);MinimumSize=Size;StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
   HandleCreated+=(s,e)=>{int d=dark?1:0;DwmSetWindowAttribute(Handle,20,ref d,4);};
   TableLayoutPanel page=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,18,24,18),RowCount=4,ColumnCount=1};
   page.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.RowStyles.Add(new RowStyle(SizeType.Percent,100));page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   Controls.Add(page);
   page.Controls.Add(new Label{Text="Orchard Keys",AutoSize=true,Font=new Font("Segoe UI",18,FontStyle.Bold)},0,0);
   headline=new Label{AutoSize=true,ForeColor=muted,MaximumSize=new Size(560,0),Margin=new Padding(3,4,3,12),
    Text="Installs the Fn / function-row driver for the USB-C Magic Keyboard (05AC:0320) and the settings tray. The driver is test-signed: Windows test signing must be on, which needs Secure Boot off."};
   page.Controls.Add(headline,0,1);
   log=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=card,ForeColor=ink,Font=new Font("Consolas",9)};
   page.Controls.Add(log,0,2);
   FlowLayoutPanel buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Margin=new Padding(0,12,0,0)};
   install=new Button{Text="Install",AutoSize=true,FlatStyle=FlatStyle.Flat,BackColor=accent,ForeColor=accentInk,Padding=new Padding(14,4,14,4)};install.FlatAppearance.BorderColor=accent;
   uninstall=new Button{Text="Uninstall",AutoSize=true,FlatStyle=FlatStyle.Flat,BackColor=card,ForeColor=ink,Padding=new Padding(10,4,10,4),Margin=new Padding(0,0,8,0)};
   install.Click+=(s,e)=>Run(Install);uninstall.Click+=(s,e)=>Run(Uninstall);
   buttons.Controls.Add(install);buttons.Controls.Add(uninstall);page.Controls.Add(buttons,0,3);
   Log("Ready. Connect the keyboard by USB, then choose Install.");
  }

  void Log(string text){if(InvokeRequired){BeginInvoke((Action)(()=>Log(text)));return;}log.AppendText(text+Environment.NewLine);}
  void Run(Action work){
   install.Enabled=uninstall.Enabled=false;
   Task.Run(()=>{try{work();}catch(Exception e){Log("Failed: "+e.Message);}})
    .ContinueWith(t=>BeginInvoke((Action)(()=>{install.Enabled=uninstall.Enabled=true;})));
  }
  static string Exec(string file,string arguments,out int exit){
   using(Process p=Process.Start(new ProcessStartInfo(file,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){
    Task<string> output=p.StandardOutput.ReadToEndAsync(),error=p.StandardError.ReadToEndAsync();p.WaitForExit();exit=p.ExitCode;return output.Result+error.Result;
   }
  }
  static bool TestSigning(){int exit;return Regex.IsMatch(Exec("bcdedit.exe","/enum {current}",out exit),@"(?mi)^testsigning\s+Yes");}
  static bool SecureBoot(){try{object v=Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecureBoot\State","UEFISecureBootEnabled",0);return v is int && (int)v==1;}catch(Exception){return false;}}

  void Install(){
   if(!TestSigning()){
    if(SecureBoot()){Log("Secure Boot is on, so Windows won't load a test-signed driver. Turn Secure Boot off in your PC's firmware settings, then run Setup again.");return;}
    if((DialogResult)Invoke((Func<DialogResult>)(()=>MessageBox.Show(this,"Windows test signing is off. Turn it on now? You'll need to restart Windows and run Setup again.","Orchard Keys Setup",MessageBoxButtons.OKCancel,MessageBoxIcon.Question)))!=DialogResult.OK){Log("Cancelled: test signing is required.");return;}
    int code;Log(Exec("bcdedit.exe","/set testsigning on",out code).Trim());
    Log(code==0?"Test signing turned on. Restart Windows, then run Setup again.":"bcdedit failed ("+code+").");
    return;
   }
   // Extract into a new folder only administrators and SYSTEM can write, so nothing else
   // can swap the package between extraction and installation.
   string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"OrchardKeysSetup-"+Guid.NewGuid().ToString("N"));
   DirectorySecurity acl=new DirectorySecurity();
   acl.SetAccessRuleProtection(true,false);
   foreach(WellKnownSidType sid in new[]{WellKnownSidType.BuiltinAdministratorsSid,WellKnownSidType.LocalSystemSid})
    acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
   Directory.CreateDirectory(folder,acl);
   try{InstallFrom(folder);}
   finally{try{Directory.Delete(folder,true);}catch(Exception){}}
  }

  static byte[] Resource(string name){
   using(Stream source=typeof(SetupForm).Assembly.GetManifestResourceStream("pkg/"+name))
   using(MemoryStream copy=new MemoryStream()){source.CopyTo(copy);return copy.ToArray();}
  }

  void InstallFrom(string folder){
   foreach(string name in Files)File.WriteAllBytes(Path.Combine(folder,name),Resource(name));
   Log("Extracted the driver package.");

   X509Certificate2 certificate=new X509Certificate2(Resource("MagicKeyboardFn.cer"));
   foreach(StoreName store in new[]{StoreName.Root,StoreName.TrustedPublisher}){
    X509Store s=new X509Store(store,StoreLocation.LocalMachine);s.Open(OpenFlags.ReadWrite);s.Add(certificate);s.Close();
   }
   Log("Trusted the driver signing certificate ("+certificate.Subject+").");

   foreach(Process p in Process.GetProcessesByName("MagicKeyboardControls")){try{p.Kill();p.WaitForExit(3000);}catch(Exception){}}
   string inf=Path.Combine(folder,"MagicKeyboardFn.inf");
   int exit;string output=Exec("pnputil.exe","/add-driver \""+inf+"\"",out exit);
   if(exit!=0 && exit!=3010)throw new InvalidOperationException("pnputil /add-driver failed ("+exit+"):\r\n"+output);
   Log("Added the driver package to Windows.");

   bool reboot=false;
   if(UpdateDriverForPlugAndPlayDevices(IntPtr.Zero,HardwareId,inf,1,out reboot))Log("Installed the driver on the connected keyboard.");
   else{
    int error=Marshal.GetLastWin32Error();
    if(error==unchecked((int)0xE000020B))Log("No keyboard connected. The driver installs automatically when you connect it.");
    else throw new Win32Exception(error);
   }
   // Interface 0 carries the battery report; it gets the Windows HID driver from the same package.
   bool auxReboot;
   if(UpdateDriverForPlugAndPlayDevices(IntPtr.Zero,AuxHardwareId,inf,1,out auxReboot)){reboot|=auxReboot;Log("Enabled the keyboard's battery reporting.");}
   bool btReboot;
   if(UpdateDriverForPlugAndPlayDevices(IntPtr.Zero,BluetoothId,inf,1,out btReboot)){reboot|=btReboot;Log("Installed the driver on the Bluetooth connection.");}
   try{Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"MagicKeyboardControls"),true);}catch(Exception){}
   if(reboot && RestartKeyboards()){reboot=false;Log("Restarted the keyboard so the new driver is loaded.");}
   if(reboot)Log("Restart Windows to finish: the previous version stays loaded until then.");
   else{
    Log("Done. Default mappings are active now.");
    string tray=Registry.GetValue(@"HKEY_LOCAL_MACHINE\"+RunKey,"MagicKeyboardControls",null) as string;
    Match path=tray==null?Match.Empty:Regex.Match(tray,"^\"([^\"]+)\"");
    // explorer.exe starts it as the signed-in user, not elevated.
    if(path.Success)Process.Start("explorer.exe","\""+path.Groups[1].Value+"\"");
   }
  }

  // Windows keeps the previous driver loaded while any keyboard device uses it (a paired
  // Bluetooth keyboard stays present even when switched off). Restarting each keyboard
  // device loads the new one; false if Windows refuses, which means a restart is needed.
  static bool RestartKeyboards(){
   int exit;string list=Exec("pnputil.exe","/enum-devices /connected",out exit);
   var ids=Regex.Matches(list,@"(USB\\VID_05AC&PID_0320&MI_0\d\\\S+|BTHENUM\\\{00001124-0000-1000-8000-00805F9B34FB\}_VID&0001004C_PID&0320\\\S+)",RegexOptions.IgnoreCase)
    .Cast<Match>().Select(m=>m.Value).Distinct().ToList();
   if(ids.Count==0)return false;
   bool all=true;
   foreach(string id in ids){Exec("pnputil.exe","/restart-device \""+id+"\"",out exit);if(exit!=0)all=false;}
   return all;
  }

  void Uninstall(){
   foreach(Process p in Process.GetProcessesByName("MagicKeyboardControls")){try{p.Kill();p.WaitForExit(3000);}catch(Exception){}}
   int exit;string list=Exec("pnputil.exe","/enum-drivers",out exit);
   string published=null;bool removed=false;
   foreach(string line in list.Split('\n')){
    Match oem=Regex.Match(line,@":\s+(oem\d+\.inf)\s*$");
    if(oem.Success){published=oem.Groups[1].Value;continue;}
    if(published!=null && Regex.IsMatch(line,@":\s+magickeyboardfn\.inf\s*$",RegexOptions.IgnoreCase)){
     Log(Exec("pnputil.exe","/delete-driver "+published+" /uninstall /force",out exit).Trim());removed=true;published=null;
    }
   }
   using(RegistryKey run=Registry.LocalMachine.OpenSubKey(RunKey,true)){if(run!=null)run.DeleteValue("MagicKeyboardControls",false);}
   // Stop trusting the test certificate Install added.
   X509Certificate2 certificate=new X509Certificate2(Resource("MagicKeyboardFn.cer"));
   foreach(StoreName store in new[]{StoreName.Root,StoreName.TrustedPublisher}){
    X509Store s=new X509Store(store,StoreLocation.LocalMachine);s.Open(OpenFlags.ReadWrite);s.Remove(certificate);s.Close();
   }
   Log("Removed the driver signing certificate from the trusted stores.");
   Exec("pnputil.exe","/scan-devices",out exit);
   Log(removed?"Removed. The keyboard is back on the standard Windows driver.":"No Magic Keyboard driver package was installed.");
   if(TestSigning())Log("Windows test signing is still on. To turn it off, run \"bcdedit /set testsigning off\" as administrator and restart.");
  }

  [STAThread] static void Main(){Application.EnableVisualStyles();Application.Run(new SetupForm());}
 }
}

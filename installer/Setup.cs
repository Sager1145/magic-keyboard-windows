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

  bool Ask(string text){return (DialogResult)Invoke((Func<DialogResult>)(()=>MessageBox.Show(this,text,"Orchard Keys Setup",MessageBoxButtons.YesNo,MessageBoxIcon.Question)))==DialogResult.Yes;}
  static void Restart(string arguments){int code;Exec("shutdown.exe",arguments,out code);}

  // Setup finishes the install by itself after the restart test signing needs: a copy in an
  // admin-only folder runs once at the next sign-in with /resume.
  const string RunOnceKey=@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
  static void ResumeAfterRestart(){
   string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"OrchardKeys");
   Directory.CreateDirectory(folder,AdminOnly());
   string copy=Path.Combine(folder,"OrchardKeysSetup.exe");
   File.Copy(Application.ExecutablePath,copy,true);
   using(RegistryKey once=Registry.LocalMachine.CreateSubKey(RunOnceKey))once.SetValue("OrchardKeysSetup","\""+copy+"\" /resume");
  }
  static DirectorySecurity AdminOnly(){
   DirectorySecurity acl=new DirectorySecurity();
   acl.SetAccessRuleProtection(true,false);
   foreach(WellKnownSidType sid in new[]{WellKnownSidType.BuiltinAdministratorsSid,WellKnownSidType.LocalSystemSid})
    acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
   return acl;
  }

  void Install(){
   if(!TestSigning()){
    if(SecureBoot()){
     // Secure Boot locks test signing off; only the PC's firmware settings can change it.
     Log("Secure Boot is on, so Windows can't load a test-signed driver.");
     Log("Turn Secure Boot off in your PC's firmware (UEFI) settings, usually under Security or Boot, save, and start Windows. Then run Setup again.");
     if(Ask("Secure Boot is on, so this driver can't load.\n\nRestart into your PC's firmware settings now to turn Secure Boot off?\n\nSave your work first."))
     {Log("Restarting into the firmware settings...");Restart("/r /fw /t 5");}
     return;
    }
    if(!Ask("Windows test signing is off. The driver is test-signed, so Windows loads it only with test signing on.\n\nTurn it on now? Setup will finish the install after Windows restarts.")){Log("Cancelled: test signing is required.");return;}
    int code;Log(Exec("bcdedit.exe","/set testsigning on",out code).Trim());
    if(code!=0){Log("bcdedit failed ("+code+").");return;}
    ResumeAfterRestart();
    Log("Test signing turned on. Setup finishes the install after Windows restarts.");
    if(Ask("Restart Windows now to finish installing?\n\nSave your work first.")){Log("Restarting...");Restart("/r /t 5");}
    else Log("Restart Windows when you're ready; Setup continues after you sign in.");
    return;
   }
   // Extract into a new folder only administrators and SYSTEM can write, so nothing else
   // can swap the package between extraction and installation.
   string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"OrchardKeysSetup-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(folder,AdminOnly());
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
   // Start menu entry "Orchard Keys", pointing at this version (driver store paths change per version).
   try{
    string app=InstalledApp(Resource("MagicKeyboardControls.exe"));
    if(app!=null){CreateShortcut(app);Log("Added Orchard Keys to the Start menu.");}
   }catch(Exception e){Log("Start menu entry not added: "+e.Message);}
   if(reboot && RestartKeyboards()){reboot=false;Log("Restarted the keyboard so the new driver is loaded.");}
   if(reboot){
    Log("Restart Windows to finish: the previous version stays loaded until then.");
    if(Ask("Windows needs a restart to load the new driver (the previous version is still in use).\n\nRestart now?\n\nSave your work first.")){Log("Restarting...");Restart("/r /t 5");}
    return;
   }
   if(!DriverRunning(InstalledApp(Resource("MagicKeyboardControls.exe")))) Log("The driver isn't running yet: connect the keyboard (cable or Bluetooth), or restart Windows.");
   else{
    Log("Done. Default mappings are active now.");
    string tray=Registry.GetValue(@"HKEY_LOCAL_MACHINE\"+RunKey,"MagicKeyboardControls",null) as string;
    Match path=tray==null?Match.Empty:Regex.Match(tray,"^\"([^\"]+)\"");
    // explorer.exe starts it as the signed-in user, not elevated.
    if(path.Success)Process.Start("explorer.exe","\""+path.Groups[1].Value+"\"");
   }
  }

  // TRUE when the loaded MagicKeyboardFn.sys is the one from this package's driver store folder.
  [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int type,IntPtr buffer,int length,out int needed);
  static bool DriverRunning(string app){
   if(app==null)return false;
   string folder=Path.GetFileName(Path.GetDirectoryName(app));
   int length=1<<20,needed;IntPtr buffer=Marshal.AllocHGlobal(length);
   try{
    if(NtQuerySystemInformation(11,buffer,length,out needed)!=0)return false; // SystemModuleInformation
    int count=Marshal.ReadInt32(buffer);
    for(int i=0;i<count;i++){
     byte[] path=new byte[256];Marshal.Copy(IntPtr.Add(buffer,8+i*296+40),path,0,256);
     string name=System.Text.Encoding.ASCII.GetString(path).TrimEnd('\0');
     if(name.EndsWith("MagicKeyboardFn.sys",StringComparison.OrdinalIgnoreCase))return name.IndexOf(folder,StringComparison.OrdinalIgnoreCase)>=0;
    }
    return false;
   }finally{Marshal.FreeHGlobal(buffer);}
  }

  // Start menu entry that opens the settings window (which also restarts the tray app).
  static readonly string Shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),"Orchard Keys.lnk");

  // The installed copy of the settings app: the driver store folder holding this exe.
  static string InstalledApp(byte[] app){
   string store=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\DriverStore\FileRepository");
   using(var sha=System.Security.Cryptography.SHA256.Create()){
    string want=Convert.ToBase64String(sha.ComputeHash(app));
    foreach(string dir in Directory.GetDirectories(store,"magickeyboardfn.inf_amd64_*")){
     string exe=Path.Combine(dir,"MagicKeyboardControls.exe");
     if(File.Exists(exe) && Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(exe)))==want)return exe;
    }
   }
   return null;
  }

  // WScript.Shell via late binding, so Setup needs no extra references.
  static void CreateShortcut(string target){
   Type shellType=Type.GetTypeFromProgID("WScript.Shell");
   object shell=Activator.CreateInstance(shellType);
   object link=shellType.InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,new object[]{Shortcut});
   Type linkType=link.GetType();
   Action<string,object> set=(name,value)=>linkType.InvokeMember(name,System.Reflection.BindingFlags.SetProperty,null,link,new[]{value});
   set("TargetPath",target);set("WorkingDirectory",Path.GetDirectoryName(target));set("IconLocation",target+",0");
   set("Description","Customize the Magic Keyboard keys (also restarts the tray app)");
   linkType.InvokeMember("Save",System.Reflection.BindingFlags.InvokeMethod,null,link,null);
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
   try{File.Delete(Shortcut);}catch(Exception){}
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

  // /resume: started once at sign-in after the restart test signing needed; installs right away.
  [STAThread] static void Main(string[] args){
   Application.EnableVisualStyles();
   SetupForm form=new SetupForm();
   if(args.Contains("/resume",StringComparer.OrdinalIgnoreCase))form.Shown+=(s,e)=>{form.Log("Continuing the install after the restart.");form.Run(form.Install);};
   Application.Run(form);
  }
 }
}

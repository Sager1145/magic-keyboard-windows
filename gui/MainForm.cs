using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MagicKeyboard {
 // Settings editor. Every change is saved and sent to the MagicKeyboardFn driver right
 // away; the driver runs the mappings, so they keep working when this window closes.
 public sealed class MainForm : Form {
  sealed class ActionItem {public string Id,Text;public bool Header;public override string ToString(){return Text;}}
  // Action picker contents: category headers followed by their actions.
  static readonly string[][] Categories={
   new[]{"","none"},
   new[]{"Display & sound","brightness-down","brightness-up","mute","volume-down","volume-up"},
   new[]{"Media","previous-track","play-pause","next-track"},
   new[]{"Windows","task-view","search","voice-typing","do-not-disturb","notifications","quick-settings","input-method","show-desktop","screenshot","clipboard-history","emoji","calculator","lock"},
   new[]{"Power","sleep","power"},
   new[]{"Custom","key","shortcut","text","app","url","folder","powershell"}
  };
  static readonly Font Mono=new Font("Consolas",10);

  Configuration draft;ActionRunner runner;
  KeyboardRow keyboard;Segmented mode,press;ComboBox action;TextBox value;Button valueButton,tryIt,reset;
  Label title,subtitle,valueLabel,hint,error,status,lastKey,driverPill,trayPill;
  TableLayoutPanel editor;
  bool filling;int key,gesture,lastAction=-1;
  readonly System.Windows.Forms.Timer statusPoll=new System.Windows.Forms.Timer{Interval=1000};
  readonly System.Windows.Forms.Timer apply=new System.Windows.Forms.Timer{Interval=450};

  public MainForm() {
   try{draft=Configuration.Load();}catch(Exception e){MessageBox.Show("Saved profile could not be read. Defaults will be used.\n"+e.Message,"Magic Keyboard");draft=Configuration.Create();}
   Text="Magic Keyboard";Font=new Font("Segoe UI",9.75f);BackColor=Theme.Back;ForeColor=Theme.Ink;
   MinimumSize=new Size(900,620);Size=new Size(980,660);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
   DoubleBuffered=true;
   try{Icon=Theme.TrayIcon();}catch(Exception){}
   BuildUi();Select(0,0);
   apply.Tick+=(s,e)=>{apply.Stop();Apply();};
   HandleCreated+=(s,e)=>Theme.TitleBar(this);
   Load+=(s,e)=>{
    runner=new ActionRunner(SetStatus);
    try{AgentWindow.EnsureRunning();}catch(Exception){}
    statusPoll.Tick+=(a,b)=>PollStatus();statusPoll.Start();PollStatus();
   };
   FormClosing+=(s,e)=>{if(apply.Enabled){apply.Stop();Apply();}};
  }

  static Label MakeLabel(string text,float size,FontStyle style,Color color){return new Label{Text=text,AutoSize=true,Font=new Font("Segoe UI",size,style),ForeColor=color,BackColor=Color.Transparent};}
  static Label Pill(){return new Label{AutoSize=true,Padding=new Padding(10,4,10,4),Margin=new Padding(10,8,0,0),Font=new Font("Segoe UI",8.75f,FontStyle.Bold)};}
  static Button FlatButton(string text,bool primary){
   Button b=new Button{Text=text,AutoSize=true,FlatStyle=FlatStyle.Flat,Padding=new Padding(10,3,10,3),Margin=new Padding(0,0,8,0),Cursor=Cursors.Hand,
    BackColor=primary?Theme.Accent:Theme.Raised,ForeColor=primary?Theme.AccentInk:Theme.Ink};
   b.FlatAppearance.BorderColor=primary?Theme.Accent:Theme.Line;b.FlatAppearance.MouseOverBackColor=primary?ControlPaint.Light(Theme.Accent,0.15f):Theme.Hover;
   return b;
  }
  static Panel Card(Control content){
   Panel card=new Panel{Dock=DockStyle.Fill,BackColor=Theme.Card,Padding=new Padding(20,16,20,16),Margin=new Padding(0,0,0,12)};
   card.Paint+=(s,e)=>{using(Pen p=new Pen(Theme.Line))e.Graphics.DrawRectangle(p,0,0,card.Width-1,card.Height-1);};
   content.Dock=DockStyle.Fill;card.Controls.Add(content);return card;
  }

  void BuildUi() {
   TableLayoutPanel page=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,18,24,12),ColumnCount=1,RowCount=4,BackColor=Theme.Back};
   page.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   page.RowStyles.Add(new RowStyle(SizeType.Percent,100));page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   Controls.Add(page);

   // Header: name, live status, top-row mode
   TableLayoutPanel header=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,Margin=new Padding(0,0,0,12)};
   header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,330));
   FlowLayoutPanel name=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=new Padding(0)};
   name.Controls.Add(MakeLabel("Magic Keyboard",20,FontStyle.Bold,Theme.Ink));
   driverPill=Pill();trayPill=Pill();name.Controls.Add(driverPill);name.Controls.Add(trayPill);
   header.Controls.Add(name,0,0);
   mode=new Segmented{Items=new[]{"F1–F12 first","Actions first"},Dock=DockStyle.Top,Margin=new Padding(0,6,0,0),Font=Font};
   mode.Selected=draft.RequireFn?0:1;
   mode.SelectedChanged+=(s,e)=>{if(filling)return;draft.RequireFn=mode.Selected==0;Select(key,gesture);Changed();};
   header.Controls.Add(mode,1,0);
   page.Controls.Add(header,0,0);

   // Keyboard: the top row plus fn, as clickable keycaps
   keyboard=new KeyboardRow{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,12),Font=Font};
   keyboard.Bind(k=>Theme.Glyph(KeyCapAction(k)),k=>KeyName(k)+": "+Summary(k));
   keyboard.SelectedChanged+=(s,e)=>Select(keyboard.SelectedKey,0);
   page.Controls.Add(keyboard,0,1);

   // Editor card
   editor=new TableLayoutPanel{ColumnCount=1,RowCount=10,BackColor=Theme.Card};
   foreach(SizeType t in new[]{SizeType.AutoSize,SizeType.AutoSize,SizeType.AutoSize,SizeType.AutoSize,SizeType.AutoSize,SizeType.AutoSize,SizeType.Absolute,SizeType.AutoSize,SizeType.Percent,SizeType.AutoSize})
    editor.RowStyles.Add(t==SizeType.Percent?new RowStyle(SizeType.Percent,100):t==SizeType.Absolute?new RowStyle(SizeType.Absolute,34):new RowStyle(t));
   TableLayoutPanel titleRow=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,ColumnCount=2,Margin=new Padding(0)};
   titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,330));
   title=MakeLabel("F1",18,FontStyle.Bold,Theme.Ink);titleRow.Controls.Add(title,0,0);
   press=new Segmented{Items=new[]{"Tap","Double tap","Hold"},Dock=DockStyle.Top,Margin=new Padding(0,4,0,0),Font=Font};
   press.SelectedChanged+=(s,e)=>{if(!filling)Select(key,press.Selected);};
   titleRow.Controls.Add(press,1,0);
   editor.Controls.Add(titleRow,0,0);
   subtitle=MakeLabel("",9.75f,FontStyle.Regular,Theme.Muted);subtitle.Margin=new Padding(3,2,3,14);editor.Controls.Add(subtitle,0,1);
   editor.Controls.Add(MakeLabel("Action",9.75f,FontStyle.Bold,Theme.Ink),0,2);
   action=new ComboBox{Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=26,MaxDropDownItems=16,Margin=new Padding(3,4,3,12),FlatStyle=FlatStyle.Flat,BackColor=Theme.Raised,ForeColor=Theme.Ink};
   foreach(string[] category in Categories){
    if(category[0].Length>0)action.Items.Add(new ActionItem{Text=category[0],Header=true});
    for(int i=1;i<category.Length;i++)action.Items.Add(new ActionItem{Id=category[i],Text=Actions.Name(new Binding(category[i]))});
   }
   action.DrawItem+=DrawAction;
   action.SelectedIndexChanged+=(s,e)=>{
    if(action.SelectedIndex<0)return;
    ActionItem item=(ActionItem)action.Items[action.SelectedIndex];
    if(item.Header){action.SelectedIndex=Math.Min(action.Items.Count-1,action.SelectedIndex+(action.SelectedIndex<lastAction?-1:1));return;}
    lastAction=action.SelectedIndex;
    if(filling)return;
    value.Text="";UpdateEditor();Edited();
   };
   editor.Controls.Add(action,0,3);
   valueLabel=MakeLabel("",9.75f,FontStyle.Bold,Theme.Ink);editor.Controls.Add(valueLabel,0,5);
   TableLayoutPanel valueRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=new Padding(0,4,0,0),BackColor=Theme.Card};
   valueRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));valueRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
   value=new TextBox{Dock=DockStyle.Fill,BorderStyle=BorderStyle.FixedSingle,BackColor=Theme.Raised,ForeColor=Theme.Ink};value.TextChanged+=(s,e)=>{if(!filling)Edited();};
   valueButton=FlatButton("",false);valueButton.Margin=new Padding(6,0,3,0);valueButton.Click+=(s,e)=>ValueButton();
   valueRow.Controls.Add(value,0,0);valueRow.Controls.Add(valueButton,1,0);editor.Controls.Add(valueRow,0,6);
   error=MakeLabel("",9,FontStyle.Regular,Theme.Bad);error.MaximumSize=new Size(800,0);editor.Controls.Add(error,0,7);
   hint=MakeLabel("",9,FontStyle.Regular,Theme.Muted);hint.MaximumSize=new Size(800,0);hint.Margin=new Padding(3,12,3,0);editor.Controls.Add(hint,0,4);
   FlowLayoutPanel buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,BackColor=Theme.Card};
   tryIt=FlatButton("Try it",true);tryIt.Click+=(s,e)=>TryAction();
   reset=FlatButton("Use default",false);reset.Click+=(s,e)=>{draft.SetBinding(key,gesture,Configuration.DefaultBinding(key,gesture));Select(key,gesture);keyboard.Invalidate();Changed();};
   buttons.Controls.AddRange(new Control[]{tryIt,reset});editor.Controls.Add(buttons,0,9);
   page.Controls.Add(Card(editor),0,2);

   // Footer: save state, last key, restore all
   TableLayoutPanel footer=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=3,Margin=new Padding(0)};
   footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
   status=new Label{AutoEllipsis=true,Dock=DockStyle.Fill,ForeColor=Theme.Muted,Text="Changes are saved to the keyboard as you make them.",TextAlign=ContentAlignment.MiddleLeft};
   lastKey=new Label{AutoEllipsis=true,Dock=DockStyle.Fill,ForeColor=Theme.Muted,TextAlign=ContentAlignment.MiddleLeft};
   LinkLabel restore=new LinkLabel{Text="Restore all defaults",AutoSize=true,LinkColor=Theme.Accent,ActiveLinkColor=Theme.Accent,Margin=new Padding(3,6,3,3)};
   restore.LinkClicked+=(s,e)=>{
    if(MessageBox.Show(this,"Reset every key to its default action?","Magic Keyboard",MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK)return;
    draft=Configuration.Create();filling=true;mode.Selected=0;filling=false;Select(0,0);keyboard.Invalidate();Changed();
   };
   footer.Controls.Add(status,0,0);footer.Controls.Add(lastKey,1,0);footer.Controls.Add(restore,2,0);
   page.Controls.Add(footer,0,3);
  }

  void DrawAction(object sender,DrawItemEventArgs e){
   if(e.Index<0)return;
   ActionItem item=(ActionItem)action.Items[e.Index];
   bool inEdit=(e.State&DrawItemState.ComboBoxEdit)!=0;
   bool selected=(e.State&DrawItemState.Selected)!=0 && !item.Header && !inEdit;
   using(SolidBrush back=new SolidBrush(selected?Theme.Accent:Theme.Raised))e.Graphics.FillRectangle(back,e.Bounds);
   Color color=item.Header?Theme.Muted:selected?Theme.AccentInk:Theme.Ink;
   if(item.Header){
    using(Font bold=new Font(e.Font.FontFamily,8.25f,FontStyle.Bold))
     TextRenderer.DrawText(e.Graphics,item.Text.ToUpperInvariant(),bold,new Rectangle(e.Bounds.X+8,e.Bounds.Y,e.Bounds.Width-8,e.Bounds.Height),color,TextFormatFlags.VerticalCenter|TextFormatFlags.Bottom);
    return;
   }
   int x=e.Bounds.X+(inEdit?6:14);
   using(Font icon=new Font(Theme.IconFamily,10.5f))
    TextRenderer.DrawText(e.Graphics,Theme.Glyph(item.Id),icon,new Rectangle(x,e.Bounds.Y,22,e.Bounds.Height),color,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.NoPadding);
   TextRenderer.DrawText(e.Graphics,item.Text,e.Font,new Rectangle(x+28,e.Bounds.Y,e.Bounds.Width-x-28,e.Bounds.Height),color,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
  }

  // Keys ------------------------------------------------------------------
  static string KeyName(int k){return k<12?"F"+(k+1):k==13?"Fn":"Lock";}
  // The keycap shows the action people use most: the row action, Fn tap, Lock hold (or tap).
  string KeyCapAction(int k){
   if(k<12)return draft.Keys[k].Action;
   if(k==12)return draft.GetBinding(12,2).Action!="none"?draft.GetBinding(12,2).Action:draft.Keys[12].Action;
   return draft.Keys[13].Action;
  }
  string Summary(int k){
   if(k<12)return Actions.Name(draft.Keys[k]);
   string[] names={"Tap","Double tap","Hold"};
   var parts=new List<string>();
   for(int g=0;g<3;g++){Binding b=draft.GetBinding(k,g);if(b.Action!="none")parts.Add(names[g]+" · "+Actions.Name(b));}
   return parts.Count==0?"Do nothing":String.Join("  ·  ",parts);
  }

  // Editor -----------------------------------------------------------------
  void Select(int k,int g){
   key=k;gesture=g;
   Binding b=draft.GetBinding(k,g);
   filling=true;
   keyboard.SelectedKey=k;
   press.Visible=k>=12;press.Selected=g;
   title.Text=k<12?"F"+(k+1):k==13?"Fn":"Lock";
   string when=k<12?(draft.RequireFn?"Pressed while holding Fn. On its own it's F"+(k+1)+".":"Pressed on its own. Hold Fn for F"+(k+1)+".")
    :k==13?(g==0?"Tap Fn by itself.":g==1?"Tap Fn twice quickly.":"Hold Fn, then release without pressing another key.")
    :(g==0?"Tap Lock.":g==1?"Tap Lock twice quickly.":"Hold Lock for 1 second.");
   subtitle.Text=when+"  Default: "+Actions.Name(Configuration.DefaultBinding(k,g))+".";
   int index=-1;
   for(int i=0;i<action.Items.Count;i++){ActionItem item=(ActionItem)action.Items[i];if(!item.Header&&item.Id==b.Action){index=i;break;}}
   lastAction=index;action.SelectedIndex=index;
   value.Text=b.Value;
   filling=false;
   UpdateEditor();
  }
  string ActionId(){return action.SelectedIndex<0?"none":((ActionItem)action.Items[action.SelectedIndex]).Id;}
  void UpdateEditor(){
   string id=ActionId();
   string label=id=="shortcut"?"Key combination":id=="key"?"Key":id=="text"?"Text to type":id=="app"?"Application":id=="url"?"Website address":id=="folder"?"Folder":id=="powershell"?"PowerShell command":null;
   bool hasValue=label!=null;
   valueLabel.Text=label??"";valueLabel.Visible=value.Visible=hasValue;
   bool multiline=id=="powershell";
   value.Multiline=multiline;value.AcceptsReturn=multiline;value.ScrollBars=multiline?ScrollBars.Vertical:ScrollBars.None;
   value.Font=multiline?Mono:Font;
   editor.RowStyles[6].Height=!hasValue?0:multiline?130:34;
   string button=id=="shortcut"||id=="key"?"Record…":id=="app"||id=="folder"?"Browse…":null;
   valueButton.Visible=button!=null;if(button!=null)valueButton.Text=button;
   tryIt.Enabled=id!="none";
   hint.Text=Hint(new Binding(id,value.Text));
   ShowError(null);
  }
  static string Hint(Binding b){
   if(b.Action=="none")return "The key does nothing.";
   byte type,mods;ushort usage;
   try{Driver.Translate(b,out type,out mods,out usage);}catch(ArgumentException){return "";}
   if(type!=Driver.App)return "Handled by the keyboard driver. Works even when this app isn't running.";
   if(usage!=0||mods!=0){
    string basic=b.Action=="search"?"opens Search":b.Action=="voice-typing"?"opens voice typing":"opens Notification Center";
    return "Press again to turn it off. Toggling uses the Magic Keyboard tray app; without it, the driver still "+basic+".";
   }
   return "Runs through the Magic Keyboard tray app, which starts when you sign in.";
  }
  void ShowError(string text){error.Text=text??"";error.Visible=!String.IsNullOrEmpty(text);}
  void ValueButton(){
   string id=ActionId();
   if(id=="shortcut"||id=="key"){using(Recorder r=new Recorder(id=="key")){if(r.ShowDialog(this)==DialogResult.OK)value.Text=r.Result;}return;}
   if(id=="app"){using(OpenFileDialog d=new OpenFileDialog{Filter="Programs|*.exe;*.lnk;*.bat;*.cmd|All files|*.*"}){if(d.ShowDialog(this)==DialogResult.OK)value.Text=d.FileName;}return;}
   if(id=="folder"){using(FolderBrowserDialog d=new FolderBrowserDialog()){if(d.ShowDialog(this)==DialogResult.OK)value.Text=d.SelectedPath;}}
  }
  static void ValidateBinding(Binding b){
   if(b.Action=="shortcut")Shortcut.Parse(b.Value);
   if(b.Action=="key")Shortcut.SingleKey(b.Value);
   if((b.Action=="powershell"||b.Action=="text"||b.Action=="app"||b.Action=="url"||b.Action=="folder")&&String.IsNullOrWhiteSpace(b.Value))throw new ArgumentException("Enter a value for this action.");
  }
  void TryAction(){
   Binding b=new Binding(ActionId(),value.Text);
   try{ValidateBinding(b);}catch(ArgumentException e){ShowError(e.Message);return;}
   if((b.Action=="power"||b.Action=="sleep"||b.Action=="lock") && MessageBox.Show(this,Actions.Name(b)+" now?","Try action",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
   runner.Execute(key,b);
  }

  // Saving -----------------------------------------------------------------
  void Edited(){
   Binding b=new Binding(ActionId(),value.Text);
   draft.SetBinding(key,gesture,b);
   keyboard.Invalidate();
   hint.Text=Hint(b);
   try{ValidateBinding(b);ShowError(null);}catch(ArgumentException e){ShowError(e.Message);apply.Stop();status.ForeColor=Theme.Muted;status.Text="Not saved yet · finish this key's setting.";return;}
   Changed();
  }
  void Changed(){apply.Stop();apply.Start();status.ForeColor=Theme.Muted;status.Text="Saving…";}
  void Apply(){
   try{draft.Validate();draft.Save();}catch(Exception e){status.ForeColor=Theme.Bad;status.Text="Not saved · "+e.Message;return;}
   try{Driver.Apply(draft.Clone());status.ForeColor=Theme.Good;status.Text="✓ Saved to the keyboard";}
   catch(Exception){status.ForeColor=Theme.Muted;status.Text="✓ Saved · applies when the keyboard is connected";}
  }
  void PollStatus(){
   bool driver;
   try{using(Driver.Open(0x80000000)){}driver=true;}catch(Exception){driver=false;}
   SetPill(driverPill,driver,driver?"Keyboard connected":"Keyboard not connected");
   bool tray=AgentWindow.Running();
   SetPill(trayPill,tray,tray?"Tray on":"Tray off");
   if(!tray)return;
   string[] lines;
   try{lines=File.ReadAllText(AgentWindow.StatusFile).Split('\n');}catch(IOException){return;}catch(UnauthorizedAccessException){return;}
   if(lines.Length>=3 && lines[2].StartsWith("Fn driver · "))lastKey.Text="Last key: "+lines[2].Substring("Fn driver · ".Length);
  }
  static void SetPill(Label pill,bool ok,string text){
   pill.Text=(ok?"● ":"○ ")+text;
   pill.ForeColor=ok?Theme.Good:Theme.Muted;
   pill.BackColor=Theme.Raised;
  }
  void SetStatus(string text){if(IsDisposed)return;if(InvokeRequired){BeginInvoke((Action)(()=>SetStatus(text)));return;}status.ForeColor=Theme.Muted;status.Text=text;}
  protected override void Dispose(bool disposing){if(disposing){statusPoll.Dispose();apply.Dispose();}base.Dispose(disposing);}
 }
 sealed class Recorder:Form {
  public string Result="";
  [System.Runtime.InteropServices.DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
  public Recorder(bool single=false){Text=single?"Record key":"Record key combination";Size=new Size(450,170);StartPosition=FormStartPosition.CenterParent;KeyPreview=true;BackColor=Theme.Card;ForeColor=Theme.Ink;HandleCreated+=(s,e)=>Theme.TitleBar(this);Controls.Add(new Label{Dock=DockStyle.Fill,Text=single?"Press a single key.\nExample: K, F5, Escape\nEscape cancels.":"Press modifiers and one key.\nExample: Ctrl+Alt+K\nEscape cancels.",Padding=new Padding(20),Font=new Font("Segoe UI",11)});KeyDown+=(s,e)=>{e.SuppressKeyPress=true;if(e.KeyCode==Keys.Escape){DialogResult=DialogResult.Cancel;return;}if(new[]{Keys.ControlKey,Keys.ShiftKey,Keys.Menu,Keys.LWin,Keys.RWin}.Contains(e.KeyCode))return;if(single){Result=e.KeyCode.ToString();DialogResult=DialogResult.OK;return;}string v="";if(e.Control)v+="Ctrl+";if(e.Alt)v+="Alt+";if(e.Shift)v+="Shift+";if(GetAsyncKeyState(0x5B)<0||GetAsyncKeyState(0x5C)<0)v+="Win+";Result=v+e.KeyCode;DialogResult=DialogResult.OK;};}
 }
 static class Program {
  [STAThread]static void Main(string[] args){
   if(args.Contains("--driver-test")){SelfTest.RunDriver();return;}
   if(args.Contains("--self-test")){SelfTest.Run();return;}
   if(args.Contains("--agent") || args.Contains("--background")){
    using(Mutex agent=new Mutex(false,AgentWindow.MutexName)){
     try{if(!agent.WaitOne(0))return;}catch(AbandonedMutexException){}
     Application.EnableVisualStyles();Application.Run(new AgentWindow(args.Contains("--observe")));
    }
    return;
   }
   using(Mutex mutex=new Mutex(false,"Local\\MagicKeyboardControlsGui")){if(!mutex.WaitOne(0)){MessageBox.Show("Magic Keyboard settings are already open.");return;}Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm());}
  }
 }
}

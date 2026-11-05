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
  KeyboardRow keyboard;Segmented mode,press;RoundCombo action;Field valueField;TextBox value;RoundButton valueButton,tryIt,reset;
  Label title,subtitle,valueLabel,hint,error,status,live;Chip driverPill,trayPill,batteryPill;Stepper holdTime;FlowLayoutPanel holdRow;
  TableLayoutPanel editor,titleRow,footer;Panel header;Label modHint;RoundButton restore;
  ModifierRow modifiers;Segmented actsAs;Label actionLabel;FlowLayoutPanel modRow;
  bool filling;int key,gesture,lastAction=-1;
  readonly System.Windows.Forms.Timer statusPoll=new System.Windows.Forms.Timer{Interval=1000};
  readonly ToolTip chipTips=new ToolTip();
  readonly System.Windows.Forms.Timer apply=new System.Windows.Forms.Timer{Interval=450};

  public MainForm() {
   try{draft=Configuration.Load();}catch(Exception e){MessageBox.Show("Saved profile could not be read. Defaults will be used.\n"+e.Message,Theme.ProductName);draft=Configuration.Create();}
   Text=Theme.ProductName;Font=Theme.Body;BackColor=Theme.Back;ForeColor=Theme.Ink;
   MinimumSize=new Size(480,420);Size=new Size(980,750);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
   DoubleBuffered=true;
   try{Icon=Theme.AppIcon();}catch(Exception){}
   BuildUi();Select(0,0);
   apply.Tick+=(s,e)=>{apply.Stop();Apply();};
   HandleCreated+=(s,e)=>{Theme.TitleBar(this);LiveKeys.MarkSettings(Handle);};
   Load+=(s,e)=>{
    runner=new ActionRunner(SetStatus);
    try{AgentWindow.EnsureRunning();}catch(Exception){}
    statusPoll.Tick+=(a,b)=>PollStatus();statusPoll.Start();PollStatus();
   };
   FormClosing+=(s,e)=>{if(apply.Enabled){apply.Stop();Apply();}};
  }

  static Label MakeLabel(string text,float size,FontStyle style,Color color){return new Label{Text=text,AutoSize=true,Font=new Font("Segoe UI",size,style),ForeColor=color,BackColor=Color.Transparent};}
  static Chip Pill(){return new Chip{Margin=new Padding(0)};}
  static RoundButton FlatButton(string text,bool primary){return new RoundButton(text,primary);}
  static Panel Card(Control content){
   Panel card=new Panel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,BackColor=Theme.Back,Padding=new Padding(20,16,20,16),Margin=new Padding(0,0,0,12)};
   card.Paint+=(s,e)=>Theme.Shape(e.Graphics,card,Theme.Card,Theme.Line);card.Resize+=(s,e)=>card.Invalidate();
   content.Dock=DockStyle.Fill;card.Controls.Add(content);return card;
  }

  void BuildUi() {
   // The page is as tall as its content needs, and at least as tall as the window: when the
   // window is smaller than the content it scrolls instead of cutting anything off.
   Panel host=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=Theme.Back};
   Theme.Scrollbars(host);
   // Its width always leaves room for the scrollbar, so the scrollbar appearing never
   // changes the layout (which would resize the keys and hide the scrollbar again).
   int bar=SystemInformation.VerticalScrollBarWidth;
   TableLayoutPanel page=new TableLayoutPanel{Location=Point.Empty,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(24,18,Math.Max(4,24-bar),12),ColumnCount=1,RowCount=6,BackColor=Theme.Back};
   page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   page.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   page.RowStyles.Add(new RowStyle(SizeType.Percent,100));page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   host.Controls.Add(page);Controls.Add(host);
   host.Resize+=(s,e)=>{int w=Math.Max(200,host.Width-bar);page.MinimumSize=new Size(w,host.ClientSize.Height);page.MaximumSize=new Size(w,0);Reflow();};

   // Header: name, live status, top-row mode
   // Laid out by LayoutHeader, so its height is exactly what it shows at any width.
   header=new Panel{Dock=DockStyle.Fill,Height=60,Margin=new Padding(0,0,0,12),BackColor=Theme.Back};
   int mark=(int)(40*DeviceDpi/96f);
   header.Controls.Add(new PictureBox{Image=Theme.Logo(mark,false,Color.Empty),Size=new Size(mark,mark)});
   header.Controls.Add(MakeLabel(Theme.ProductName,20,FontStyle.Bold,Theme.Ink));
   driverPill=Pill();trayPill=Pill();batteryPill=Pill();batteryPill.Visible=false;header.Controls.Add(driverPill);header.Controls.Add(batteryPill);header.Controls.Add(trayPill);
   mode=new Segmented{Items=new[]{"F1–F12 first","Actions first"},Width=330,Font=Font};
   mode.Selected=draft.RequireFn?0:1;
   mode.SelectedChanged+=(s,e)=>{if(filling)return;draft.RequireFn=mode.Selected==0;keyboard.ActionsFirst=!draft.RequireFn;Select(key,gesture);Changed();};
   header.Controls.Add(mode);
   header.Resize+=(s,e)=>LayoutHeader();
   foreach(Control chip in new Control[]{driverPill,batteryPill,trayPill}){chip.SizeChanged+=(s,e)=>LayoutHeader();chip.VisibleChanged+=(s,e)=>LayoutHeader();}
   page.Controls.Add(header,0,0);

   // Keyboard: the top row (F1-F12, lock) as clickable keycaps
   keyboard=new KeyboardRow{Dock=DockStyle.Top,Margin=new Padding(0,0,0,12),Font=Font};
   keyboard.Bind(k=>Theme.Glyph(KeyCapAction(k)),k=>KeyName(k)+": "+Summary(k));keyboard.ActionsFirst=!draft.RequireFn;
   keyboard.SelectedChanged+=(s,e)=>Select(keyboard.SelectedKey,0);
   page.Controls.Add(keyboard,0,1);
   // Bottom row: fn, then control, option and command on both sides of the space bar
   modifiers=new ModifierRow{Dock=DockStyle.Top,Margin=new Padding(0,0,0,12),Font=Font};
   modifiers.Bind(()=>Theme.Glyph(KeyCapAction(13)),m=>TargetShort(draft.Modifiers[m]),m=>draft.Modifiers[m]!=Configuration.ModifierDefaults[m],k=>k==13?KeyName(13)+": "+Summary(13):ModifierTitle(k-20)+": acts as "+TargetLong(draft.Modifiers[k-20]));
   modifiers.SelectedChanged+=(s,e)=>Select(modifiers.SelectedKey,0);
   page.Controls.Add(modifiers,0,2);
   // Live line: the key or combination being pressed and what it does
   live=new Label{AutoSize=true,AutoEllipsis=true,Font=new Font("Segoe UI",11f,FontStyle.Bold),ForeColor=Theme.Muted,Margin=new Padding(3,0,3,12),Text=LiveIdle};
   page.Controls.Add(live,0,3);

   // Editor card
   editor=new TableLayoutPanel{ColumnCount=1,RowCount=10,BackColor=Theme.Card,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink};
   editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   editor.Resize+=(s,e)=>Reflow();
   foreach(SizeType t in new[]{SizeType.AutoSize,SizeType.AutoSize,SizeType.AutoSize,SizeType.AutoSize,SizeType.AutoSize,SizeType.AutoSize,SizeType.Absolute,SizeType.AutoSize,SizeType.Percent,SizeType.AutoSize})
    editor.RowStyles.Add(t==SizeType.Percent?new RowStyle(SizeType.Percent,100):t==SizeType.Absolute?new RowStyle(SizeType.Absolute,34):new RowStyle(t));
   titleRow=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,ColumnCount=2,Margin=new Padding(0)};
   titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   title=MakeLabel("F1",18,FontStyle.Bold,Theme.Ink);titleRow.Controls.Add(title,0,0);
   press=new Segmented{Items=new[]{"Tap","Double tap","Hold"},Anchor=AnchorStyles.Top|AnchorStyles.Right,Width=330,Margin=new Padding(12,4,0,0),Font=Font};
   press.SelectedChanged+=(s,e)=>{if(!filling)Select(key,press.Selected);};
   titleRow.Controls.Add(press,1,0);
   editor.Controls.Add(titleRow,0,0);
   subtitle=MakeLabel("",9.75f,FontStyle.Regular,Theme.Muted);subtitle.Margin=new Padding(3,2,3,14);editor.Controls.Add(subtitle,0,1);
   actionLabel=MakeLabel("Action",9.75f,FontStyle.Bold,Theme.Ink);editor.Controls.Add(actionLabel,0,2);
   action=new RoundCombo{Dock=DockStyle.Top,MaxDropDownItems=16,Margin=new Padding(3,4,3,12)};
   foreach(string[] category in Categories){
    if(category[0].Length>0)action.Items.Add(new ActionItem{Text=category[0],Header=true});
    for(int i=1;i<category.Length;i++)action.Items.Add(new ActionItem{Id=category[i],Text=Actions.Name(new Binding(category[i]))});
   }
   action.DrawItem+=DrawAction;Theme.Scrollbars(action);
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
   valueField=new Field{Dock=DockStyle.Fill,Margin=new Padding(3,0,3,0)};value=valueField.Box;Theme.Scrollbars(value);value.TextChanged+=(s,e)=>{if(!filling)Edited();};
   valueButton=FlatButton("",false);valueButton.Margin=new Padding(8,0,3,0);valueButton.Click+=(s,e)=>ValueButton();
   valueRow.Controls.Add(valueField,0,0);valueRow.Controls.Add(valueButton,1,0);editor.Controls.Add(valueRow,0,6);
   error=MakeLabel("",9,FontStyle.Regular,Theme.Bad);error.MaximumSize=new Size(800,0);editor.Controls.Add(error,0,7);
   hint=MakeLabel("",9,FontStyle.Regular,Theme.Muted);hint.MaximumSize=new Size(800,0);hint.Margin=new Padding(3,12,3,0);editor.Controls.Add(hint,0,4);
   FlowLayoutPanel buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,BackColor=Theme.Card};
   tryIt=FlatButton("Try it",true);tryIt.Click+=(s,e)=>TryAction();
   reset=FlatButton("Use default",false);reset.Click+=(s,e)=>{if(key>=20){draft.Modifiers[key-20]=Configuration.ModifierDefaults[key-20];modifiers.Invalidate();Select(key,0);Changed();return;}draft.SetBinding(key,gesture,Configuration.DefaultBinding(key,gesture));Select(key,gesture);keyboard.Invalidate();modifiers.Invalidate();Changed();};
   buttons.Controls.AddRange(new Control[]{tryIt,reset});editor.Controls.Add(buttons,0,9);
   // Lock hold time, shown with Lock's Hold action
   holdRow=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,WrapContents=false,BackColor=Theme.Card,Margin=new Padding(0,14,0,8)};
   Label holdLabel=MakeLabel("Hold for",9.75f,FontStyle.Bold,Theme.Ink);holdLabel.Margin=new Padding(3,8,10,0);
   holdTime=new Stepper(Configuration.MinLockHoldMs/1000m,Configuration.MaxLockHoldMs/1000m,0.1m,"s");
   holdTime.ValueChanged+=(s,e)=>{if(filling)return;draft.LockHoldMs=(int)(holdTime.Value*1000);Select(key,gesture);keyboard.Invalidate();Changed();};
   Label holdUnit=MakeLabel("0.5 – 5 seconds",9,FontStyle.Regular,Theme.Muted);holdUnit.Margin=new Padding(10,9,0,0);
   holdRow.Controls.AddRange(new Control[]{holdLabel,holdTime,holdUnit});
   // What a modifier key acts as, shown for the bottom-row keys
   modRow=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Theme.Card,Margin=new Padding(0,4,0,8)};
   actsAs=new Segmented{Items=new[]{"Control (Ctrl)","Option (Alt)","Command (Win)"},Width=420,Margin=new Padding(3,0,3,12),Font=Font};
   actsAs.SelectedChanged+=(s,e)=>{if(filling||key<20)return;draft.Modifiers[key-20]=Configuration.ModifierTargets[actsAs.Selected];modifiers.Invalidate();Select(key,0);Changed();};
   modHint=MakeLabel("The keyboard driver remaps the key itself, so it works in every app, even when this app isn't running. Shortcuts follow: with Command acting as Ctrl, Command+C copies.",9,FontStyle.Regular,Theme.Muted);
   modHint.MaximumSize=new Size(800,0);modHint.Margin=new Padding(3,0,3,0);
   modRow.Controls.AddRange(new Control[]{actsAs,modHint});
   FlowLayoutPanel extras=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Theme.Card,Margin=new Padding(0)};
   extras.Controls.AddRange(new Control[]{holdRow,modRow});editor.Controls.Add(extras,0,8);
   page.Controls.Add(Card(editor),0,4);

   // Footer: save state, restore all
   footer=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=3,Margin=new Padding(0)};
   footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
   status=new Label{AutoEllipsis=true,Dock=DockStyle.Fill,ForeColor=Theme.Muted,Text="Changes are saved to the keyboard as you make them.",TextAlign=ContentAlignment.MiddleLeft};
   restore=new RoundButton("Restore all defaults",false){Margin=new Padding(8,0,0,0)};
   restore.Click+=(s,e)=>{
    if(MessageBox.Show(this,"Reset every key to its default action?",Theme.ProductName,MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK)return;
    draft=Configuration.Create();filling=true;mode.Selected=0;filling=false;keyboard.ActionsFirst=false;Select(0,0);keyboard.Invalidate();modifiers.Invalidate();Changed();
   };
   footer.Controls.Add(status,0,0);footer.Controls.Add(restore,1,0);
   page.Controls.Add(footer,0,5);
  }

  // Fits the layout to the window width: the mode switch moves under the name when the
  // header is narrow, wide switches shrink, and text wraps to the card.
  // Logo, name and status chips flow left to right, wrapping when the header is narrow;
  // the mode switch sits at the right of the first line, or on its own line below.
  bool layingOut;
  void LayoutHeader(){
   if(header==null || layingOut)return;
   layingOut=true;
   try{
    int width=Math.Max(120,header.ClientSize.Width),line=48,gap=10,x=0,y=0;
    foreach(Control c in header.Controls){
     if(c==mode || !c.Visible)continue;
     if(x>0 && x+c.Width>width){x=0;y+=line+6;}
     c.Location=new Point(x,y+(line-c.Height)/2);
     x+=c.Width+gap;
    }
    int modeWidth=Math.Min(330,width);
    mode.Width=modeWidth;
    if(y==0 && x+modeWidth+14<=width)mode.Location=new Point(width-modeWidth,(line-mode.Height)/2);
    else{y+=line+6;mode.Location=new Point(0,y);line=mode.Height;}
    int height=y+line;
    if(header.Height!=height)header.Height=height;
   }finally{layingOut=false;}
  }

  void Reflow(){
   if(header==null || editor==null || footer==null || restore==null)return;
   LayoutHeader();
   int width=header.ClientSize.Width;
   int inner=Math.Max(160,editor.ClientSize.Width-8);
   foreach(Label l in new[]{subtitle,hint,error,modHint})if(l!=null)l.MaximumSize=new Size(inner,0);
   live.MaximumSize=new Size(Math.Max(160,width),0);
   // Tap / Double tap / Hold sits beside the key name, or under it when there is no room.
   bool stackTitle=inner<title.PreferredSize.Width+200+24;
   MoveCell(titleRow,press,stackTitle?0:1,stackTitle?1:0);
   press.Anchor=AnchorStyles.Top|(stackTitle?AnchorStyles.Left:AnchorStyles.Right);
   press.Width=stackTitle?Math.Min(330,inner):Math.Max(180,Math.Min(330,inner-title.PreferredSize.Width-24));
   // "Restore all defaults" moves under the status line when the footer is narrow.
   bool stackFooter=footer.ClientSize.Width<TextRenderer.MeasureText(status.Text,status.Font).Width+restore.Width+24;
   MoveCell(footer,restore,stackFooter?0:1,stackFooter?1:0);
   restore.Anchor=AnchorStyles.Top|AnchorStyles.Left;
   restore.Margin=stackFooter?new Padding(0,6,0,0):new Padding(8,0,0,0);
   actsAs.Width=Math.Max(200,Math.Min(420,inner));
  }

  static void MoveCell(TableLayoutPanel table,Control control,int column,int row){
   TableLayoutPanelCellPosition at=table.GetPositionFromControl(control);
   if(at.Column!=column || at.Row!=row)table.SetCellPosition(control,new TableLayoutPanelCellPosition(column,row));
  }

  void DrawAction(object sender,DrawItemEventArgs e){
   if(e.Index<0)return;
   ActionItem item=(ActionItem)action.Items[e.Index];
   bool inEdit=(e.State&DrawItemState.ComboBoxEdit)!=0;
   bool selected=(e.State&DrawItemState.Selected)!=0 && !item.Header && !inEdit;
   using(SolidBrush back=new SolidBrush(selected?Theme.Accent:inEdit?e.BackColor:Theme.Raised))e.Graphics.FillRectangle(back,e.Bounds);
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
   for(int g=0;g<3;g++){Binding b=draft.GetBinding(k,g);if(b.Action!="none")parts.Add((k==12&&g==2?"Hold "+HoldSeconds()+" s":names[g])+" · "+Actions.Name(b));}
   return parts.Count==0?"Do nothing":String.Join("  ·  ",parts);
  }

  static string ModifierTitle(int m){string n=ModifierRow.Names[m];return (m<3?"Left ":"Right ")+Char.ToUpperInvariant(n[0])+n.Substring(1);}
  static string TargetShort(string t){return t=="control"?"Ctrl":t=="option"?"Alt":"Win";}
  static string TargetLong(string t){return t=="control"?"Ctrl":t=="option"?"Alt":"the Windows key";}
  // Modifier keys (20-24): only "acts as" applies.
  void SelectModifier(int m){
   key=20+m;gesture=0;
   filling=true;
   keyboard.SelectedKey=-1;modifiers.SelectedKey=20+m;
   press.Visible=false;holdRow.Visible=false;
   title.Text=ModifierTitle(m);
   subtitle.Text="The "+ModifierRow.Symbols[m]+" "+ModifierRow.Names[m]+" key "+(m<3?"left":"right")+" of the space bar.  Default: "+TargetLong(Configuration.ModifierDefaults[m])+".";
   actionLabel.Text="Acts as";action.Visible=false;hint.Visible=false;
   valueLabel.Visible=valueField.Parent.Visible=false;editor.RowStyles[6].Height=0;ShowError(null);
   tryIt.Visible=false;modRow.Visible=true;
   actsAs.Selected=Array.IndexOf(Configuration.ModifierTargets,draft.Modifiers[m]);
   filling=false;
  }

  string HoldSeconds(){return (draft.LockHoldMs/1000.0).ToString("0.0#");}

  // Editor -----------------------------------------------------------------
  void Select(int k,int g){
   if(IsHandleCreated)BeginInvoke((Action)Reflow); // the title width changes with the key
   if(k>=20){SelectModifier(k-20);return;}
   modifiers.SelectedKey=k;actionLabel.Text="Action";action.Visible=hint.Visible=tryIt.Visible=true;modRow.Visible=false;
   if(k==13)g=0; // Fn itself has only a tap action
   key=k;gesture=g;
   Binding b=draft.GetBinding(k,g);
   filling=true;
   keyboard.SelectedKey=k;
   press.Visible=k==12;press.Selected=g;
   holdRow.Visible=k==12&&g==2;holdTime.Value=draft.LockHoldMs/1000m;
   title.Text=k<12?"F"+(k+1):k==13?"Fn":"Lock";
   string when=k<12?(draft.RequireFn?"Pressed while holding Fn. On its own it's F"+(k+1)+".":"Pressed on its own. Hold Fn for F"+(k+1)+".")
    :k==13?"Tap Fn by itself, without pressing another key."
    :(g==0?"Tap Lock.":g==1?"Tap Lock twice quickly.":"Hold Lock for "+HoldSeconds()+" seconds.");
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
   valueLabel.Text=label??"";valueLabel.Visible=valueField.Parent.Visible=hasValue;
   bool multiline=id=="powershell";
   value.Multiline=multiline;value.AcceptsReturn=multiline;value.ScrollBars=multiline?ScrollBars.Vertical:ScrollBars.None;
   value.Font=multiline?Mono:Font;
   editor.RowStyles[6].Height=!hasValue?0:multiline?130:Theme.ControlHeight;
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
    return "Press again to turn it off. Toggling uses the Orchard Keys tray app; without it, the driver still "+basic+".";
   }
   return "Runs through the Orchard Keys tray app, which starts when you sign in.";
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
   keyboard.Invalidate();modifiers.Invalidate();
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
   string link=null;
   try{link=Battery.Link();}catch(Exception){}
   // The chip names the connection; the driver maps keys the same way over both.
   string name=link=="bluetooth"?"Bluetooth":"USB";
   if(link==null && !driver)SetPill(driverPill,false,"Keyboard not connected");
   else SetPill(driverPill,driver,driver?name+" connected":name+" · driver not running");
   string why=link==null && !driver?"":driver
    ?(link=="bluetooth"?"Connected over Bluetooth.":"Connected with the cable.")+" The Orchard Keys driver is mapping keys."
    :"The keyboard is connected but the Orchard Keys driver isn't running on this connection. Reconnect the keyboard or restart Windows.";
   if(chipTips.GetToolTip(driverPill)!=why)chipTips.SetToolTip(driverPill,why);
   bool tray=AgentWindow.Running();
   SetPill(trayPill,tray,tray?"Tray on":"Tray off");
   ShowBattery(tray);
  }
  // Battery level as last read by the tray app (status line 2: percent;charging).
  void ShowBattery(bool tray){
   string[] f=new string[0];
   if(tray){try{string[] lines=File.ReadAllText(AgentWindow.StatusFile).Split('\n');if(lines.Length>=2)f=lines[1].Trim().Split(';');}catch(IOException){}catch(UnauthorizedAccessException){}}
   int percent;
   if(f.Length<2 || !int.TryParse(f[0],out percent)){batteryPill.Visible=false;return;}
   bool charging=f[1]=="1";
   batteryPill.Text=(charging?"\u26A1 ":"")+"Battery "+percent+"%";
   batteryPill.ForeColor=!charging && percent<=10?Theme.Bad:charging?Theme.Good:Theme.Ink;
   batteryPill.Visible=true;
  }
  const string LiveIdle="Press a function key to see it here.";
  // Keys the tray app reports while this window is open (LiveKeys); shown only while held.
  protected override void WndProc(ref Message m){
   if(LiveKeys.Message!=0 && m.Msg==LiveKeys.Message){
    int newest;PhysicalKeys keys=LiveKeys.Decode(m.WParam,m.LParam,out newest);
    keyboard.Held=keys.Held;modifiers.Held=keys.Held;
    if(keys.Held==0){live.Text=LiveIdle;live.ForeColor=Theme.Muted;return;}
    string does=Outcome(newest,keys.Fn);
    live.Text=keys.Describe()+(does.Length==0?"":"   →   "+does);live.ForeColor=Theme.Accent;
    return;
   }
   base.WndProc(ref m);
  }
  // What pressing key k does with the current profile.
  string Outcome(int k,bool fn){
   if(k<0 || k>13)return "";
   if(k<12)return (draft.RequireFn?fn:!fn)?Actions.Name(draft.Keys[k]):"F"+(k+1);
   if(k==13)return "tap: "+Actions.Name(draft.Keys[13]);
   return Summary(12);
  }
  static void SetPill(Label pill,bool ok,string text){
   pill.Text=(ok?"● ":"○ ")+text;
   pill.ForeColor=ok?Theme.Good:Theme.Muted;
  }
  void SetStatus(string text){if(IsDisposed)return;if(InvokeRequired){BeginInvoke((Action)(()=>SetStatus(text)));return;}status.ForeColor=Theme.Muted;status.Text=text;}
  protected override void Dispose(bool disposing){if(disposing){statusPoll.Dispose();apply.Dispose();chipTips.Dispose();}base.Dispose(disposing);}
 }
 sealed class Recorder:Form {
  public string Result="";
  [System.Runtime.InteropServices.DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
  public Recorder(bool single=false){Text=single?"Record key":"Record key combination";Size=new Size(450,170);StartPosition=FormStartPosition.CenterParent;KeyPreview=true;BackColor=Theme.Card;ForeColor=Theme.Ink;HandleCreated+=(s,e)=>Theme.TitleBar(this);Controls.Add(new Label{Dock=DockStyle.Fill,Text=single?"Press a single key.\nExample: K, F5, Escape\nEscape cancels.":"Press modifiers and one key.\nExample: Ctrl+Alt+K\nEscape cancels.",Padding=new Padding(20),Font=new Font("Segoe UI",11)});KeyDown+=(s,e)=>{e.SuppressKeyPress=true;if(e.KeyCode==Keys.Escape){DialogResult=DialogResult.Cancel;return;}if(new[]{Keys.ControlKey,Keys.ShiftKey,Keys.Menu,Keys.LWin,Keys.RWin}.Contains(e.KeyCode))return;if(single){Result=e.KeyCode.ToString();DialogResult=DialogResult.OK;return;}string v="";if(e.Control)v+="Ctrl+";if(e.Alt)v+="Alt+";if(e.Shift)v+="Shift+";if(GetAsyncKeyState(0x5B)<0||GetAsyncKeyState(0x5C)<0)v+="Win+";Result=v+e.KeyCode;DialogResult=DialogResult.OK;};}
 }
 static class Program {
  [STAThread]static void Main(string[] args){
   if(args.Length==2 && args[0]=="--write-icon"){Theme.WriteIco(args[1]);return;}
   if(args.Contains("--driver-test")){SelfTest.RunDriver();return;}
   if(args.Contains("--self-test")){SelfTest.Run();return;}
   if(args.Contains("--agent") || args.Contains("--background")){
    if(AgentWindow.Elevated())return; // the tray app runs unelevated (it starts at sign-in)
    using(Mutex agent=new Mutex(false,AgentWindow.MutexName)){
     try{if(!agent.WaitOne(0))return;}catch(AbandonedMutexException){}
     Application.EnableVisualStyles();Application.Run(new AgentWindow(args.Contains("--observe")));
    }
    return;
   }
   using(Mutex mutex=new Mutex(false,"Local\\MagicKeyboardControlsGui")){if(!mutex.WaitOne(0)){LiveKeys.ShowSettings();return;}Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm());}
  }
 }
}

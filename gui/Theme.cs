using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MagicKeyboard {
 // Colors, icon glyphs and small custom-drawn controls shared by the settings window and tray.
 static class Theme {
  public static readonly bool Dark=ReadDark();
  public static readonly Color Back=Dark?Color.FromArgb(32,32,32):Color.FromArgb(243,243,243);
  public static readonly Color Card=Dark?Color.FromArgb(43,43,43):Color.White;
  public static readonly Color Raised=Dark?Color.FromArgb(55,55,55):Color.FromArgb(249,249,249);
  public static readonly Color Hover=Dark?Color.FromArgb(62,62,62):Color.FromArgb(237,237,237);
  public static readonly Color Line=Dark?Color.FromArgb(70,70,70):Color.FromArgb(222,222,222);
  public static readonly Color Ink=Dark?Color.FromArgb(240,240,240):Color.FromArgb(27,27,27);
  public static readonly Color Muted=Dark?Color.FromArgb(160,160,160):Color.FromArgb(96,96,96);
  public static readonly Color Accent=Dark?Color.FromArgb(96,205,255):Color.FromArgb(0,95,184);
  public static readonly Color AccentInk=Dark?Color.Black:Color.White;
  public static readonly Color Good=Dark?Color.FromArgb(108,203,95):Color.FromArgb(15,123,15);
  public static readonly Color Bad=Dark?Color.FromArgb(255,153,164):Color.FromArgb(196,43,28);
  public static readonly string IconFamily=HasFont("Segoe Fluent Icons")?"Segoe Fluent Icons":"Segoe MDL2 Assets";
  // One shape for everything: every control is ControlHeight tall with Radius corners;
  // a shape inset inside another uses Radius minus the inset.
  public const float Radius=8f;
  public const int ControlHeight=32;
  public static readonly Font Body=new Font("Segoe UI",9.75f);

  static bool ReadDark(){
   try{object v=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1);return v is int && (int)v==0;}
   catch(Exception){return false;}
  }
  static bool HasFont(string name){using(InstalledFontCollection fonts=new InstalledFontCollection()){foreach(FontFamily f in fonts.Families)if(f.Name==name)return true;}return false;}

  [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
  public static void TitleBar(Form form){int dark=Dark?1:0;DwmSetWindowAttribute(form.Handle,20,ref dark,4);}

  public static GraphicsPath Round(RectangleF r,float radius){
   GraphicsPath p=new GraphicsPath();float d=radius*2;
   p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);
   p.CloseFigure();return p;
  }

  // Segoe Fluent Icons code points for each action.
  public static string Glyph(string action){
   switch(action){
    case "brightness-down":return "";case "brightness-up":return "";
    case "task-view":return "";case "search":return "";case "voice-typing":return "";
    case "do-not-disturb":return "";case "notifications":return "";case "quick-settings":return "";
    case "previous-track":return "";case "play-pause":return "";case "next-track":return "";
    case "mute":return "";case "volume-down":return "";case "volume-up":return "";
    case "lock":return "";case "input-method":return "";case "screenshot":return "";
    case "calculator":return "";case "show-desktop":return "";case "clipboard-history":return "";
    case "emoji":return "";case "sleep":return "";case "power":return "";
    case "key":return "";case "shortcut":return "";case "text":return "";
    case "app":return "";case "url":return "";case "folder":return "";case "powershell":return "";
   }
   return "";
  }

  public const string ProductName="Orchard Keys";

  // The Orchard Keys mark: a square keycap with "fn" and a leaf on its stem.
  // Color: graphite cap, white "fn", green leaf. Mono (tray): a solid cap in one color
  // with "fn" cut out, which stays legible at 16 px.
  public static Bitmap Logo(int size,bool mono,Color monoInk){
   Bitmap b=new Bitmap(size,size,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
   using(Graphics g=Graphics.FromImage(b)){
    g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
    float s=size,inset=mono?s*0.04f:s*0.06f,top=s*0.2f;
    RectangleF cap=new RectangleF(inset,top,s-inset*2,s-top-inset);
    float radius=Math.Max(1.5f,cap.Width*0.22f);
    // Leaf on a short stem, above the cap's top right.
    using(GraphicsPath leaf=new GraphicsPath()){
     float lx=s*0.6f,ly=top+s*0.01f,lw=s*0.32f,lh=s*0.2f;
     leaf.AddBezier(lx,ly,lx+lw*0.2f,ly-lh*1.1f,lx+lw*0.8f,ly-lh,lx+lw,ly-lh);
     leaf.AddBezier(lx+lw,ly-lh,lx+lw*0.85f,ly-lh*0.05f,lx+lw*0.3f,ly+lh*0.1f,lx,ly);
     using(SolidBrush green=new SolidBrush(mono?monoInk:Color.FromArgb(108,203,95)))g.FillPath(green,leaf);
    }
    using(GraphicsPath p=Round(cap,radius)){
     if(mono){using(SolidBrush ink=new SolidBrush(monoInk))g.FillPath(ink,p);}
     else{
      using(LinearGradientBrush fill=new LinearGradientBrush(cap,Color.FromArgb(74,74,78),Color.FromArgb(26,26,28),LinearGradientMode.Vertical))g.FillPath(fill,p);
      using(Pen rim=new Pen(Color.FromArgb(90,255,255,255),Math.Max(1f,s/64f)))g.DrawPath(rim,p);
     }
    }
    using(FontFamily family=new FontFamily("Segoe UI"))
    using(GraphicsPath text=new GraphicsPath())
    using(StringFormat center=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}){
     text.AddString("fn",family,(int)FontStyle.Bold,cap.Height*0.66f,new RectangleF(cap.X,cap.Y-cap.Height*0.05f,cap.Width,cap.Height),center);
     if(mono){g.CompositingMode=CompositingMode.SourceCopy;using(SolidBrush clear=new SolidBrush(Color.Transparent))g.FillPath(clear,text);}
     else using(SolidBrush white=new SolidBrush(Color.White))g.FillPath(white,text);
    }
   }
   return b;
  }

  [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
  static Icon ToIcon(Bitmap b){IntPtr handle=b.GetHicon();Icon icon=(Icon)Icon.FromHandle(handle).Clone();DestroyIcon(handle);return icon;}
  // Tray icon: the mono mark in the taskbar's text color, at the current DPI.
  public static Icon TrayIcon(){
   bool lightTaskbar=false;
   try{object v=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","SystemUsesLightTheme",0);lightTaskbar=v is int && (int)v==1;}catch(Exception){}
   using(Bitmap b=Logo(SystemInformation.SmallIconSize.Width,true,lightTaskbar?Color.Black:Color.White))return ToIcon(b);
  }
  // Window icon: the color mark.
  public static Icon AppIcon(){using(Bitmap b=Logo(SystemInformation.IconSize.Width,false,Color.Empty))return ToIcon(b);}
  // Writes the color mark as a multi-size .ico (PNG entries) for the .exe resources.
  public static void WriteIco(string path){
   int[] sizes={16,20,24,32,40,48,64,128,256};
   var images=new System.Collections.Generic.List<byte[]>();
   foreach(int size in sizes)using(Bitmap b=Logo(size,false,Color.Empty))using(System.IO.MemoryStream m=new System.IO.MemoryStream()){b.Save(m,System.Drawing.Imaging.ImageFormat.Png);images.Add(m.ToArray());}
   using(System.IO.BinaryWriter w=new System.IO.BinaryWriter(System.IO.File.Create(path))){
    w.Write((short)0);w.Write((short)1);w.Write((short)sizes.Length);
    int offset=6+16*sizes.Length;
    for(int i=0;i<sizes.Length;i++){
     w.Write((byte)(sizes[i]>=256?0:sizes[i]));w.Write((byte)(sizes[i]>=256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);
     w.Write((short)1);w.Write((short)32);w.Write(images[i].Length);w.Write(offset);offset+=images[i].Length;
    }
    foreach(byte[] image in images)w.Write(image);
   }
  }

  // Tray menu colors that follow the theme.
  public sealed class MenuColors:ProfessionalColorTable {
   public override Color ToolStripDropDownBackground{get{return Card;}}
   public override Color ImageMarginGradientBegin{get{return Card;}}
   public override Color ImageMarginGradientMiddle{get{return Card;}}
   public override Color ImageMarginGradientEnd{get{return Card;}}
   public override Color MenuBorder{get{return Line;}}
   public override Color MenuItemBorder{get{return Hover;}}
   public override Color MenuItemSelected{get{return Hover;}}
   public override Color SeparatorDark{get{return Line;}}
   public override Color SeparatorLight{get{return Line;}}
  }
  // Menu items get the same rounded highlight as other controls; the menu window gets
  // Windows 11's rounded corners.
  sealed class MenuRenderer:ToolStripProfessionalRenderer {
   public MenuRenderer():base(new MenuColors()){RoundedEdges=false;}
   protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){
    if(!e.Item.Selected || !e.Item.Enabled)return;
    e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
    using(GraphicsPath p=Round(new RectangleF(4,1,e.Item.Width-9,e.Item.Height-3),Radius-4))using(SolidBrush b=new SolidBrush(Hover))e.Graphics.FillPath(b,p);
   }
  }
  public static ContextMenuStrip Menu(){
   ContextMenuStrip menu=new ContextMenuStrip{Renderer=new MenuRenderer(),ShowImageMargin=false,Font=Body,BackColor=Card,ForeColor=Ink,Padding=new Padding(4)};
   menu.Opened+=(s,e)=>{int round=2;DwmSetWindowAttribute(menu.Handle,33,ref round,4);};
   return menu;
  }
  // Fills a control's square corners with its parent's color, then draws the rounded shape.
  public static void Shape(Graphics g,Control c,Color fill,Color edge){
   g.SmoothingMode=SmoothingMode.AntiAlias;
   g.Clear(c.Parent!=null?c.Parent.BackColor:Back);
   using(GraphicsPath p=Round(new RectangleF(0.5f,0.5f,c.Width-1.5f,c.Height-1.5f),Radius))
   using(SolidBrush b=new SolidBrush(fill))using(Pen pen=new Pen(edge)){g.FillPath(b,p);g.DrawPath(pen,p);}
  }
 }

 // Push button: primary (accent) or secondary, ControlHeight tall, sized to its text.
 sealed class RoundButton:Button {
  bool hover,down;public bool Primary;
  public RoundButton(string text,bool primary){
   SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
   Primary=primary;Font=Theme.Body;Cursor=Cursors.Hand;Margin=new Padding(0,0,8,0);Text=text;
  }
  protected override void OnTextChanged(EventArgs e){
   base.OnTextChanged(e);
   Size=new Size(Math.Max(88,TextRenderer.MeasureText(Text,Font).Width+32),Theme.ControlHeight);
  }
  protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
  protected override void OnMouseLeave(EventArgs e){hover=down=false;Invalidate();base.OnMouseLeave(e);}
  protected override void OnMouseDown(MouseEventArgs e){down=true;Invalidate();base.OnMouseDown(e);}
  protected override void OnMouseUp(MouseEventArgs e){down=false;Invalidate();base.OnMouseUp(e);}
  protected override void OnPaint(PaintEventArgs e){
   Color fill=Primary?Theme.Accent:Theme.Raised;
   if(!Enabled)fill=Theme.Raised;
   else if(down)fill=Primary?ControlPaint.Dark(Theme.Accent,0.05f):Theme.Line;
   else if(hover)fill=Primary?ControlPaint.Light(Theme.Accent,0.15f):Theme.Hover;
   Theme.Shape(e.Graphics,this,fill,Primary&&Enabled?fill:Theme.Line);
   if(Focused && ShowFocusCues)
    using(GraphicsPath p=Theme.Round(new RectangleF(2.5f,2.5f,Width-5.5f,Height-5.5f),Theme.Radius-2))using(Pen pen=new Pen(Primary?Theme.AccentInk:Theme.Accent))e.Graphics.DrawPath(pen,p);
   TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,!Enabled?Theme.Muted:Primary?Theme.AccentInk:Theme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
  }
 }

 // Rounded frame around a borderless TextBox, matching buttons and lists.
 sealed class Field:Panel {
  public readonly TextBox Box;
  public Field(){
   SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
   Box=new TextBox{BorderStyle=BorderStyle.None,BackColor=Theme.Raised,ForeColor=Theme.Ink,Font=Theme.Body,Dock=DockStyle.Fill};
   Padding=new Padding(10,8,8,6);Height=Theme.ControlHeight;BackColor=Theme.Raised;Cursor=Cursors.IBeam;
   Controls.Add(Box);
   Box.GotFocus+=(s,e)=>Invalidate();Box.LostFocus+=(s,e)=>Invalidate();
   Click+=(s,e)=>Box.Focus();
  }
  protected override void OnPaint(PaintEventArgs e){Theme.Shape(e.Graphics,this,Theme.Raised,Box.Focused?Theme.Accent:Theme.Line);}
 }

 // Drop-down list drawn with the shared rounded shape and a chevron.
 sealed class RoundCombo:ComboBox {
  bool hover;
  // Paints the closed box itself; the selected item is drawn by the DrawItem handler.
  public RoundCombo(){
   SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
   DropDownStyle=ComboBoxStyle.DropDownList;DrawMode=DrawMode.OwnerDrawFixed;ItemHeight=Theme.ControlHeight-6;FlatStyle=FlatStyle.Flat;BackColor=Theme.Raised;ForeColor=Theme.Ink;Font=Theme.Body;Cursor=Cursors.Hand;
  }
  protected override void OnSelectedIndexChanged(EventArgs e){Invalidate();base.OnSelectedIndexChanged(e);}
  protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
  protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
  protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
  protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
  protected override void OnPaint(PaintEventArgs e){
   Graphics g=e.Graphics;
   Theme.Shape(g,this,hover?Theme.Hover:Theme.Raised,Focused||DroppedDown?Theme.Accent:Theme.Line);
   if(SelectedIndex>=0){
    Rectangle text=new Rectangle(2,3,Width-34,Height-6);
    Region clip=g.Clip;
    g.SetClip(text);
    OnDrawItem(new DrawItemEventArgs(g,Font,text,SelectedIndex,DrawItemState.ComboBoxEdit,Theme.Ink,hover?Theme.Hover:Theme.Raised));
    g.Clip=clip;
   }
   using(Font icon=new Font(Theme.IconFamily,8f))
    TextRenderer.DrawText(g,"",icon,new Rectangle(Width-32,0,26,Height),Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
  }
 }

 // Number with − and + buttons, in the shared rounded shape.
 sealed class Stepper:Control {
  decimal value,min,max,step;readonly string unit;int hover=-1;
  public event EventHandler ValueChanged;
  public Stepper(decimal min,decimal max,decimal step,string unit){
   SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
   this.min=min;this.max=max;this.step=step;this.unit=unit;value=min;Size=new Size(150,Theme.ControlHeight);Font=Theme.Body;TabStop=true;
  }
  public decimal Value{get{return value;}set{decimal v=Math.Max(min,Math.Min(max,Math.Round(value/step)*step));if(v==this.value)return;this.value=v;Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}}
  int Part(int x){return x<Theme.ControlHeight?0:x>=Width-Theme.ControlHeight?2:1;}
  protected override void OnMouseMove(MouseEventArgs e){int p=Part(e.X);if(p!=hover){hover=p;Cursor=p==1?Cursors.Default:Cursors.Hand;Invalidate();}}
  protected override void OnMouseLeave(EventArgs e){hover=-1;Invalidate();}
  protected override void OnMouseDown(MouseEventArgs e){Focus();int p=Part(e.X);if(p==0)Value-=step;else if(p==2)Value+=step;}
  protected override void OnMouseWheel(MouseEventArgs e){Value+=e.Delta>0?step:-step;}
  protected override bool IsInputKey(Keys k){return k==Keys.Up||k==Keys.Down||k==Keys.Left||k==Keys.Right||base.IsInputKey(k);}
  protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Right)Value+=step;else if(e.KeyCode==Keys.Down||e.KeyCode==Keys.Left)Value-=step;}
  protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
  protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
  protected override void OnPaint(PaintEventArgs e){
   Graphics g=e.Graphics;int s=Theme.ControlHeight;
   Theme.Shape(g,this,Theme.Raised,Focused?Theme.Accent:Theme.Line);
   for(int i=0;i<2;i++){
    int part=i==0?0:2;RectangleF r=new RectangleF(i==0?3:Width-s+2,3,s-6,Height-7);
    bool off=part==0?value<=min:value>=max;
    if(hover==part && !off)using(GraphicsPath p=Theme.Round(r,Theme.Radius-3))using(SolidBrush b=new SolidBrush(Theme.Hover))g.FillPath(b,p);
    using(Font icon=new Font(Theme.IconFamily,9f))
     TextRenderer.DrawText(g,part==0?"":"",icon,Rectangle.Round(r),off?Theme.Line:Theme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
   }
   TextRenderer.DrawText(g,value.ToString("0.0")+" "+unit,Font,new Rectangle(s,0,Width-2*s,Height),Theme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
  }
 }

 // Small status chip (keyboard / tray state) in the shared shape.
 sealed class Chip:Label {
  public Chip(){AutoSize=true;Padding=new Padding(10,7,10,7);Font=new Font("Segoe UI Semibold",9f);BackColor=Color.Transparent;TextAlign=ContentAlignment.MiddleCenter;}
  protected override void OnPaintBackground(PaintEventArgs e){Theme.Shape(e.Graphics,this,Theme.Raised,Theme.Line);}
 }

 // Segmented control (mode and Tap / Double tap / Hold) in the shared shape.
 sealed class Segmented:Control {
  string[] items=new string[0];int selected,hover=-1;
  public event EventHandler SelectedChanged;
  public Segmented(){SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);Height=Theme.ControlHeight;Cursor=Cursors.Hand;Font=Theme.Body;}
  public string[] Items{get{return items;}set{items=value;Invalidate();}}
  public int Selected{get{return selected;}set{if(selected==value)return;selected=value;Invalidate();if(SelectedChanged!=null)SelectedChanged(this,EventArgs.Empty);}}
  RectangleF Segment(int i){float w=(Width-1f)/Math.Max(1,items.Length);return new RectangleF(i*w,0,w,Height-1);}
  protected override void OnMouseMove(MouseEventArgs e){int h=(int)(e.X/((Width-1f)/Math.Max(1,items.Length)));if(h!=hover){hover=h;Invalidate();}}
  protected override void OnMouseLeave(EventArgs e){hover=-1;Invalidate();}
  protected override void OnMouseDown(MouseEventArgs e){int i=(int)(e.X/((Width-1f)/Math.Max(1,items.Length)));if(i>=0&&i<items.Length)Selected=i;}
  protected override void OnPaint(PaintEventArgs e){
   Graphics g=e.Graphics;
   Theme.Shape(g,this,Theme.Raised,Theme.Line);
   for(int i=0;i<items.Length;i++){
    RectangleF r=Segment(i);r.Inflate(-3,-3);
    if(i==selected||i==hover)
     using(GraphicsPath pill=Theme.Round(r,Theme.Radius-3))using(SolidBrush b=new SolidBrush(i==selected?Theme.Accent:Theme.Hover))g.FillPath(b,pill);
    TextRenderer.DrawText(g,items[i],Font,Rectangle.Round(r),i==selected?Theme.AccentInk:Theme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
   }
  }
 }

 // The Magic Keyboard's top row drawn as keycaps; click a key to edit it.
 sealed class KeyboardRow:Control {
  // Display order: fn, F1-F12, Lock (data indices 13, 0-11, 12).
  static readonly int[] Keys={13,0,1,2,3,4,5,6,7,8,9,10,11,12};
  Func<int,string> glyph=k=>"";Func<int,string> tip=k=>"";
  int selected,hover=-1;readonly ToolTip toolTip=new ToolTip();
  public event EventHandler SelectedChanged;
  public KeyboardRow(){SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);Cursor=Cursors.Hand;Height=96;}
  public void Bind(Func<int,string> glyphOf,Func<int,string> tipOf){glyph=glyphOf;tip=tipOf;Invalidate();}
  public int SelectedKey{get{return Keys[selected];}set{selected=Array.IndexOf(Keys,value);Invalidate();}}
  int held;
  // Keys being pressed right now (data index bits), drawn lit.
  public int Held{get{return held;}set{if(held==value)return;held=value;Invalidate();}}
  bool actionsFirst;
  // Actions first: the action icon is the big label. F1-F12 first: the key name is.
  public bool ActionsFirst{get{return actionsFirst;}set{if(actionsFirst==value)return;actionsFirst=value;Invalidate();}}
  // Square keycaps, as large as the row allows, centered.
  RectangleF Cap(int i){
   float gap=6,extra=10,w=Math.Min((Width-1-gap*13-extra*2)/14f,Height-4);
   float left=(Width-1-(w*14+gap*13+extra*2))/2,x=left+i*(w+gap)+(i>0?extra:0)+(i>12?extra:0);
   return new RectangleF(x,(Height-3-w)/2,w,w);
  }
  int HitTest(Point p){for(int i=0;i<Keys.Length;i++)if(Cap(i).Contains(p))return i;return -1;}
  protected override void OnMouseMove(MouseEventArgs e){int h=HitTest(e.Location);if(h==hover)return;hover=h;Invalidate();toolTip.SetToolTip(this,h<0?"":tip(Keys[h]));}
  protected override void OnMouseLeave(EventArgs e){hover=-1;Invalidate();}
  protected override void OnMouseDown(MouseEventArgs e){int h=HitTest(e.Location);if(h<0||h==selected)return;selected=h;Invalidate();if(SelectedChanged!=null)SelectedChanged(this,EventArgs.Empty);}
  protected override void OnPaint(PaintEventArgs e){
   Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;g.Clear(Parent!=null?Parent.BackColor:Theme.Back);
   float size=Keys.Length>0?Cap(0).Width:40;
   using(Font label=new Font("Segoe UI",Math.Max(7f,size*0.14f)))
   using(Font icon=new Font(Theme.IconFamily,Math.Max(9f,size*0.17f)))
   using(Font bigLabel=new Font("Segoe UI Semibold",Math.Max(9f,size*0.24f)))
   using(Font bigIcon=new Font(Theme.IconFamily,Math.Max(12f,size*0.34f))){
    for(int i=0;i<Keys.Length;i++){
     RectangleF r=Cap(i);int k=Keys[i];bool sel=i==selected,down=(held&(1<<k))!=0;
     if(down)r.Offset(0,2);
     using(GraphicsPath cap=Theme.Round(r,Theme.Radius)){
      using(SolidBrush fill=new SolidBrush(sel?Theme.Accent:i==hover?Theme.Hover:Theme.Card))g.FillPath(fill,cap);
      if(down)using(SolidBrush glow=new SolidBrush(Color.FromArgb(70,Theme.Good)))g.FillPath(glow,cap);
      using(Pen edge=new Pen(down?Theme.Good:sel?Theme.Accent:Theme.Line,down?2f:1f))g.DrawPath(edge,cap);
     }
     Color ink=sel?Theme.AccentInk:Theme.Ink,muted=sel?Theme.AccentInk:Theme.Muted;
     string name=k==13?"fn":k==12?"lock":"F"+(k+1);
     string gl=glyph(k);
     Rectangle corner=new Rectangle((int)(r.X+r.Width*0.1f),(int)(r.Y+r.Height*0.07f),(int)(r.Width*0.85f),(int)(r.Height*0.3f));
     Rectangle middle=new Rectangle((int)r.X,(int)(r.Y+r.Height*0.3f),(int)r.Width,(int)(r.Height*0.62f));
     const TextFormatFlags center=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding;
     // fn and lock always lead with their action; F1-F12 follow the mode.
     bool iconBig=(actionsFirst || k>=12) && gl.Length>0;
     if(iconBig){
      TextRenderer.DrawText(g,name,label,corner,muted,TextFormatFlags.Left|TextFormatFlags.NoPadding);
      TextRenderer.DrawText(g,gl,bigIcon,middle,ink,center);
     }else{
      if(gl.Length>0)TextRenderer.DrawText(g,gl,icon,corner,muted,TextFormatFlags.Left|TextFormatFlags.NoPadding);
      TextRenderer.DrawText(g,name,bigLabel,middle,ink,center);
     }
    }
   }
  }
  protected override void Dispose(bool disposing){if(disposing)toolTip.Dispose();base.Dispose(disposing);}
 }
}

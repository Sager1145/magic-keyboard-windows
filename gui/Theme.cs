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

  // Small tray icon: a keyboard glyph drawn at the current DPI.
  [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
  public static Icon TrayIcon(){
   int size=SystemInformation.SmallIconSize.Width;
   using(Bitmap b=new Bitmap(size,size))
   using(Graphics g=Graphics.FromImage(b))
   using(Font f=new Font(IconFamily,size*0.72f,GraphicsUnit.Pixel)){
    g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
    bool lightTaskbar=false;
    try{object v=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","SystemUsesLightTheme",0);lightTaskbar=v is int && (int)v==1;}catch(Exception){}
    TextRenderer.DrawText(g,"",f,new Rectangle(0,0,size,size),lightTaskbar?Color.Black:Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
    IntPtr handle=b.GetHicon();
    Icon icon=(Icon)Icon.FromHandle(handle).Clone();
    DestroyIcon(handle);
    return icon;
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
  public static ContextMenuStrip Menu(){
   ContextMenuStrip menu=new ContextMenuStrip{Renderer=new ToolStripProfessionalRenderer(new MenuColors()){RoundedEdges=false},ShowImageMargin=false,Font=new Font("Segoe UI",9.5f),BackColor=Card,ForeColor=Ink,Padding=new Padding(4)};
   return menu;
  }
 }

 // Pill-shaped segmented control (mode and Tap / Double tap / Hold).
 sealed class Segmented:Control {
  string[] items=new string[0];int selected,hover=-1;
  public event EventHandler SelectedChanged;
  public Segmented(){SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);Height=34;Cursor=Cursors.Hand;}
  public string[] Items{get{return items;}set{items=value;Invalidate();}}
  public int Selected{get{return selected;}set{if(selected==value)return;selected=value;Invalidate();if(SelectedChanged!=null)SelectedChanged(this,EventArgs.Empty);}}
  RectangleF Segment(int i){float w=(Width-1f)/Math.Max(1,items.Length);return new RectangleF(i*w,0,w,Height-1);}
  protected override void OnMouseMove(MouseEventArgs e){int h=(int)(e.X/((Width-1f)/Math.Max(1,items.Length)));if(h!=hover){hover=h;Invalidate();}}
  protected override void OnMouseLeave(EventArgs e){hover=-1;Invalidate();}
  protected override void OnMouseDown(MouseEventArgs e){int i=(int)(e.X/((Width-1f)/Math.Max(1,items.Length)));if(i>=0&&i<items.Length)Selected=i;}
  protected override void OnPaint(PaintEventArgs e){
   Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent!=null?Parent.BackColor:Theme.Back);
   using(GraphicsPath outer=Theme.Round(new RectangleF(0,0,Width-1,Height-1),Height/2f-1))
   using(SolidBrush back=new SolidBrush(Theme.Raised))using(Pen line=new Pen(Theme.Line)){g.FillPath(back,outer);g.DrawPath(line,outer);}
   for(int i=0;i<items.Length;i++){
    RectangleF r=Segment(i);r.Inflate(-3,-3);
    if(i==selected||i==hover)
     using(GraphicsPath pill=Theme.Round(r,r.Height/2))using(SolidBrush b=new SolidBrush(i==selected?Theme.Accent:Theme.Hover))g.FillPath(b,pill);
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
  RectangleF Cap(int i){
   float gap=6,extra=10,w=(Width-1-gap*13-extra*2)/14f,x=i*(w+gap)+(i>0?extra:0)+(i>12?extra:0);
   float h=Math.Min(Height-1,w*1.05f);return new RectangleF(x,(Height-1-h)/2,w,h);
  }
  int HitTest(Point p){for(int i=0;i<Keys.Length;i++)if(Cap(i).Contains(p))return i;return -1;}
  protected override void OnMouseMove(MouseEventArgs e){int h=HitTest(e.Location);if(h==hover)return;hover=h;Invalidate();toolTip.SetToolTip(this,h<0?"":tip(Keys[h]));}
  protected override void OnMouseLeave(EventArgs e){hover=-1;Invalidate();}
  protected override void OnMouseDown(MouseEventArgs e){int h=HitTest(e.Location);if(h<0||h==selected)return;selected=h;Invalidate();if(SelectedChanged!=null)SelectedChanged(this,EventArgs.Empty);}
  protected override void OnPaint(PaintEventArgs e){
   Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;g.Clear(Parent!=null?Parent.BackColor:Theme.Back);
   using(Font label=new Font("Segoe UI",8.25f))
   using(Font icon=new Font(Theme.IconFamily,13f)){
    for(int i=0;i<Keys.Length;i++){
     RectangleF r=Cap(i);int k=Keys[i];bool sel=i==selected;
     using(GraphicsPath cap=Theme.Round(r,7)){
      using(SolidBrush fill=new SolidBrush(sel?Theme.Accent:i==hover?Theme.Hover:Theme.Card))g.FillPath(fill,cap);
      using(Pen edge=new Pen(sel?Theme.Accent:Theme.Line))g.DrawPath(edge,cap);
     }
     Color ink=sel?Theme.AccentInk:Theme.Ink,muted=sel?Theme.AccentInk:Theme.Muted;
     string name=k==13?"fn":k==12?"lock":"F"+(k+1);
     TextRenderer.DrawText(g,name,label,new Rectangle((int)r.X+6,(int)r.Y+4,(int)r.Width-8,16),muted,TextFormatFlags.Left|TextFormatFlags.NoPadding);
     string gl=glyph(k);
     if(gl.Length>0)TextRenderer.DrawText(g,gl,icon,new Rectangle((int)r.X,(int)(r.Y+r.Height*0.35f),(int)r.Width,(int)(r.Height*0.6f)),ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
    }
   }
  }
  protected override void Dispose(bool disposing){if(disposing)toolTip.Dispose();base.Dispose(disposing);}
 }
}

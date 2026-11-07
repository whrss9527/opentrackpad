using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace OpenTrackpadUi {
  static class Palette {
    static readonly Dictionary<string,Font> fonts=new Dictionary<string,Font>();
    static Color ColorOf(int light,int dark){int rgb=UiPreferences.Dark?dark:light;return Color.FromArgb((rgb>>16)&255,(rgb>>8)&255,rgb&255);}
    public static Color Canvas{get{return SystemInformation.HighContrast?SystemColors.Window:ColorOf(0xf7f8fa,0x191c23);}}
    public static Color Surface{get{return SystemInformation.HighContrast?SystemColors.Window:ColorOf(0xffffff,0x252932);}}
    public static Color Ink{get{return SystemInformation.HighContrast?SystemColors.WindowText:ColorOf(0x252935,0xf0f1f5);}}
    public static Color Muted{get{return SystemInformation.HighContrast?SystemColors.WindowText:ColorOf(0x6b7283,0xaeb5c3);}}
    public static Color Line{get{return SystemInformation.HighContrast?SystemColors.WindowText:ColorOf(0xe4e7ee,0x3a404e);}}
    public static Color Accent{get{return SystemInformation.HighContrast?SystemColors.Highlight:ColorOf(0x147d69,0x78dec3);}}
    public static Color PrimaryInk{get{return SystemInformation.HighContrast?SystemColors.HighlightText:UiPreferences.Dark?Color.FromArgb(16,41,34):Color.White;}}
    public static Color Soft{get{return SystemInformation.HighContrast?SystemColors.Control:ColorOf(0xe6f5ef,0x2b3e3c);}}
    public static Color Hover{get{return SystemInformation.HighContrast?SystemColors.Highlight:ColorOf(0xf0f2f7,0x303642);}}
    public static Color Nav{get{return SystemInformation.HighContrast?SystemColors.Window:ColorOf(0xeff1f5,0x20232b);}}
    public static Color NavInk{get{return Ink;}}
    public static Color NavMuted{get{return SystemInformation.HighContrast?SystemColors.WindowText:ColorOf(0x616a7a,0xaeb5c3);}}
    public static Color NavSelected{get{return SystemInformation.HighContrast?SystemColors.Highlight:ColorOf(0xffffff,0x373e4b);}}
    public static Color GentleEdge{get{return SystemInformation.HighContrast?Line:Color.FromArgb(UiPreferences.Dark?100:130,Line);}}
    public static Color LogSurface{get{return Color.FromArgb(23,32,46);}}
    public static Color LogInk{get{return Color.FromArgb(200,216,236);}}
    public static Font Font(float size,FontStyle style){return FontFor(UiPreferences.FontFamily,size,style);}
    public static Font FontFor(string family,float size,FontStyle style){
      string key=family+"|"+size.ToString("0.###",CultureInfo.InvariantCulture)+"|"+(int)style;
      lock(fonts){Font result;if(fonts.TryGetValue(key,out result))return result;
        try{using(var face=new FontFamily(family)){if(!face.IsStyleAvailable(style))style=FontStyle.Regular;result=new Font(face,size,style,GraphicsUnit.Point);}}
        catch(ArgumentException){result=new Font(SystemFonts.MessageBoxFont.FontFamily,size,style,GraphicsUnit.Point);}
        fonts[key]=result;return result;
      }
    }
    public static Label Label(string text,float size,Color color){return new Label{Text=text,Font=Font(size,FontStyle.Regular),ForeColor=color,AutoSize=false,AutoEllipsis=true,BackColor=Color.Transparent,TextAlign=ContentAlignment.MiddleLeft,Tag=color==Ink?"ink":color==Muted?"muted":color==Accent?"accent":null};}
    public static void StyleMenu(ContextMenuStrip menu){
      menu.Renderer=new SurfaceMenuRenderer();menu.ShowImageMargin=false;menu.ShowCheckMargin=true;menu.DropShadowEnabled=true;
      menu.Padding=new Padding(7);menu.BackColor=Surface;menu.ForeColor=Ink;menu.Font=Font(9.5f,FontStyle.Regular);
      menu.Opening+=delegate{menu.BackColor=Surface;menu.ForeColor=Ink;menu.Font=Font(9.5f,FontStyle.Regular);StyleItems(menu.Items);};
      menu.SizeChanged+=delegate{if(menu.Width<4 || menu.Height<4)return;using(var path=Round(new RectangleF(0,0,menu.Width,menu.Height),10)){Region previous=menu.Region;menu.Region=new Region(path);if(previous!=null)previous.Dispose();}};
    }
    static void StyleItems(ToolStripItemCollection items){foreach(ToolStripItem item in items){item.ForeColor=Ink;if(!(item.Tag is FontChoice))item.Font=Font(9.5f,FontStyle.Regular);item.Padding=new Padding(5,7,12,7);var drop=item as ToolStripDropDownItem;if(drop!=null){drop.DropDown.BackColor=Surface;drop.DropDown.ForeColor=Ink;drop.DropDown.Renderer=new SurfaceMenuRenderer();StyleItems(drop.DropDownItems);}}}
    sealed class SurfaceMenuRenderer:ToolStripProfessionalRenderer{
      public SurfaceMenuRenderer(){RoundedEdges=false;}
      protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(Surface);}
      protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){}
      protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Round(new RectangleF(.5f,.5f,e.ToolStrip.Width-1,e.ToolStrip.Height-1),10))using(var pen=new Pen(Line))e.Graphics.DrawPath(pen,path);}
      protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){if(e.Item.Selected || e.Item.Pressed){using(var path=Round(new RectangleF(1,1,e.Item.Width-2,e.Item.Height-2),6))using(var fill=new SolidBrush(Hover))e.Graphics.FillPath(fill,path);}}
      protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=SystemInformation.HighContrast && (e.Item.Selected || e.Item.Pressed)?SystemColors.HighlightText:e.Item.Enabled?Ink:Muted;base.OnRenderItemText(e);}
      protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using(var pen=new Pen(Line))e.Graphics.DrawLine(pen,7,e.Item.Height/2,e.Item.Width-7,e.Item.Height/2);}
      protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e){Rectangle r=e.ImageRectangle;using(var pen=new Pen(SystemInformation.HighContrast && e.Item.Selected?SystemColors.HighlightText:Accent,1.7f))e.Graphics.DrawLines(pen,new[]{new Point(r.Left+2,r.Top+r.Height/2),new Point(r.Left+r.Width/2-1,r.Bottom-3),new Point(r.Right-2,r.Top+3)});}
      protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e){e.ArrowColor=SystemInformation.HighContrast && e.Item.Selected?SystemColors.HighlightText:e.Item.Enabled?Muted:Line;base.OnRenderArrow(e);}
    }
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)]static extern int SetWindowTheme(IntPtr window,string application,string classes);
    public static void Apply(Control root){ApplyControl(root,false);root.Invalidate(true);}
    static void ApplyControl(Control control,bool navigation){
      navigation=navigation || Convert.ToString(control.Tag)=="nav";
      FontStyle style=control.Font.Style;
      if(control is Label && control.Font.SizeInPoints>=11 && (Convert.ToString(control.Tag)=="ink" || navigation))style=FontStyle.Bold;
      control.Font=Font(control.Font.SizeInPoints,style);
      if(control is TextBox){control.BackColor=LogSurface;control.ForeColor=LogInk;control.Font=FontFor("Consolas",9,FontStyle.Regular);}
      else if(control is Panel || control is Form)control.BackColor=navigation?Nav:control is Card?Canvas:Convert.ToString(control.Tag)=="surface"?Surface:Canvas;
      else if(control is SpeedSlider)control.BackColor=Surface;
      else if(control is Label){string role=Convert.ToString(control.Tag);control.ForeColor=navigation?(role=="nav-title"?NavInk:NavMuted):role=="accent"?Accent:role=="muted"?Muted:Ink;control.BackColor=Color.Transparent;}
      if(control.IsHandleCreated){try{if(control is Form){int dark=UiPreferences.Dark?1:0,corner=2;DwmSetWindowAttribute(control.Handle,20,ref dark,4);DwmSetWindowAttribute(control.Handle,33,ref corner,4);}else if(control is TextBox || control is ScrollableControl)SetWindowTheme(control.Handle,UiPreferences.Dark?"DarkMode_Explorer":"Explorer",null);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
      foreach(Control child in control.Controls)ApplyControl(child,navigation);control.Invalidate();
    }
    public static GraphicsPath Round(RectangleF r,float radius){float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));var p=new GraphicsPath();if(d<=0){p.AddRectangle(r);return p;}p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
  }
  sealed class Card:Panel {
    public bool Emphasis=false;
    public Card(){DoubleBuffered=true;BackColor=Palette.Canvas;Size=new Size(738,100);Padding=new Padding(22);Margin=new Padding(0,0,0,12);SetStyle(ControlStyles.ResizeRedraw,true);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Width<4 || Height<4)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float s=e.Graphics.DpiX/96;
      if(!SystemInformation.HighContrast && !UiPreferences.Dark)using(var shadow=Palette.Round(new RectangleF(1.5f*s,2.5f*s,Width-3*s,Height-3*s),18*s))using(var brush=new SolidBrush(Color.FromArgb(5,25,32,50)))e.Graphics.FillPath(brush,shadow);
      using(var path=Palette.Round(new RectangleF(.5f*s,.5f*s,Width-s,Height-2*s),18*s))using(var fill=new SolidBrush(Palette.Surface))using(var edge=new Pen(Palette.GentleEdge,s)){e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(edge,path);}
      if(Emphasis && !SystemInformation.HighContrast)using(var pen=new Pen(Color.FromArgb(UiPreferences.Dark?62:72,Palette.Accent),2*s)){pen.StartCap=pen.EndCap=LineCap.Round;e.Graphics.DrawLine(pen,26*s,2*s,Math.Max(28*s,Width-26*s),2*s);}
    }
  }
  class ActionButton:Button {
    bool hover;public string NavigationIcon="";public bool Primary{get;set;}public bool Navigation{get;set;}public bool Selected{get;set;}
    public ActionButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;Font=Palette.Font(9.5f,FontStyle.Regular);Height=38;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
      e.Graphics.Clear(Parent is Card?Palette.Surface:Parent==null?Palette.Canvas:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float s=e.Graphics.DpiX/96;
      Color fill=Navigation?(Selected?Palette.NavSelected:hover?Palette.Hover:Palette.Nav):Primary?(hover?ControlPaint.Light(Palette.Accent,.08f):Palette.Accent):hover?Palette.Hover:Palette.Surface;
      Color ink=Navigation?(Selected?(SystemInformation.HighContrast?Palette.PrimaryInk:Palette.Ink):Palette.NavMuted):Primary?Palette.PrimaryInk:Palette.Ink;
      if(!Enabled){fill=SystemInformation.HighContrast?Palette.Surface:Palette.Hover;ink=Palette.Muted;}
      using(var path=Palette.Round(new RectangleF(1,1,Width-2,Height-2),8*s))using(var brush=new SolidBrush(fill)){e.Graphics.FillPath(brush,path);if(!Navigation && !Primary)using(var pen=new Pen(Palette.GentleEdge,s))e.Graphics.DrawPath(pen,path);}
      if(Navigation){Glyphs.Draw(e.Graphics,new RectangleF(17*s,(Height-20*s)/2,20*s,20*s),NavigationIcon,Selected?(SystemInformation.HighContrast?Palette.PrimaryInk:Palette.Accent):ink);Rectangle text=new Rectangle((int)(48*s),0,Math.Max(1,Width-(int)(55*s)),Height);TextRenderer.DrawText(e.Graphics,Text,Font,text,ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);}
      else TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
      if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),ink,fill);
    }
  }
  sealed class ChoiceButton:ActionButton {
    string[] options=new string[0];int selected;
    public event EventHandler SelectionChanged;
    public IList<FontChoice> FontChoices=null;
    public string[] Options{get{return options;}set{options=value??new string[0];SelectedIndex=selected;}}
    public int SelectedIndex{get{return selected;}set{int next=Math.Max(0,Math.Min(options.Length-1,value));bool changed=selected!=next;selected=next;Text=options.Length==0?"选择…":options[selected]+"   ▾";if(changed && SelectionChanged!=null)SelectionChanged(this,EventArgs.Empty);}}
    public ChoiceButton(){AccessibleRole=AccessibleRole.ComboBox;}
    protected override void OnClick(EventArgs e){base.OnClick(e);if(options.Length==0)return;var menu=new ContextMenuStrip();Palette.StyleMenu(menu);
      for(int index=0;index<options.Length;index++){int choice=index;var item=new ToolStripMenuItem(options[index]);item.Checked=index==selected;
        if(FontChoices!=null && index<FontChoices.Count){item.Tag=FontChoices[index];item.Font=Palette.FontFor(FontChoices[index].Family,9.5f,FontStyle.Regular);}
        item.Click+=delegate{SelectedIndex=choice;};menu.Items.Add(item);
      }
      menu.Closed+=delegate{menu.Dispose();if(!IsDisposed)Focus();};menu.Show(this,new Point(0,Height));
    }
  }
  sealed class Toggle:CheckBox {
    public Toggle(){AutoSize=false;Size=new Size(48,30);Text="";Cursor=Cursors.Hand;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Parent is Card?Palette.Surface:Parent==null?Palette.Canvas:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float s=e.Graphics.DpiX/96;using(var path=Palette.Round(new RectangleF(2*s,3*s,44*s,24*s),12*s))using(var fill=new SolidBrush(Enabled?(Checked?Palette.Accent:Color.FromArgb(209,217,228)):Palette.Line))e.Graphics.FillPath(fill,path);using(var brush=new SolidBrush(Palette.PrimaryInk))e.Graphics.FillEllipse(brush,(Checked?25:5)*s,6*s,18*s,18*s);if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle);}
  }
  sealed class SpeedSlider:Control {
    int value=10;bool dragging,keyEditing;public event EventHandler ValueChanged,InteractionCompleted;
    public bool IsInteracting {get{return dragging || keyEditing;}}
    public void CancelInteraction(){dragging=keyEditing=false;Capture=false;Invalidate();}
    void FinishInteraction(){bool active=IsInteracting;dragging=keyEditing=false;if(active && InteractionCompleted!=null)InteractionCompleted(this,EventArgs.Empty);}
    public int Value{get{return value;}set{int next=Math.Max(1,Math.Min(20,value));if(next==this.value)return;this.value=next;Invalidate();AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1);if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}}
    public SpeedSlider(){Height=44;TabStop=true;Cursor=Cursors.Hand;BackColor=Palette.Surface;AccessibleName="统一速度";AccessibleRole=AccessibleRole.Slider;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);}
    void SetFromX(int x){float s;using(Graphics g=CreateGraphics())s=g.DpiX/96;float pad=14*s;Value=1+(int)Math.Round(19.0*(x-pad)/Math.Max(1,Width-2*pad));}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Focus();dragging=true;Capture=true;SetFromX(e.X);}protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging)SetFromX(e.X);}protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button!=MouseButtons.Left)return;FinishInteraction();Capture=false;}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture && dragging)FinishInteraction();}
    protected override bool IsInputKey(Keys key){return key==Keys.Left || key==Keys.Right || key==Keys.Up || key==Keys.Down || base.IsInputKey(key);}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.Modifiers!=Keys.None)return;if(e.KeyCode==Keys.Left || e.KeyCode==Keys.Down){keyEditing=true;Value--;}else if(e.KeyCode==Keys.Right || e.KeyCode==Keys.Up){keyEditing=true;Value++;}else if(e.KeyCode==Keys.Home){keyEditing=true;Value=1;}else if(e.KeyCode==Keys.End){keyEditing=true;Value=20;}else return;e.Handled=true;e.SuppressKeyPress=true;}
    protected override void OnKeyUp(KeyEventArgs e){base.OnKeyUp(e);if(keyEditing)FinishInteraction();}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);FinishInteraction();Invalidate();}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Palette.Surface);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float s=e.Graphics.DpiX/96,pad=14*s,y=Height/2f,x=pad+(Width-2*pad)*(Value-1)/19f;using(var track=new Pen(Palette.Line,6*s))using(var active=new Pen(Palette.Accent,6*s)){track.StartCap=track.EndCap=active.StartCap=active.EndCap=LineCap.Round;e.Graphics.DrawLine(track,pad,y,Width-pad,y);if(x>pad)e.Graphics.DrawLine(active,pad,y,x,y);}if(Focused)using(var halo=new SolidBrush(Palette.Soft))e.Graphics.FillEllipse(halo,x-15*s,y-15*s,30*s,30*s);using(var knob=new SolidBrush(Palette.Accent))e.Graphics.FillEllipse(knob,x-10*s,y-10*s,20*s,20*s);using(var core=new SolidBrush(Palette.PrimaryInk))e.Graphics.FillEllipse(core,x-4*s,y-4*s,8*s,8*s);}
    protected override AccessibleObject CreateAccessibilityInstance(){return new SliderAccessibility(this);}
    sealed class SliderAccessibility:ControlAccessibleObject{readonly SpeedSlider slider;public SliderAccessibility(SpeedSlider slider):base(slider){this.slider=slider;}public override string Value{get{return slider.Value.ToString();}set{int parsed;if(int.TryParse(value,out parsed))slider.Value=parsed;}}public override AccessibleRole Role{get{return AccessibleRole.Slider;}}}
  }
  static class Glyphs {
    public static void Draw(Graphics graphics,RectangleF bounds,string kind,Color ink){var state=graphics.Save();graphics.TranslateTransform(bounds.X,bounds.Y);graphics.ScaleTransform(bounds.Width/24f,bounds.Height/24f);graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var pen=new Pen(ink,1.6f)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
      if(kind=="speed"){graphics.DrawArc(pen,3,4,18,18,175,190);graphics.DrawLine(pen,12,13,17,8);graphics.DrawEllipse(pen,10.5f,11.5f,3,3);}
      else if(kind=="maintenance"){for(int row=0;row<3;row++){float y=6+row*6,x=row==1?15:8;graphics.DrawLine(pen,3,y,x-2,y);graphics.DrawLine(pen,x+2,y,21,y);graphics.DrawEllipse(pen,x-2,y-2,4,4);}}
      else if(kind=="appearance"){graphics.DrawEllipse(pen,3,3,18,18);graphics.DrawEllipse(pen,7,7,2,2);graphics.DrawEllipse(pen,14,7,2,2);graphics.DrawEllipse(pen,7,14,2,2);graphics.DrawArc(pen,12,12,6,6,0,180);}
      else if(kind=="drag"){for(int finger=0;finger<3;finger++)graphics.DrawLine(pen,7+finger*5,5,7+finger*5,14);graphics.DrawLine(pen,8,19,18,19);graphics.DrawLines(pen,new[]{new PointF(15,16),new PointF(18,19),new PointF(15,22)});}
      else if(kind=="windows"){using(var path=Palette.Round(new RectangleF(3,5,12,13),2))graphics.DrawPath(pen,path);graphics.DrawLines(pen,new[]{new PointF(9,2),new PointF(21,2),new PointF(21,15)});graphics.DrawLine(pen,3,9,15,9);}
      else if(kind=="desktops"){using(var path=Palette.Round(new RectangleF(2,6,8,12),2))graphics.DrawPath(pen,path);using(var path=Palette.Round(new RectangleF(14,6,8,12),2))graphics.DrawPath(pen,path);graphics.DrawLine(pen,8,3,16,3);graphics.DrawLine(pen,16,3,13,1);}
      else if(kind=="startup"){graphics.DrawArc(pen,4,4,16,16,-45,270);graphics.DrawLine(pen,12,2,12,12);}
      else{using(var path=Palette.Round(new RectangleF(2,3,20,17),3))graphics.DrawPath(pen,path);graphics.DrawLine(pen,7,17,17,17);for(int finger=0;finger<3;finger++)graphics.DrawLine(pen,8+finger*4,7,8+finger*4,12);}
    }graphics.Restore(state);}
  }
  sealed class ModuleIcon:Control {
    public string Kind="trackpad";
    public ModuleIcon(){TabStop=false;AccessibleRole=AccessibleRole.Graphic;Size=new Size(40,40);SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Palette.Surface);var state=e.Graphics.Save();e.Graphics.ScaleTransform(Width/48f,Height/48f);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
      Color ink=Kind=="drag"?Color.FromArgb(121,95,195):Kind=="windows"?Color.FromArgb(75,113,203):Kind=="desktops"?Color.FromArgb(174,111,52):Palette.Accent;
      if(UiPreferences.Dark)ink=ControlPaint.Light(ink,.35f);
      Color fill=UiPreferences.Dark?Color.FromArgb(45,ink):Color.FromArgb(22,ink);
      using(var path=Palette.Round(new RectangleF(0,0,48,48),14))using(var brush=new LinearGradientBrush(new Rectangle(0,0,48,48),fill,Color.FromArgb(UiPreferences.Dark?14:5,ink),45f))e.Graphics.FillPath(brush,path);
      Glyphs.Draw(e.Graphics,new RectangleF(12,12,24,24),Kind,ink);e.Graphics.Restore(state);
    }
  }
  sealed class BrandMark:Control {
    public BrandMark(){TabStop=false;Size=new Size(40,40);BackColor=Palette.Nav;}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Palette.Nav);var state=e.Graphics.Save();e.Graphics.ScaleTransform(Width/48f,Height/48f);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
      using(var path=Palette.Round(new RectangleF(0,0,48,48),14))using(var brush=new LinearGradientBrush(new Rectangle(0,0,48,48),Color.FromArgb(88,200,169),Color.FromArgb(40,146,130),45f))e.Graphics.FillPath(brush,path);
      Glyphs.Draw(e.Graphics,new RectangleF(9,9,30,30),"trackpad",Color.White);e.Graphics.Restore(state);
    }
  }
}

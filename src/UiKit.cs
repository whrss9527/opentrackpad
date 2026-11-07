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
    public static Color DisabledInk{get{return SystemInformation.HighContrast?SystemColors.GrayText:ColorOf(0x7d8594,0x8992a2);}}
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
    public static Color LogSurface{get{return Surface;}}
    public static Color LogInk{get{return Ink;}}
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
      protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=!e.Item.Enabled?DisabledInk:SystemInformation.HighContrast && (e.Item.Selected || e.Item.Pressed)?SystemColors.HighlightText:Ink;base.OnRenderItemText(e);}
      protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using(var pen=new Pen(Line))e.Graphics.DrawLine(pen,7,e.Item.Height/2,e.Item.Width-7,e.Item.Height/2);}
      protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e){Rectangle r=e.ImageRectangle;using(var pen=new Pen(!e.Item.Enabled?DisabledInk:SystemInformation.HighContrast && e.Item.Selected?SystemColors.HighlightText:Accent,1.7f))e.Graphics.DrawLines(pen,new[]{new Point(r.Left+2,r.Top+r.Height/2),new Point(r.Left+r.Width/2-1,r.Bottom-3),new Point(r.Right-2,r.Top+3)});}
      protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e){e.ArrowColor=!e.Item.Enabled?DisabledInk:SystemInformation.HighContrast && e.Item.Selected?SystemColors.HighlightText:Muted;base.OnRenderArrow(e);}
    }
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)]static extern int SetWindowTheme(IntPtr window,string application,string classes);
    public static void Apply(Control root){ApplyControl(root,false);root.Invalidate(true);}
    static void ApplyControl(Control control,bool navigation){
      navigation=navigation || Convert.ToString(control.Tag)=="nav";
      FontStyle style=control.Font.Style;
      if(control is Label && control.Font.SizeInPoints>=11 && (Convert.ToString(control.Tag)=="ink" || navigation))style=FontStyle.Bold;
      control.Font=Font(control.Font.SizeInPoints,style);
      if(control is TextBox){control.BackColor=LogSurface;control.ForeColor=LogInk;control.Font=Font(9,FontStyle.Regular);}
      else if(control is Panel || control is Form)control.BackColor=navigation?Nav:control is Card?Canvas:Convert.ToString(control.Tag)=="surface"?Surface:Canvas;
      else if(control is SpeedSlider)control.BackColor=Surface;
      else if(control is Label){string role=Convert.ToString(control.Tag);control.ForeColor=navigation?(role=="nav-title"?NavInk:NavMuted):role=="accent"?Accent:role=="muted"?Muted:Ink;control.BackColor=Color.Transparent;}
      if(control.IsHandleCreated){try{if(control is Form){int dark=!SystemInformation.HighContrast && UiPreferences.Dark?1:0,corner=2;DwmSetWindowAttribute(control.Handle,20,ref dark,4);DwmSetWindowAttribute(control.Handle,33,ref corner,4);}else if(control is TextBox || control is ScrollableControl)SetWindowTheme(control.Handle,SystemInformation.HighContrast?null:UiPreferences.Dark?"DarkMode_Explorer":"Explorer",null);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
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
    bool hover,mousePressed,keyPressed;public string NavigationIcon="";public bool Primary{get;set;}public bool Navigation{get;set;}public bool Selected{get;set;}
    public ActionButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;Font=Palette.Font(9.5f,FontStyle.Regular);Height=38;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(Enabled && e.Button==MouseButtons.Left){mousePressed=true;hover=ClientRectangle.Contains(e.Location);Invalidate();}base.OnMouseDown(e);}
    protected override void OnMouseMove(MouseEventArgs e){if(mousePressed){bool inside=ClientRectangle.Contains(e.Location);if(hover!=inside){hover=inside;Invalidate();}}base.OnMouseMove(e);}
    protected override void OnMouseUp(MouseEventArgs e){if(e.Button==MouseButtons.Left){mousePressed=false;Invalidate();}base.OnMouseUp(e);}
    protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture){mousePressed=false;Invalidate();}base.OnMouseCaptureChanged(e);}
    protected override void OnKeyDown(KeyEventArgs e){if(Enabled && e.Modifiers==Keys.None && e.KeyCode==Keys.Space){keyPressed=true;Invalidate();}base.OnKeyDown(e);}
    protected override void OnKeyUp(KeyEventArgs e){if(e.KeyCode==Keys.Space){keyPressed=false;Invalidate();}base.OnKeyUp(e);}
    void ResetPress(){mousePressed=keyPressed=false;Invalidate();}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){ResetPress();base.OnLostFocus(e);}
    protected override void OnEnabledChanged(EventArgs e){if(!Enabled){hover=false;ResetPress();}base.OnEnabledChanged(e);Invalidate();}
    protected override void OnVisibleChanged(EventArgs e){if(!Visible){hover=false;ResetPress();}base.OnVisibleChanged(e);}
    protected override void OnChangeUICues(UICuesEventArgs e){base.OnChangeUICues(e);if(e.ChangeFocus)Invalidate();}
    protected override bool IsInputKey(Keys key){Keys code=key&Keys.KeyCode;if(Navigation && (key&Keys.Modifiers)==Keys.None && (code==Keys.Up || code==Keys.Down || code==Keys.Home || code==Keys.End))return true;return base.IsInputKey(key);}
    protected override void OnPaint(PaintEventArgs e){
      e.Graphics.Clear(Parent is Card?Palette.Surface:Parent==null?Palette.Canvas:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float s=e.Graphics.DpiX/96;
      bool pressed=Enabled && (keyPressed || mousePressed && hover);
      Color fill=Navigation?(Selected?Palette.NavSelected:hover?Palette.Hover:Palette.Nav):Primary?(hover?ControlPaint.Light(Palette.Accent,.08f):Palette.Accent):hover?Palette.Hover:Palette.Surface;
      Color ink=Navigation?(Selected?(SystemInformation.HighContrast?Palette.PrimaryInk:Palette.Ink):Palette.NavMuted):Primary?Palette.PrimaryInk:Palette.Ink;
      if(pressed && !SystemInformation.HighContrast)fill=Primary?(UiPreferences.Dark?Color.FromArgb(91,198,173):Color.FromArgb(16,105,88)):(UiPreferences.Dark?Color.FromArgb(60,69,83):Color.FromArgb(224,231,238));
      if(SystemInformation.HighContrast && Enabled && (Primary || Selected && Navigation || hover || pressed)){fill=SystemColors.Highlight;ink=SystemColors.HighlightText;}
      if(!Enabled){fill=SystemInformation.HighContrast?Palette.Surface:Palette.Hover;ink=Palette.DisabledInk;}
      using(var path=Palette.Round(new RectangleF(1,1,Width-2,Height-2),8*s))using(var brush=new SolidBrush(fill)){e.Graphics.FillPath(brush,path);if(!Navigation && !Primary)using(var pen=new Pen(Palette.GentleEdge,s))e.Graphics.DrawPath(pen,path);}
      int offset=pressed?(int)Math.Round(s):0;
      if(Navigation){Glyphs.Draw(e.Graphics,new RectangleF(17*s,(Height-20*s)/2+offset,20*s,20*s),NavigationIcon,!Enabled?ink:Selected && !SystemInformation.HighContrast?Palette.Accent:ink);Rectangle text=new Rectangle((int)(48*s),offset,Math.Max(1,Width-(int)(55*s)),Height);TextRenderer.DrawText(e.Graphics,Text,Font,text,ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);}
      else {Rectangle text=ClientRectangle;text.Offset(0,offset);TextRenderer.DrawText(e.Graphics,Text,Font,text,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);}
      if(Enabled && Focused && ShowFocusCues){Color ring=Primary || SystemInformation.HighContrast?ink:Palette.Accent;using(var path=Palette.Round(new RectangleF(3.5f*s,3.5f*s,Width-7*s,Height-7*s),5.5f*s))using(var pen=new Pen(ring,1.5f*s))e.Graphics.DrawPath(pen,path);}
    }
  }
  sealed class ChoiceButton:ActionButton {
    string[] options=new string[0];int selected;ContextMenuStrip choices;bool disposing;
    public event EventHandler SelectionChanged;
    public IList<FontChoice> FontChoices=null;
    public string[] Options{get{return options;}set{options=value??new string[0];SelectedIndex=selected;}}
    public int SelectedIndex{get{return selected;}set{int next=Math.Max(0,Math.Min(options.Length-1,value));bool changed=selected!=next;selected=next;Text=options.Length==0?"选择…":options[selected]+"   ▾";if(IsHandleCreated)AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1);if(changed && SelectionChanged!=null)SelectionChanged(this,EventArgs.Empty);}}
    public ChoiceButton(){AccessibleRole=AccessibleRole.ComboBox;AccessibleDescription="使用方向键选择，按 Alt+向下键或 F4 展开选项。";}
    protected override void OnClick(EventArgs e){base.OnClick(e);ShowChoices();}
    void ShowChoices(){
      if(!Enabled || IsDisposed || disposing || options.Length==0)return;
      if(choices!=null){if(choices.Visible){choices.Close(ToolStripDropDownCloseReason.Keyboard);return;}choices.Dispose();choices=null;}
      var menu=new ChoiceMenu();choices=menu;Palette.StyleMenu(menu);menu.MinimumSize=new Size(Width,0);
      menu.Opening+=delegate{menu.Font=Font;foreach(ToolStripItem item in menu.Items)if(!(item.Tag is FontChoice))item.Font=Font;};
      for(int index=0;index<options.Length;index++){int choice=index;var item=new ToolStripMenuItem(options[index]);item.Checked=index==selected;
        if(FontChoices!=null && index<FontChoices.Count){item.Tag=FontChoices[index];item.Font=Palette.FontFor(FontChoices[index].Family,Font.SizeInPoints,FontStyle.Regular);}
        item.Click+=delegate{SelectedIndex=choice;};menu.Items.Add(item);
      }
      // Dispose after the close stack has returned, on the next open or with the owner.
      menu.Closed+=delegate(object sender,ToolStripDropDownClosedEventArgs e){if(!IsDisposed && !disposing){AccessibilityNotifyClients(AccessibleEvents.StateChange,-1);if(Enabled && Visible && (e.CloseReason==ToolStripDropDownCloseReason.ItemClicked || e.CloseReason==ToolStripDropDownCloseReason.Keyboard))Focus();}};
      menu.Show(this,new Point(0,Height));menu.Items[selected].Select();AccessibilityNotifyClients(AccessibleEvents.StateChange,-1);
    }
    protected override bool IsInputKey(Keys key){Keys code=key&Keys.KeyCode;if((key&Keys.Modifiers)==Keys.None && (code==Keys.Left || code==Keys.Right || code==Keys.Up || code==Keys.Down || code==Keys.Home || code==Keys.End))return true;return base.IsInputKey(key);}
    protected override bool ProcessCmdKey(ref Message message,Keys key){if(key==Keys.F4 || key==(Keys.Alt|Keys.Down) || key==(Keys.Alt|Keys.Up)){if(Enabled)ShowChoices();return true;}return base.ProcessCmdKey(ref message,key);}
    protected override void OnKeyDown(KeyEventArgs e){
      if(Enabled && ((e.Modifiers==Keys.Alt && (e.KeyCode==Keys.Down || e.KeyCode==Keys.Up)) || e.Modifiers==Keys.None && e.KeyCode==Keys.F4)){ShowChoices();e.Handled=e.SuppressKeyPress=true;return;}
      if(Enabled && e.Modifiers==Keys.None){int next=selected;if(e.KeyCode==Keys.Left || e.KeyCode==Keys.Up)next--;else if(e.KeyCode==Keys.Right || e.KeyCode==Keys.Down)next++;else if(e.KeyCode==Keys.Home)next=0;else if(e.KeyCode==Keys.End)next=options.Length-1;else{base.OnKeyDown(e);return;}SelectedIndex=next;e.Handled=e.SuppressKeyPress=true;return;}
      base.OnKeyDown(e);
    }
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);if(!Enabled && choices!=null)choices.Close();Invalidate();}
    protected override void Dispose(bool disposing){this.disposing=disposing;if(disposing && choices!=null){var menu=choices;choices=null;menu.Dispose();}base.Dispose(disposing);}
    protected override AccessibleObject CreateAccessibilityInstance(){return new ChoiceAccessibility(this);}
    sealed class ChoiceMenu:ContextMenuStrip {
      protected override bool ProcessCmdKey(ref Message message,Keys key){if(key==Keys.F4 || key==(Keys.Alt|Keys.Down) || key==(Keys.Alt|Keys.Up)){Close(ToolStripDropDownCloseReason.Keyboard);return true;}return base.ProcessCmdKey(ref message,key);}
    }
    sealed class ChoiceAccessibility:ControlAccessibleObject {
      readonly ChoiceButton owner;
      public ChoiceAccessibility(ChoiceButton owner):base(owner){this.owner=owner;}
      public override AccessibleRole Role{get{return AccessibleRole.ComboBox;}}
      public override AccessibleStates State{get{return base.State|AccessibleStates.Focusable|AccessibleStates.HasPopup|(owner.choices!=null && owner.choices.Visible?AccessibleStates.Expanded:AccessibleStates.Collapsed);}}
      public override string Value{get{return owner.options.Length==0?String.Empty:owner.options[owner.selected];}set{if(!owner.Enabled)return;for(int index=0;index<owner.options.Length;index++)if(owner.options[index]==value){owner.SelectedIndex=index;break;}}}
      public override string DefaultAction{get{return owner.choices!=null && owner.choices.Visible?"收起选项":"展开选项";}}
      public override void DoDefaultAction(){owner.ShowChoices();}
    }
  }
  sealed class Toggle:CheckBox {
    public Toggle(){AutoSize=false;Size=new Size(48,30);Text="";Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.CheckButton;AccessibleDescription="按空格键切换。";SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
      Color background=Parent is Card?Palette.Surface:Parent==null?Palette.Canvas:Parent.BackColor;e.Graphics.Clear(background);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float s=e.Graphics.DpiX/96;
      Color rail=Checked?Palette.Accent:UiPreferences.Dark?Palette.Line:Color.FromArgb(209,217,228),thumb=Checked?Palette.PrimaryInk:UiPreferences.Dark?Palette.Ink:Color.White;
      if(SystemInformation.HighContrast){rail=Checked?SystemColors.Highlight:SystemColors.Control;thumb=Checked?SystemColors.HighlightText:SystemColors.ControlText;}
      if(!Enabled){rail=SystemInformation.HighContrast?SystemColors.Control:Palette.Line;thumb=Palette.DisabledInk;}
      using(var path=Palette.Round(new RectangleF(2*s,3*s,44*s,24*s),12*s))using(var fill=new SolidBrush(rail)){e.Graphics.FillPath(fill,path);if(SystemInformation.HighContrast)using(var edge=new Pen(Enabled?SystemColors.WindowText:SystemColors.GrayText,s))e.Graphics.DrawPath(edge,path);}
      using(var brush=new SolidBrush(thumb))e.Graphics.FillEllipse(brush,(Checked?25:5)*s,6*s,18*s,18*s);
      if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle,Palette.Ink,background);
    }
    protected override AccessibleObject CreateAccessibilityInstance(){return new ToggleAccessibility(this);}
    sealed class ToggleAccessibility:CheckBoxAccessibleObject {
      readonly Toggle owner;
      public ToggleAccessibility(Toggle owner):base(owner){this.owner=owner;}
      public override void DoDefaultAction(){if(owner.Enabled && !owner.IsDisposed)base.DoDefaultAction();}
    }
  }
  sealed class SpeedSlider:Control {
    // Unrelated key releases must not commit a still-held adjustment key.
    int value=10;bool dragging;readonly HashSet<Keys> editingKeys=new HashSet<Keys>();public event EventHandler ValueChanged,InteractionCompleted;
    public bool IsInteracting {get{return dragging || editingKeys.Count!=0;}}
    public void CancelInteraction(){dragging=false;editingKeys.Clear();Capture=false;Invalidate();}
    void FinishInteraction(){bool active=IsInteracting;CancelInteraction();if(active && InteractionCompleted!=null)InteractionCompleted(this,EventArgs.Empty);}
    public int Value{get{return value;}set{int next=Math.Max(1,Math.Min(20,value));if(next==this.value)return;this.value=next;Invalidate();AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1);if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}}
    public SpeedSlider(){Height=44;TabStop=true;Cursor=Cursors.Hand;BackColor=Palette.Surface;AccessibleName="统一速度";AccessibleRole=AccessibleRole.Slider;AccessibleDescription="速度范围 1 到 20；方向键微调，Home 最低，End 最高。";SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);}
    void SetFromX(int x){float s;using(Graphics g=CreateGraphics())s=g.DpiX/96;float pad=14*s;Value=1+(int)Math.Round(19.0*(x-pad)/Math.Max(1,Width-2*pad));}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(!Enabled || e.Button!=MouseButtons.Left)return;Focus();dragging=true;Capture=true;SetFromX(e.X);}protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging && Enabled)SetFromX(e.X);}protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button!=MouseButtons.Left || !dragging)return;FinishInteraction();}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture && dragging)FinishInteraction();}
    static bool AdjustmentKey(Keys key){return key==Keys.Left || key==Keys.Right || key==Keys.Up || key==Keys.Down || key==Keys.Home || key==Keys.End;}
    protected override bool IsInputKey(Keys key){return (key&Keys.Modifiers)==Keys.None && AdjustmentKey(key&Keys.KeyCode) || base.IsInputKey(key);}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(!Enabled || e.Modifiers!=Keys.None || !AdjustmentKey(e.KeyCode))return;editingKeys.Add(e.KeyCode);if(e.KeyCode==Keys.Left || e.KeyCode==Keys.Down)Value--;else if(e.KeyCode==Keys.Right || e.KeyCode==Keys.Up)Value++;else if(e.KeyCode==Keys.Home)Value=1;else Value=20;e.Handled=e.SuppressKeyPress=true;}
    protected override void OnKeyUp(KeyEventArgs e){base.OnKeyUp(e);if(!editingKeys.Remove(e.KeyCode))return;if(editingKeys.Count==0 && !dragging){if(InteractionCompleted!=null)InteractionCompleted(this,EventArgs.Empty);}e.Handled=true;}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);FinishInteraction();Invalidate();}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);if(!Enabled)FinishInteraction();Invalidate();}
    protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(!Visible)FinishInteraction();}
    protected override void Dispose(bool disposing){if(disposing)CancelInteraction();base.Dispose(disposing);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Palette.Surface);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float s=e.Graphics.DpiX/96,pad=14*s,y=Height/2f,x=pad+(Width-2*pad)*(Value-1)/19f;Color accent=Enabled?Palette.Accent:Palette.DisabledInk;using(var track=new Pen(Palette.Line,6*s))using(var active=new Pen(accent,6*s)){track.StartCap=track.EndCap=active.StartCap=active.EndCap=LineCap.Round;e.Graphics.DrawLine(track,pad,y,Width-pad,y);if(x>pad)e.Graphics.DrawLine(active,pad,y,x,y);}if(Focused && Enabled)using(var halo=new SolidBrush(Palette.Soft))e.Graphics.FillEllipse(halo,x-15*s,y-15*s,30*s,30*s);using(var knob=new SolidBrush(accent))e.Graphics.FillEllipse(knob,x-10*s,y-10*s,20*s,20*s);using(var core=new SolidBrush(Enabled?Palette.PrimaryInk:Palette.Surface))e.Graphics.FillEllipse(core,x-4*s,y-4*s,8*s,8*s);if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-1,-1),Palette.Ink,Palette.Surface);}
    protected override AccessibleObject CreateAccessibilityInstance(){return new SliderAccessibility(this);}
    sealed class SliderAccessibility:ControlAccessibleObject{readonly SpeedSlider slider;public SliderAccessibility(SpeedSlider slider):base(slider){this.slider=slider;}public override string Value{get{return slider.Value.ToString(CultureInfo.InvariantCulture);}set{int parsed;if(!slider.Enabled || !int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out parsed))return;slider.CancelInteraction();slider.Value=parsed;if(slider.InteractionCompleted!=null)slider.InteractionCompleted(slider,EventArgs.Empty);}}public override AccessibleRole Role{get{return AccessibleRole.Slider;}}public override AccessibleStates State{get{return base.State|AccessibleStates.Focusable;}}}
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
  // Shared vector artwork: the icon generator calls the same geometry as the sidebar.
  public static class BrandArtwork {
    public static void Draw(Graphics graphics,RectangleF bounds){
      var state=graphics.Save();graphics.TranslateTransform(bounds.X,bounds.Y);graphics.ScaleTransform(bounds.Width/64f,bounds.Height/64f);graphics.SmoothingMode=SmoothingMode.AntiAlias;
      using(var path=Palette.Round(new RectangleF(.5f,.5f,63,63),18))using(var brush=new LinearGradientBrush(new RectangleF(0,0,64,64),Color.FromArgb(95,205,177),Color.FromArgb(37,143,128),45f))graphics.FillPath(brush,path);
      using(var path=Palette.Round(new RectangleF(1,1,62,62),17.5f))using(var edge=new Pen(Color.FromArgb(38,255,255,255),1))graphics.DrawPath(edge,path);
      using(var pen=new Pen(Color.White,3.25f)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
        using(var pad=Palette.Round(new RectangleF(15,17,34,30),5.5f))graphics.DrawPath(pen,pad);
        for(int finger=0;finger<3;finger++)graphics.DrawLine(pen,24+finger*8,24,24+finger*8,32);
        graphics.DrawLine(pen,24,40,40,40);
      }
      graphics.Restore(state);
    }
  }
  sealed class BrandMark:Control {
    public BrandMark(){TabStop=false;Size=new Size(40,40);BackColor=Palette.Nav;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Palette.Nav);BrandArtwork.Draw(e.Graphics,ClientRectangle);
    }
  }
}

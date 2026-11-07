using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Reflection;using System.Security.Cryptography;using System.Web.Script.Serialization;using System.Windows.Forms;using ThreeFingerDrag;using OpenTrackpadUi;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenTrackpadShared;
class UiSmoke {
  static List<string> checks=new List<string>();static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
  static IEnumerable<Control> Walk(Control root){foreach(Control child in root.Controls){yield return child;foreach(Control nested in Walk(child))yield return nested;}}
  static ActionButton Button(Form form,string text){foreach(var c in Walk(form))if(c is ActionButton && c.Text.StartsWith(text))return (ActionButton)c;throw new Exception("Missing button "+text);}
  static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));}
  [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
  [DllImport("user32.dll")]static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
  [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr window);
  [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
  [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out NativeRect rect);
  [DllImport("user32.dll")]static extern bool RedrawWindow(IntPtr window,IntPtr update,IntPtr region,uint flags);
  [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
  [DllImport("gdi32.dll")]static extern bool BitBlt(IntPtr target,int x,int y,int width,int height,IntPtr source,int sourceX,int sourceY,uint operation);
  [DllImport("gdi32.dll")]static extern uint GetPixel(IntPtr dc,int x,int y);
  [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int Left,Top,Right,Bottom;}
  static object Field(object value,string name){return value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);}
  static T Named<T>(Control root,string name) where T:Control{foreach(Control control in Walk(root))if(control.Name==name)return (T)control;throw new InvalidOperationException("Missing control "+name);}
  static void Mouse(Control control,string method,MouseButtons buttons,int x,int y){control.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(control,new object[]{new MouseEventArgs(buttons,1,x,y,0)});}
  static uint ButtonFill(ActionButton button){button.Update();IntPtr dc=GetDC(button.Handle);try{return GetPixel(dc,button.Width/2,Math.Max(2,button.Height/4));}finally{ReleaseDC(button.Handle,dc);}}
  static void Command(SettingsForm form,Keys key){typeof(SettingsForm).GetMethod("ProcessCmdKey",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{new Message(),key});}
  // Route only to this process's own control, with the same preprocessing used by WinForms.
  static void OwnKey(Control control,Keys key,bool released){
    if(control.FindForm()==null || control.FindForm().GetType()!=typeof(SettingsForm))throw new InvalidOperationException("Only test settings controls may receive keys");
    Message message=Message.Create(control.Handle,released?0x101:0x100,new IntPtr((int)key),IntPtr.Zero);
    if(released || !control.PreProcessMessage(ref message))SendMessage(control.Handle,message.Msg,message.WParam,message.LParam);
  }
  static object CaptureClient(SettingsForm form,string path){
    int paints=0;PaintEventHandler observer=delegate{paints++;};var observed=new List<Control>{form};foreach(Control control in Walk(form))observed.Add(control);foreach(Control control in observed)control.Paint+=observer;
    form.Invalidate(true);RedrawWindow(form.Handle,IntPtr.Zero,IntPtr.Zero,0x181);form.Update();Application.DoEvents();
    // The rectangle and DC must use the window's own rendering units. A PMv2
    // rectangle paired with an unaware DC would copy padding beyond its buffer.
    IntPtr previous=SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(form.Handle));
    try{
      NativeRect rect;if(!GetClientRect(form.Handle,out rect))throw new InvalidOperationException("Cannot read the test window client rectangle");
      int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
      if(width<1 || height<1)throw new InvalidOperationException("The test window has no paintable client area");
      using(var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb)){
        IntPtr source=GetDC(form.Handle);if(source==IntPtr.Zero)throw new InvalidOperationException("Cannot acquire the test window client DC");
        try{using(Graphics graphics=Graphics.FromImage(bitmap)){IntPtr target=graphics.GetHdc();try{if(!BitBlt(target,0,0,width,height,source,0,0,0x00CC0020))throw new InvalidOperationException("Native client capture failed");}finally{graphics.ReleaseHdc(target);}}}
        finally{ReleaseDC(form.Handle,source);}
        var colors=new HashSet<int>();for(int x=0;x<width;x+=Math.Max(1,width/32))for(int y=0;y<height;y+=Math.Max(1,height/24))colors.Add(bitmap.GetPixel(x,y).ToArgb());
        if(colors.Count<5 || paints==0)throw new InvalidOperationException("The native capture did not contain a painted settings window");
        bitmap.Save(path,ImageFormat.Png);
        return new{path=Path.GetFileName(path),width=width,height=height,paintEvents=paints,sampledColors=colors.Count,method="WM_PAINT + own client GetDC/BitBlt in window DPI context"};
      }
    }finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);foreach(Control control in observed)control.Paint-=observer;}
  }
  static string Describe(Control control){return (control.Name.Length>0?control.Name:control.GetType().Name)+" ["+control.Text.Replace("\r"," ").Replace("\n"," ")+"]";}
  static bool Auditable(Control control){return control.Visible && (control is Label || control is ActionButton || control is Toggle || control is SpeedSlider || control is FlowLayoutPanel);}
  static List<string> AuditLayout(SettingsForm form){
    var problems=new List<string>();
    foreach(Control parent in Walk(form)){
      var siblings=new List<Control>();foreach(Control child in parent.Controls)if(Auditable(child))siblings.Add(child);
      foreach(Control child in siblings){
        Rectangle bounds=child.Bounds;if(bounds.Left<0 || bounds.Right>parent.ClientSize.Width+1 || bounds.Top<0 || bounds.Bottom>parent.ClientSize.Height+1)problems.Add("Outside parent: "+Describe(child)+" "+bounds+" / "+parent.ClientSize);
        var button=child as ActionButton;
        if(button!=null){using(Graphics g=button.CreateGraphics()){
          Size needed=TextRenderer.MeasureText(g,button.Text,button.Font,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
          int icon=button.Navigation?(int)Math.Ceiling(55*g.DpiX/96):4;
          if(needed.Width+icon>button.ClientSize.Width || needed.Height+2>button.ClientSize.Height)problems.Add("Button text clipped: "+Describe(button)+" needs "+needed+" / "+button.ClientSize);
        }}
        var label=child as Label;
        if(label!=null && Convert.ToString(label.Tag)=="nav-title"){
          Size needed=label.GetPreferredSize(new Size(int.MaxValue,int.MaxValue));
          if(needed.Width>label.ClientSize.Width || needed.Height>label.ClientSize.Height)problems.Add("Brand text clipped: "+Describe(label)+" needs "+needed+" / "+label.ClientSize);
        }
        if(label!=null && (label.Name=="settingTitle" || Convert.ToString(label.Tag)=="headline" || label.Font.SizeInPoints>=20)){
          using(Graphics g=label.CreateGraphics()){
            Size needed=TextRenderer.MeasureText(g,label.Text,label.Font,new Size(Math.Max(1,label.Width),int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
            if(needed.Height>label.Height+1)problems.Add("Title text clipped: "+Describe(label)+" needs "+needed+" / "+label.ClientSize);
          }
        }
      }
      for(int a=0;a<siblings.Count;a++)for(int b=a+1;b<siblings.Count;b++){
        Rectangle overlap=Rectangle.Intersect(siblings[a].Bounds,siblings[b].Bounds);
        if(overlap.Width>1 && overlap.Height>1)problems.Add("Overlapping controls: "+Describe(siblings[a])+" and "+Describe(siblings[b])+" "+overlap);
      }
    }
    return problems;
  }
  static bool ReachLastAction(SettingsForm form,int page){
    Control action=Button(form,page==1?"收起到托盘":page==2?"退出后台":"保存外观");
    for(Control ancestor=action.Parent;ancestor!=null;ancestor=ancestor.Parent){
      var host=ancestor as ScrollableControl;if(host==null || !host.AutoScroll)continue;
      host.ScrollControlIntoView(action);Application.DoEvents();
      Rectangle visibleBounds=new Rectangle(host.PointToClient(action.PointToScreen(Point.Empty)),action.Size);
      return host.ClientRectangle.Contains(visibleBounds);
    }
    return false;
  }
  static void LayoutMatrix(string output,string context){
    var cases=new List<object>();var problems=new List<string>();var fonts=new List<string>();uint dpi=0;float graphicsDpi=0;
    string screenshots=Path.Combine(output,"native-"+context);Directory.CreateDirectory(screenshots);
    foreach(FontChoice font in UiPreferences.Fonts)fonts.Add(font.Key);
    foreach(string theme in new[]{"light","dark"})foreach(string font in fonts){
      UiPreferences.Preview(new AppearanceSettings{Theme=theme,Font=font});
      using(var form=new SettingsForm(null,null)){
        form.Show();Application.DoEvents();dpi=GetDpiForWindow(form.Handle);using(Graphics graphics=form.CreateGraphics())graphicsDpi=graphics.DpiX;
        Size normal=form.Size;
        foreach(string size in new[]{"normal","minimum"}){
          form.Size=size=="normal"?normal:form.MinimumSize;Application.DoEvents();
          foreach(int page in new[]{1,2,3}){
            Command(form,Keys.Control|(page==1?Keys.D1:page==2?Keys.D2:Keys.D3));Application.DoEvents();
            foreach(Control control in Walk(form)){var scroll=control as ScrollableControl;if(scroll!=null && scroll.AutoScroll)scroll.AutoScrollPosition=Point.Empty;}
            Application.DoEvents();var errors=AuditLayout(form);string id=context+"-"+theme+"-"+font+"-"+size+"-page"+page;
            object capture=CaptureClient(form,Path.Combine(screenshots,id+".png"));
            bool lastActionVisible=ReachLastAction(form,page);if(!lastActionVisible)errors.Add("The last action cannot be fully reached inside the scrolling viewport");
            object bottomCapture=size=="minimum"?CaptureClient(form,Path.Combine(screenshots,id+"-bottom.png")):null;
            foreach(string error in errors)problems.Add(id+": "+error);
            object layoutDetails=null;if(errors.Count>0 && page==2){var grid=(TableLayoutPanel)Field(form,"logLayout");var heading=(Control)Field(form,"logHeading");layoutDetails=new{row0Height=grid.RowStyles[0].Height,layoutClientSize=grid.ClientSize.ToString(),headingBounds=heading.Bounds.ToString(),logBounds=Named<TextBox>(form,"diagnosticLog").Bounds.ToString()};}
            cases.Add(new{id=id,theme=theme,font=font,page=page,size=size,actualDpi=dpi,graphicsDpi=graphicsDpi,clientWidth=form.ClientSize.Width,clientHeight=form.ClientSize.Height,minimumWidth=form.MinimumSize.Width,lastActionVisible=lastActionVisible,issues=errors,layoutDetails=layoutDetails,capture=capture,bottomCapture=bottomCapture});
          }
        }
        form.RequestDispose();
      }
    }
    string path=Path.Combine(output,"layout-"+context+".json");
    File.WriteAllText(path,new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(new{success=problems.Count==0,context=context,actualDpi=dpi,graphicsDpi=graphicsDpi,fonts=fonts,cases=cases,issues=problems,globalInputInjected=false,systemDisplaySettingsChanged=false,coverage="Real process DPI awareness contexts on this desktop; other desktop scales are not emulated."}));
    if(problems.Count>0)throw new InvalidOperationException(problems.Count+" layout boundary issues; inspect "+path);
  }
  static void AppearanceStorageTests(string root){
    string path=Path.Combine(root,"state","appearance.json"),recovery=OpenTrackpadShared.AtomicJson.RecoveryPath(path);
    OpenTrackpadShared.AtomicJson.Write(path,new Dictionary<string,object>{{"version",1},{"theme","light"},{"font","auto"},{"extension",42}});
    UiPreferences.Reload();Check(UiPreferences.Current.Theme=="light","Appearance preferences load from the isolated fixture");
    UiPreferences.Save(new AppearanceSettings{Theme="dark",Font="auto"});
    var data=(Dictionary<string,object>)OpenTrackpadShared.AtomicJson.Read(path,null).Value;
    Check(Convert.ToInt32(data["extension"])==42,"Appearance saves preserve unknown preference fields");
    Check(File.Exists(recovery),"Appearance saves maintain a recovery copy");
    UiPreferences.Reload();Check(UiPreferences.Current.Theme=="dark" && UiPreferences.Current.Font=="auto","Saved appearance survives reload");
    File.WriteAllText(path,"{broken");UiPreferences.Reload();Check(UiPreferences.Current.Theme=="dark" && UiPreferences.LoadWarning.Length>0,"A damaged appearance file uses its recovery copy with a visible warning");
    string before=File.ReadAllText(path);bool failed=false;
    using(var locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){try{UiPreferences.Save(new AppearanceSettings{Theme="light",Font="auto"});}catch(IOException){failed=true;}}
    Check(failed && File.ReadAllText(path)==before && UiPreferences.Current.Theme=="dark","A failed appearance save preserves the file and last working style");
    UiPreferences.Save(new AppearanceSettings{Theme="light",Font="auto"});
    Check(Directory.GetFiles(Path.GetDirectoryName(path),"appearance.damaged-*.json").Length>0,"Repairing appearance retains the original damaged file");
    OpenTrackpadShared.AtomicJson.Write(path,new Dictionary<string,object>{{"version",1},{"theme","light"},{"font","missing-font-test"}});before=Hash(path);UiPreferences.Reload();
    Check(UiPreferences.Current.Font=="auto" && Hash(path)==before,"Unavailable fonts fall back without rewriting preferences");
    File.WriteAllText(path,"broken main");File.WriteAllText(recovery,"broken mirror");UiPreferences.Reload();
    Check(UiPreferences.LoadWarning.Length>0 && File.ReadAllText(path)=="broken main" && File.ReadAllText(recovery)=="broken mirror","Unreadable appearance files are reported without automatic overwriting");
  }
  static void CheckTabTraversal(SettingsForm form,Control first,Control required,string name){
    first.Focus();Application.DoEvents();Control current=first;bool found=false;var seen=new HashSet<Control>();
    for(int step=0;step<100;step++){
      if(!form.SelectNextControl(current,true,true,true,true))break;
      Control focused=null;foreach(Control control in Walk(form))if(control.Focused){focused=control;break;}
      if(focused==null || !focused.Visible || !focused.Enabled || !focused.TabStop)throw new InvalidOperationException("Tab reached a hidden, disabled or noninteractive control");
      if(focused==required)found=true;
      if(!seen.Add(focused))break;current=focused;
    }
    Check(found,name);
  }
  static void InteractionTests(){
    using(var logger=new Logger{SuppressDisk=true}){
      for(int index=0;index<150;index++)logger.Info("isolated diagnostic fixture line "+index.ToString("D3"));
      using(var form=new SettingsForm(null,logger)){
        form.Show();Application.DoEvents();
        var home=Button(form,"速度与手势");var diagnostics=Button(form,"诊断与维护");var appearance=Button(form,"外观与体验");
        home.Focus();OwnKey(home,Keys.Down,false);OwnKey(home,Keys.Down,true);
        var log=Named<TextBox>(form,"diagnosticLog");Check(log.Visible && diagnostics.Selected && diagnostics.Focused,"Sidebar Down opens the next page and transfers focus to its navigation item");
        OwnKey(diagnostics,Keys.End,false);OwnKey(appearance,Keys.End,true);Check(appearance.Selected && Named<ChoiceButton>(form,"themeChoice").Visible,"Sidebar End reaches the final page");
        OwnKey(appearance,Keys.Down,false);OwnKey(appearance,Keys.Down,true);Check(appearance.Selected,"Sidebar navigation clamps at the last page");
        OwnKey(appearance,Keys.Home,false);OwnKey(home,Keys.Home,true);OwnKey(home,Keys.Up,false);OwnKey(home,Keys.Up,true);Check(home.Selected,"Sidebar Home reaches the first page and Up stays within its boundary");
        Command(form,Keys.Control|Keys.D2);log.Focus();OwnKey(log,Keys.Home,false);OwnKey(log,Keys.Home,true);Check(diagnostics.Selected && log.Visible,"Editing navigation keys inside logs do not switch sidebar pages");

        int copied=0;string copiedText=null;form.CopyDiagnosticText=delegate(string text){copied++;copiedText=text;};
        var copy=Named<ActionButton>(form,"copyDiagnostics");var feedback=Named<Label>(form,"diagnosticFeedback");
        copy.Focus();copy.GetType().GetMethod("OnMouseLeave",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(copy,new object[]{EventArgs.Empty});uint resting=ButtonFill(copy);
        OwnKey(copy,Keys.Space,false);uint pressed=ButtonFill(copy);OwnKey(copy,Keys.Space,true);uint released=ButtonFill(copy);
        Check(pressed!=resting && released==resting && copied==1,"A held Space visibly presses an action and releases with exactly one activation");
        Check(copiedText!=null && copiedText.Contains("fixture line 149") && feedback.Visible && feedback.Text.Contains("复制"),"Copy diagnostics uses the injected writer and confirms success beside the log");
        form.CopyDiagnosticText=delegate{throw new IOException("simulated clipboard unavailable");};copy.PerformClick();
        Check(feedback.Text.Contains("复制") && (feedback.Text.Contains("无法") || feedback.Text.Contains("失败") || feedback.Text.Contains("未能")),"A failed copy exposes feedback beside the action without a global clipboard write");
        Mouse(copy,"OnMouseDown",MouseButtons.Left,copy.Width/2,copy.Height/2);Mouse(copy,"OnMouseMove",MouseButtons.Left,-5,-5);uint outside=ButtonFill(copy);
        Mouse(copy,"OnMouseUp",MouseButtons.Left,-5,-5);Check(outside==ButtonFill(copy),"Dragging outside an action cancels its pressed appearance");
        Mouse(copy,"OnMouseDown",MouseButtons.Left,copy.Width/2,copy.Height/2);copy.Enabled=false;copy.Enabled=true;
        copy.GetType().GetMethod("OnMouseLeave",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(copy,new object[]{EventArgs.Empty});Check(ButtonFill(copy)==resting,"Disabling and restoring an action does not leave it visually pressed");

        home.PerformClick();bool startup=false;form.ReadAutostart=delegate{return startup;};form.ChangeAutostart=delegate{throw new IOException("simulated startup write failure");};
        Toggle startupToggle=null;foreach(Control control in Walk(form))if(control is Toggle && control.AccessibleName=="登录后自动运行")startupToggle=(Toggle)control;
        if(startupToggle==null)throw new InvalidOperationException("Missing startup toggle");startupToggle.Checked=true;
        Check(!startupToggle.Checked && !startup,"A failed startup write restores the control from the unchanged model");

        Command(form,Keys.Control|Keys.D3);var theme=Named<ChoiceButton>(form,"themeChoice");theme.AccessibilityObject.DoDefaultAction();Application.DoEvents();
        var menu=(ContextMenuStrip)Field(theme,"choices");Check(menu.Width>=theme.Width && menu.Items[0].Font.FontFamily.Name==theme.Font.FontFamily.Name && Math.Abs(menu.Items[0].Font.SizeInPoints-theme.Font.SizeInPoints)<.1f,"Choice menus align with the trigger width and typography");
        menu.GetType().GetMethod("ProcessCmdKey",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(menu,new object[]{new Message(),Keys.F4});
        Check(!menu.Visible && (theme.AccessibilityObject.State&AccessibleStates.Collapsed)!=0,"F4 closes the focused choice popup through its own keyboard route");
        form.RequestDispose();
      }
      LogReadingTests(logger);
    }
  }
  static void LogReadingTests(Logger logger){
    using(var form=new SettingsForm(null,logger)){
      form.Show();Command(form,Keys.Control|Keys.D2);Application.DoEvents();
      var log=Named<TextBox>(form,"diagnosticLog");var follow=Named<ActionButton>(form,"followDiagnostics");var feedback=Named<Label>(form,"diagnosticFeedback");
      // Scroll only this fixture's edit control; this is an ordinary user-reading message.
      SendMessage(log.Handle,0x115,new IntPtr(6),IntPtr.Zero);Application.DoEvents();
      log.Select(Math.Min(18,log.TextLength),8);string frozen=log.Text;int selection=log.SelectionStart,length=log.SelectionLength;
      int first=SendMessage(log.Handle,0xCE,IntPtr.Zero,IntPtr.Zero).ToInt32();logger.Info("isolated record appended while reading");Application.DoEvents();
      Check(log.Text==frozen && log.SelectionStart==selection && log.SelectionLength==length && SendMessage(log.Handle,0xCE,IntPtr.Zero,IntPtr.Zero).ToInt32()==first,"Reading older diagnostics freezes the text and preserves selection and scroll position");
      Check(follow.Text.Contains("回到最新") && feedback.Text.Contains("新记录"),"Paused diagnostics indicate that new records are waiting");
      Button(form,"速度与手势").PerformClick();logger.Info("isolated record appended on another page");Button(form,"诊断与维护").PerformClick();Application.DoEvents();
      Check(log.Text==frozen && log.SelectionStart==selection && log.SelectionLength==length,"Returning to diagnostics preserves the paused reading view");
      Command(form,Keys.Control|Keys.D1);Command(form,Keys.Control|Keys.D2);Application.DoEvents();
      Check(log.SelectionStart==selection && log.SelectionLength==length && SendMessage(log.Handle,0xCE,IntPtr.Zero,IntPtr.Zero).ToInt32()==first,"Keyboard page navigation preserves the paused log selection and reading position");
      follow.PerformClick();Application.DoEvents();Check(log.Text.Contains("record appended on another page") && follow.Text.Contains("暂停跟随"),"Returning to latest includes records received while the reading view was paused");
      form.RequestDispose();
    }
  }
  [STAThread]static void Main(string[] args){try{
    if(args.Length<2 || !AppPaths.IsIsolated)throw new InvalidOperationException("UI checks require an independent data directory");
    bool matrix=args.Length==4 && args[2]=="--layout-matrix";
    if(matrix){if(args[3]!="unaware" && args[3]!="system")throw new InvalidOperationException("Unknown DPI context");if(SetThreadDpiAwarenessContext(new IntPtr(args[3]=="unaware"?-1:-2))==IntPtr.Zero)throw new InvalidOperationException("Cannot establish the requested real DPI context");}
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
    if(matrix){LayoutMatrix(args[1],args[3]);return;}
    int before=10;string config=Path.Combine(args[0],"state","gesture-options.json"),beforeHash=Hash(config);
    using(var form=new SettingsForm(null,null)){
      form.Show();Application.DoEvents();SpeedSlider slider=null;TextBox log=null;foreach(var c in Walk(form)){if(c is SpeedSlider)slider=(SpeedSlider)c;if(c is TextBox)log=(TextBox)c;}
      Check(slider!=null && log!=null,"Settings page and diagnostic view are created");Check(slider.Value==before,"Safe preview displays the isolated model speed");
      Button(form,"精细").PerformClick();Check(slider.Value==6,"Fine preset is wired to the shared slider");Button(form,"均衡").PerformClick();Check(slider.Value==10,"Balanced preset is wired to the shared slider");Button(form,"快速").PerformClick();Check(slider.Value==17,"Fast preset is wired to the shared slider");
      var key=typeof(SpeedSlider).GetMethod("OnKeyDown",BindingFlags.NonPublic|BindingFlags.Instance);key.Invoke(slider,new object[]{new KeyEventArgs(Keys.Home)});Check(slider.Value==1,"Slider keyboard Home reaches the minimum");key.Invoke(slider,new object[]{new KeyEventArgs(Keys.End)});Check(slider.Value==20,"Slider keyboard End reaches the maximum");key.Invoke(slider,new object[]{new KeyEventArgs(Keys.Left)});Check(slider.Value==19,"Slider keyboard arrow adjusts one step");
      slider.AccessibilityObject.Value="10";Check(slider.Value==10 && slider.AccessibilityObject.Value=="10","Accessible slider value is readable and adjustable");
      MethodInfo down=typeof(SpeedSlider).GetMethod("OnMouseDown",BindingFlags.NonPublic|BindingFlags.Instance),move=typeof(SpeedSlider).GetMethod("OnMouseMove",BindingFlags.NonPublic|BindingFlags.Instance),up=typeof(SpeedSlider).GetMethod("OnMouseUp",BindingFlags.NonPublic|BindingFlags.Instance);
      down.Invoke(slider,new object[]{new MouseEventArgs(MouseButtons.Left,1,slider.Width-1,slider.Height/2,0)});Check(slider.Value==20,"Pointer interaction reaches the high-DPI slider maximum");
      move.Invoke(slider,new object[]{new MouseEventArgs(MouseButtons.Left,0,0,slider.Height/2,0)});Check(slider.Value==1,"Dragging beyond the rail clamps to the slider minimum");
      up.Invoke(slider,new object[]{new MouseEventArgs(MouseButtons.Left,1,0,slider.Height/2,0)});move.Invoke(slider,new object[]{new MouseEventArgs(MouseButtons.None,0,slider.Width/2,slider.Height/2,0)});Check(slider.Value==1,"Releasing the pointer stops slider dragging");slider.Value=before;
      Button(form,"诊断与维护").PerformClick();Check(log.Visible,"Maintenance navigation reveals diagnostics");CheckTabTraversal(form,Button(form,"诊断与维护"),log,"Diagnostic Tab order reaches the readable log and skips hidden pages");Button(form,"速度与手势").PerformClick();Check(!log.Visible,"Overview hides technical logs");
      int unchanged=slider.Value;key.Invoke(slider,new object[]{new KeyEventArgs(Keys.Control|Keys.Left)});Check(slider.Value==unchanged,"Modified arrows do not accidentally change speed");
      var command=typeof(SettingsForm).GetMethod("ProcessCmdKey",BindingFlags.NonPublic|BindingFlags.Instance);
      command.Invoke(form,new object[]{new Message(),Keys.Control|Keys.D2});Check(log.Visible,"Ctrl 2 opens diagnostics inside the app");command.Invoke(form,new object[]{new Message(),Keys.Escape});Check(!log.Visible,"Escape returns from diagnostics without closing the app");
      slider.Value=12;command.Invoke(form,new object[]{new Message(),Keys.Escape});Check(slider.Value==before,"Escape cancels a pending speed edit");
      slider.CancelInteraction();slider.Value=10;int completions=0;EventHandler completed=delegate{completions++;};slider.InteractionCompleted+=completed;slider.Focus();
      OwnKey(slider,Keys.Right,false);OwnKey(slider,Keys.A,true);Check(slider.Value==11 && slider.IsInteracting && completions==0,"Unrelated key release does not commit an ongoing speed adjustment");
      OwnKey(slider,Keys.Right,true);Check(!slider.IsInteracting && completions==1,"Releasing the adjustment key completes speed editing exactly once");
      int disabledValue=slider.Value;slider.Enabled=false;slider.AccessibilityObject.Value="17";Check(slider.Value==disabledValue && completions==1,"Disabled speed controls reject accessibility changes");slider.Enabled=true;slider.InteractionCompleted-=completed;slider.Value=before;
      CheckTabTraversal(form,Button(form,"速度与手势"),slider,"Overview Tab order reaches the speed slider and skips hidden pages");
      foreach(Control control in Walk(form))if(control is Toggle && control.Visible){var toggle=(Toggle)control;bool previous=toggle.Checked;toggle.Enabled=false;toggle.AccessibilityObject.DoDefaultAction();Check(toggle.Checked==previous,"Disabled toggles reject accessibility default actions");toggle.Enabled=true;break;}
      command.Invoke(form,new object[]{new Message(),Keys.Control|Keys.D3});
      ChoiceButton theme=null,font=null;Control appearanceStatus=null;
      foreach(var control in Walk(form)){if(control.Name=="themeChoice")theme=(ChoiceButton)control;if(control.Name=="fontChoice")font=(ChoiceButton)control;if(control.Name=="appearanceStatus")appearanceStatus=control;}
      Check(theme!=null && font!=null && theme.Visible && font.Visible,"Ctrl 3 opens the dedicated appearance page");
      AppearanceSettings savedAppearance=UiPreferences.Current;
      theme.Focus();OwnKey(theme,Keys.End,false);OwnKey(theme,Keys.End,true);Check(theme.SelectedIndex==theme.Options.Length-1,"Focused appearance choices support End through the native key route");
      OwnKey(theme,Keys.Home,false);OwnKey(theme,Keys.Home,true);OwnKey(theme,Keys.Down,false);OwnKey(theme,Keys.Down,true);Check(theme.SelectedIndex==1,"Appearance choices support Home and incremental arrow selection");
      Check(theme.AccessibilityObject.Role==AccessibleRole.ComboBox && theme.AccessibilityObject.Value==theme.Options[theme.SelectedIndex],"Appearance accessibility exposes the selected label without decoration");
      theme.AccessibilityObject.DoDefaultAction();Application.DoEvents();Check((theme.AccessibilityObject.State&AccessibleStates.Expanded)!=0,"Appearance accessibility default action expands its own options");
      OwnKey(theme,Keys.F4,false);OwnKey(theme,Keys.F4,true);Application.DoEvents();Check((theme.AccessibilityObject.State&AccessibleStates.Collapsed)!=0,"F4 closes the appearance menu without disposing an active event source");
      theme.AccessibilityObject.DoDefaultAction();Application.DoEvents();OwnKey(theme,Keys.F4,false);OwnKey(theme,Keys.F4,true);Check((theme.AccessibilityObject.State&AccessibleStates.Collapsed)!=0,"Appearance choices can reopen and close after the first selection menu");
      CheckTabTraversal(form,theme,font,"Appearance Tab order reaches the font choice and skips hidden overview controls");
      Button(form,"取消预览").PerformClick();
      theme.SelectedIndex=2;Check(UiPreferences.Dark,"Dark theme is previewed immediately");
      Check(!File.Exists(Path.Combine(args[0],"state","appearance.json")),"Appearance preview does not write preferences");
      Check(font.Options.Length>=2,"The font picker exposes automatic and Windows schemes");
      font.SelectedIndex=Math.Min(1,font.Options.Length-1);Check(UiPreferences.Current.Font==UiPreferences.Fonts[font.SelectedIndex].Key,"Font selection updates the local preview");
      Button(form,"取消预览").PerformClick();Check(UiPreferences.Current.Theme==savedAppearance.Theme && UiPreferences.Current.Font==savedAppearance.Font,"Cancelling appearance restores the saved theme and font");
      theme.SelectedIndex=2;Button(form,"保存外观").PerformClick();Check(!File.Exists(Path.Combine(args[0],"state","appearance.json")),"Safe-preview Save does not create user preferences");
      UiPreferences.Preview(savedAppearance);command.Invoke(form,new object[]{new Message(),Keys.Control|Keys.D1});
      form.UpdateStatus("速度设置失败：模拟错误提示");form.UpdateStatus("内置触摸板：正常输入报告");Check(Button(form,"查看详情").Visible,"Input reports do not hide actionable error feedback");Button(form,"查看详情").PerformClick();Check(log.Visible,"Error details action opens diagnostics");
      command.Invoke(form,new object[]{new Message(),Keys.Control|Keys.D1});Check(!log.Visible,"Ctrl 1 returns to the overview");Check(!Button(form,"撤销调速").Enabled,"Undo is disabled when no speed history exists");
      Check(slider.Right<=slider.Parent.ClientSize.Width,"Speed slider stays inside the card at high DPI");
      form.Size=form.MinimumSize;Application.DoEvents();Check(slider.Right<=slider.Parent.ClientSize.Width,"Speed slider stays inside the card at minimum width");
      CaptureClient(form,Path.Combine(args[1],"compact.png"));
      form.RequestDispose();
    }
    Check(!File.Exists(Path.Combine(args[0],"state","pointer-speed-before.json")),"Preview never invokes the system-speed backup or setter");Check(Hash(config)==beforeHash,"Preview interactions never change saved user preferences");
    InteractionTests();
    AppearanceStorageTests(args[0]);
    File.WriteAllText(Path.Combine(args[1],"ui-smoke.json"),new JavaScriptSerializer().Serialize(new{success=true,capturedAt=DateTime.Now.ToString("o"),checks=checks,previewSpeed=before,isolatedPreview=true,globalInputInjected=false}));
  }catch(Exception error){File.WriteAllText(Path.Combine(args[1],"ui-smoke.error.txt"),error.ToString());Environment.Exit(1);}}
}

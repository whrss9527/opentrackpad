using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Reflection;using System.Security.Cryptography;using System.Web.Script.Serialization;using System.Windows.Forms;using ThreeFingerDrag;using OpenTrackpadUi;
class UiSmoke {
  static List<string> checks=new List<string>();static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
  static IEnumerable<Control> Walk(Control root){foreach(Control child in root.Controls){yield return child;foreach(Control nested in Walk(child))yield return nested;}}
  static ActionButton Button(Form form,string text){foreach(var c in Walk(form))if(c is ActionButton && c.Text.StartsWith(text))return (ActionButton)c;throw new Exception("Missing button "+text);}
  static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));}
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
  [STAThread]static void Main(string[] args){try{
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
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
      Button(form,"诊断与维护").PerformClick();Check(log.Visible,"Maintenance navigation reveals diagnostics");Button(form,"速度与手势").PerformClick();Check(!log.Visible,"Overview hides technical logs");
      int unchanged=slider.Value;key.Invoke(slider,new object[]{new KeyEventArgs(Keys.Control|Keys.Left)});Check(slider.Value==unchanged,"Modified arrows do not accidentally change speed");
      var command=typeof(SettingsForm).GetMethod("ProcessCmdKey",BindingFlags.NonPublic|BindingFlags.Instance);
      command.Invoke(form,new object[]{new Message(),Keys.Control|Keys.D2});Check(log.Visible,"Ctrl 2 opens diagnostics inside the app");command.Invoke(form,new object[]{new Message(),Keys.Escape});Check(!log.Visible,"Escape returns from diagnostics without closing the app");
      slider.AccessibilityObject.Value="12";command.Invoke(form,new object[]{new Message(),Keys.Escape});Check(slider.Value==before,"Escape cancels a pending speed edit");
      command.Invoke(form,new object[]{new Message(),Keys.Control|Keys.D3});
      ChoiceButton theme=null,font=null;Control appearanceStatus=null;
      foreach(var control in Walk(form)){if(control.Name=="themeChoice")theme=(ChoiceButton)control;if(control.Name=="fontChoice")font=(ChoiceButton)control;if(control.Name=="appearanceStatus")appearanceStatus=control;}
      Check(theme!=null && font!=null && theme.Visible && font.Visible,"Ctrl 3 opens the dedicated appearance page");
      AppearanceSettings savedAppearance=UiPreferences.Current;
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
      using(var bitmap=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)){form.Controls[0].DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(Path.Combine(args[1],"compact.png"));}
      form.RequestDispose();
    }
    Check(!File.Exists(Path.Combine(args[0],"state","pointer-speed-before.json")),"Preview never invokes the system-speed backup or setter");Check(Hash(config)==beforeHash,"Preview interactions never change saved user preferences");
    AppearanceStorageTests(args[0]);
    File.WriteAllText(Path.Combine(args[1],"ui-smoke.json"),new JavaScriptSerializer().Serialize(new{success=true,capturedAt=DateTime.Now.ToString("o"),checks=checks,previewSpeed=before,isolatedPreview=true,globalInputInjected=false}));
  }catch(Exception error){File.WriteAllText(Path.Combine(args[1],"ui-smoke.error.txt"),error.ToString());Environment.Exit(1);}}
}

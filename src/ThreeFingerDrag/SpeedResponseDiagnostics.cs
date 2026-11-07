using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using OpenTrackpadUi;

namespace ThreeFingerDrag {
  // Targets only this test's own controls. No global mouse/keyboard events or
  // real pointer movement; every native speed change is restored in finally.
  static class SpeedResponseDiagnostics {
    [DllImport("user32.dll",EntryPoint="SendMessageW")]
    static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    static void Check(bool passed,string name,List<string> checks){if(!passed)throw new InvalidOperationException(name);checks.Add(name);}
    static SpeedSlider FindSlider(Control root){foreach(Control child in root.Controls){if(child is SpeedSlider)return (SpeedSlider)child;SpeedSlider nested=FindSlider(child);if(nested!=null)return nested;}return null;}
    static IntPtr PointFor(SpeedSlider slider,int value){float dpi;using(Graphics graphics=slider.CreateGraphics())dpi=graphics.DpiX/96;double pad=14*dpi;int x=(int)Math.Round(pad+(slider.Width-2*pad)*(value-1)/19.0);return new IntPtr((slider.Height/2<<16)|(x&65535));}
    static void Mouse(SpeedSlider slider,uint message,int value){SendMessage(slider.Handle,message,new IntPtr(message==0x202?0:1),PointFor(slider,value));}
    static long Click(SpeedSlider slider,int value,bool doubleClick){var timer=Stopwatch.StartNew();Mouse(slider,doubleClick?0x203u:0x201u,value);Mouse(slider,0x202,value);return timer.ElapsedMilliseconds;}

    public static void Run(string path){
      Native.DisableImeForProcess();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
      TouchpadSpeedParameters before=TouchpadSpeed.Read();int original=(int)before.cursorSpeed,probe=original==1?2:original-1;
      var checks=new List<string>();var clicks=new List<long>();long blockedReceiverMs=0;int commits=0;Exception backendError=null;
      try {
        using(var receiver=new SlowReceiver()){
          var timer=Stopwatch.StartNew();TouchpadSpeed.Change(probe);blockedReceiverMs=timer.ElapsedMilliseconds;
          Check(blockedReceiverMs<250,"A blocked settings receiver does not stall the speed setter",checks);
          Check(receiver.Received.WaitOne(1000),"The asynchronous settings notification reaches another window",checks);
        }
        TouchpadSpeed.Change(original);
        using(var form=new SettingsForm(null,null,delegate(int value){try{TouchpadSpeed.Change(value);commits++;}catch(Exception error){backendError=error;}})){
          form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-24000,-24000);form.ShowInTaskbar=false;
          form.Show();Application.DoEvents();SpeedSlider slider=FindSlider(form);if(slider==null)throw new Exception("Speed slider missing");
          clicks.Add(Click(slider,probe,false));
          Check(slider.Value==probe && TouchpadSpeed.GetCursorSpeed()==probe && commits==1 && backendError==null,"A native mouse click updates the displayed and actual speed before returning",checks);
          for(int i=0;i<10;i++){int target=i%2==0?original:probe;clicks.Add(Click(slider,target,false));if(slider.Value!=target || TouchpadSpeed.GetCursorSpeed()!=target)throw new Exception("A rapid click was lost");}
          Check(commits==11 && backendError==null,"Ten consecutive native mouse clicks all reach the real speed backend",checks);
          clicks.Add(Click(slider,original,true));
          Check(slider.Value==original && TouchpadSpeed.GetCursorSpeed()==original && commits==12,"The second click of a double click is also applied",checks);
          Mouse(slider,0x201,probe);Mouse(slider,0x200,original==20?18:20);
          Check(TouchpadSpeed.GetCursorSpeed()==original && commits==12,"Dragging previews values without repeatedly writing to Windows",checks);
          Mouse(slider,0x202,original==20?18:20);
          Check(TouchpadSpeed.GetCursorSpeed()==slider.Value && commits==13 && backendError==null,"Releasing the drag applies its final speed exactly once",checks);
          long maximum=0;foreach(long elapsed in clicks)maximum=Math.Max(maximum,elapsed);
          Check(maximum<250,"Each native click finishes within 250 milliseconds",checks);
          form.RequestDispose();
        }
      }finally{TouchpadSpeed.Change(original);}
      TouchpadSpeedParameters restored=TouchpadSpeed.Read();
      Check(restored.cursorSpeed==before.cursorSpeed && restored.userFlags==before.userFlags && restored.sensitivity==before.sensitivity && restored.feedbackIntensity==before.feedbackIntensity && restored.clickForceSensitivity==before.clickForceSensitivity && restored.rightClickWidth==before.rightClickWidth && restored.rightClickHeight==before.rightClickHeight,"The original speed and other touchpad settings are restored",checks);
      File.WriteAllText(path,new JavaScriptSerializer().Serialize(new {success=true,capturedAt=DateTime.Now.ToString("o"),checks=checks,originalSpeed=original,restoredSpeed=restored.cursorSpeed,blockedReceiverSetMs=blockedReceiverMs,clickMilliseconds=clicks,backendCommits=commits,otherSettingsPreserved=true,globalInputInjected=false}));
    }

    sealed class SlowReceiver:IDisposable {
      public readonly ManualResetEvent Received=new ManualResetEvent(false);
      readonly ManualResetEvent release=new ManualResetEvent(false),ready=new ManualResetEvent(false);
      readonly Thread thread;SlowWindow window;
      public SlowReceiver(){thread=new Thread(delegate(){using(var form=new SlowWindow(Received,release)){window=form;IntPtr handle=form.Handle;ready.Set();Application.Run();}});thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();if(!ready.WaitOne(2000))throw new Exception("Settings receiver did not start");}
      public void Dispose(){release.Set();if(window!=null && !window.IsDisposed)window.BeginInvoke(new Action(Application.ExitThread));if(!thread.Join(3000))throw new Exception("Settings receiver did not stop");Received.Dispose();release.Dispose();ready.Dispose();}
    }
    sealed class SlowWindow:Form {
      readonly ManualResetEvent received,release;
      public SlowWindow(ManualResetEvent received,ManualResetEvent release){this.received=received;this.release=release;ShowInTaskbar=false;}
      protected override void WndProc(ref Message message){if(message.Msg==0x1A){received.Set();release.WaitOne(2500);}base.WndProc(ref message);}
    }
  }
}

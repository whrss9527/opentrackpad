using System;
using System.Threading;
using System.Windows.Forms;
using OpenTrackpadShared;

namespace ThreeFingerDrag {
  static class Program {
    static Mutex mutex;

    public static bool Background;

    [STAThread]
    static void Main(string[] args) {
      CliOptions options;
      if(!CliOptions.TryParse(args,out options)){Environment.Exit(2);return;}
      args=options.Arguments;
      if(options.Preview && !AppPaths.IsIsolated)Environment.SetEnvironmentVariable("OPENTRACKPAD_DATA_DIR",System.IO.Path.Combine(System.IO.Path.GetTempPath(),"OpenTrackpad-preview-"+System.Diagnostics.Process.GetCurrentProcess().Id));
      if(args.Length>1 && args[0]=="--self-test-runtime") {
        try { RuntimeDiagnostics.Run(args[1]);Environment.Exit(0); }
        catch(Exception error){System.IO.File.WriteAllText(args[1]+".error.txt",error.ToString());Environment.Exit(1);}
        return;
      }
      Native.InitializeLocalComSecurity();
      if(args.Length>1 && args[0]=="--test-speed-response"){
        try{SpeedResponseDiagnostics.Run(args[1]);Environment.Exit(0);}
        catch(Exception error){System.IO.File.WriteAllText(args[1]+".error.txt",error.ToString());Environment.Exit(1);}
        return;
      }
      if(args.Length>1 && (args[0]=="--query-speed" || args[0]=="--restore-speed" || args[0]=="--test-speed" || args[0]=="--test-speed-undo" || args[0]=="--set-speed")) {
        string path=args[args.Length-1];
        try {
          object result=args[0]=="--query-speed"?TouchpadSpeed.Query():args[0]=="--restore-speed"?TouchpadSpeed.Restore():args[0]=="--test-speed"?TouchpadSpeed.VerifyLiveApi():args[0]=="--test-speed-undo"?TouchpadSpeed.VerifyLiveUndo():TouchpadSpeed.Apply(int.Parse(args[1]));
          System.IO.File.WriteAllText(path,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(result));
          Environment.Exit(0);
        }catch(Exception exception){System.IO.File.WriteAllText(path+".error.txt",exception.ToString());Environment.Exit(1);}
        return;
      }
      if(args.Length>1 && (args[0]=="--render" || args[0]=="--render-diagnostics" || args[0]=="--render-appearance" || args[0]=="--render-compact")) {
        Background=true;
        Native.DisableImeForProcess();
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        using(SettingsForm form=new SettingsForm(null,null)) {
          if(args[0]=="--render-diagnostics")form.ShowDiagnostics();
          if(args[0]=="--render-appearance")form.ShowAppearancePage();
          if(args[0]=="--render-compact")form.Size=form.MinimumSize;
          form.Show();Application.DoEvents();
          using(var graphics=form.CreateGraphics())System.IO.File.WriteAllText(args[1]+".layout.json",new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {width=form.Width,height=form.Height,dpi=graphics.DpiX,fontSize=form.Font.Size,autoScale=form.AutoScaleMode.ToString()}));
          using(var bitmap=new System.Drawing.Bitmap(form.ClientSize.Width,form.ClientSize.Height)) {
            form.Controls[0].DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,bitmap.Width,bitmap.Height));
            bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);
          }
          form.RequestDispose();
        }
        return;
      }
      if(args.Length>0 && args[0]=="--shutdown") {
        try{using(EventWaitHandle signal=EventWaitHandle.OpenExisting(AppPaths.InstanceName("ThreeFingerDrag.Stop")))signal.Set();}catch{}
        return;
      }
      if (args.Length > 1 && (args[0] == "--configure" || args[0] == "--restore" || args[0] == "--query-config")) {
        try {
          object result=args[0]=="--query-config"?GesturePreferences.Query():GesturePreferences.Apply(args[0]=="--configure");
          System.IO.File.WriteAllText(args[1],new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(result));
          Environment.Exit(0);
        } catch(Exception exception) {System.IO.File.WriteAllText(args[1]+".error.txt",exception.ToString());Environment.Exit(1);}
        return;
      }
      if (args.Length > 1 && (args[0] == "--self-test" || args[0]=="--self-test-available")) {
        try { GestureDiagnostics.Run(args[1],args[0]=="--self-test"); Environment.Exit(0); }
        catch (Exception exception) { System.IO.File.WriteAllText(args[1] + ".error.txt", exception.ToString()); Environment.Exit(1); }
        return;
      }
      Background = args.Length > 0 && args[0] == "--background";
      mutex = new Mutex(false, AppPaths.InstanceName("ThreeFingerDrag"));
      bool acquired;
      try {acquired=mutex.WaitOne(Array.IndexOf(args,"--relaunch")>=0?5000:0);}
      catch(AbandonedMutexException){acquired=true;}
      if (!acquired) {
        if(!Background)try{using(EventWaitHandle signal=EventWaitHandle.OpenExisting(AppPaths.InstanceName("ThreeFingerDrag.Show")))signal.Set();}catch{}
        mutex.Dispose();
        return;
      }

      // No app surface accepts text input. Disable IME before the first top-level
      // window is created so third-party input-method modules are not injected.
      Native.DisableImeForProcess();
      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) {
        MessageBox.Show(e.Exception.ToString(), "Open Trackpad");
      };
      AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) {
        MessageBox.Show(Convert.ToString(e.ExceptionObject), "Open Trackpad");
      };

      using (AppController controller = new AppController()) {
        Application.Run(controller);
      }

      try { mutex.ReleaseMutex(); } catch { }
      mutex.Dispose();
    }
  }
}

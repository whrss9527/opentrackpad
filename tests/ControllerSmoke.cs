using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using OpenTrackpadShared;
using ThreeFingerDrag;

static class ControllerSmoke {
    static readonly List<string> checks=new List<string>();
    static readonly List<Process> children=new List<Process>();
    static readonly Dictionary<Process,string> childProfiles=new Dictionary<Process,string>();
    static AppController controller;
    static string production,otherRoot;
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr handle);
    static void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
    static object Field(object value,string name){return value.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(value);}
    static SettingsForm CurrentForm {get{return (SettingsForm)Field(controller,"form");}}
    static Dictionary<string,object> Options(){return (Dictionary<string,object>)AtomicJson.Read(AppPaths.StateFile("gesture-options.json"),null).Value;}
    static bool PumpUntil(Func<bool> condition,int timeout){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<timeout){Application.DoEvents();if(condition())return true;Thread.Sleep(10);}return false;}
    static Process StartChild(string data,string arguments){
        var info=new ProcessStartInfo(production,arguments){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(production)};
        info.EnvironmentVariables["OPENTRACKPAD_DATA_DIR"]=data;
        var process=Process.Start(info);children.Add(process);childProfiles[process]=data;return process;
    }
    static string ProfileSignal(string data,string suffix){return "Local\\OpenTrackpad.ThreeFingerDrag."+suffix+".profile."+AppPaths.ProfileId(data);}
    static bool SignalExists(string name){try{using(var signal=EventWaitHandle.OpenExisting(name))return true;}catch(WaitHandleCannotBeOpenedException){return false;}}
    static void StopOther(Process process){
        string signalName=ProfileSignal(otherRoot,"Stop");
        if(!process.HasExited && SignalExists(signalName))using(var signal=EventWaitHandle.OpenExisting(signalName))signal.Set();
        if(!process.WaitForExit(5000)){process.Kill();throw new InvalidOperationException("The disposable sibling profile did not exit");}
    }
    static void RunChecks(){
        var input=(InputWindow)Field(controller,"input");
        Check(controller.IsPreview && !controller.DragActive && !input.Listening,"Real isolated controller never starts Raw Input listening");
        Check(controller.UnifiedSpeedValue==10,"Isolated controller starts from simulated speed");
        Check(controller.SetUnifiedSpeed(17) && controller.UnifiedSpeedValue==17 && TouchpadSpeed.GetCursorSpeed()==17,"Controller applies speed to the isolated model");
        Check(controller.CanUndoSpeed && controller.UndoSpeedTarget==10 && controller.UndoSpeed() && controller.UnifiedSpeedValue==10,"Controller undo returns to the simulated previous speed");
        controller.SetUnifiedSpeed(6);controller.SetEnabled(false);controller.SetDesktopHotkeys(true);
        var options=Options();
        Check(!(bool)options["enabled"] && (bool)options["desktopHotkeys"] && Convert.ToInt32(options["unifiedSpeed"])==6 && Convert.ToInt32(options["extension"])==42,"Controller options persist without losing unrelated fields");
        Check(!controller.DesktopHotkeysEnabled && !input.Listening,"Requested shortcuts remain unregistered in isolation");
        controller.SetDesktopHotkeys(false);controller.SetAutostart(false);controller.ConfigureGestureMode();
        Check(controller.EnabledValue && !controller.DesktopHotkeysRequested && !GesturePreferences.HasAutostart(),"Applying simulated gestures preserves shortcut and startup choices");
        controller.SetAutostart(true);controller.SetDesktopHotkeys(true);controller.RestoreNativeGestures();
        Check(!controller.EnabledValue && !controller.DesktopHotkeysRequested && !GesturePreferences.HasAutostart(),"Simulated native-gesture restoration closes startup and custom gestures");
        controller.SetEnabled(true);Check(controller.EnabledValue && !controller.DragActive && !input.Listening,"Enabling the isolated controller cannot bypass the input gate");
        MethodInfo restart=typeof(AppController).GetMethod("RestartAsAdministrator",BindingFlags.NonPublic|BindingFlags.Instance);
        restart.Invoke(controller,null);
        Check(!(bool)Field(controller,"exitRequested") && !(bool)Field(controller,"restartAfterRelease") && controller.CurrentStatus.IndexOf("预览",StringComparison.Ordinal)>=0,"Isolated administrator restart is rejected before escalation or exit");
        string instance=AppPaths.InstanceName("ThreeFingerDrag");
        Check(instance.IndexOf(".profile.",StringComparison.Ordinal)>0 && instance!="Local\\OpenTrackpad.ThreeFingerDrag","Test instance names cannot target the old normal instance");
        { var duplicate=StartChild(AppPaths.DataDirectory,"--background");
            Check(duplicate.WaitForExit(5000) && duplicate.ExitCode==0,"A second instance with the same profile exits cleanly");
            Check(CurrentForm==null || !CurrentForm.Visible,"Background duplicate does not unexpectedly show settings");
        }
        { var duplicate=StartChild(AppPaths.DataDirectory,"");
            Check(duplicate.WaitForExit(5000) && duplicate.ExitCode==0,"Interactive same-profile launch hands off to the existing controller");
            Check(PumpUntil(delegate{return CurrentForm!=null && CurrentForm.Visible;},3000),"The scoped show signal opens only this controller's settings");
            controller.HideSettings();Check(CurrentForm==null || !CurrentForm.Visible,"The real controller can close its settings back to the tray");
        }
        { var sibling=StartChild(otherRoot,"--background");
            Check(PumpUntil(delegate{return !sibling.HasExited && SignalExists(ProfileSignal(otherRoot,"Stop"));},5000),"A different profile runs independently");
            Check(CurrentForm==null || !CurrentForm.Visible,"A different profile never signals this controller's show event");
            StopOther(sibling);Check(sibling.ExitCode==0,"The independent profile exits through its own stop signal");
        }
        string concurrent=AppPaths.StateFile("concurrent-state.json");
        var writers=new List<Process>();
        foreach(string token in new[]{"writer-a","writer-b"}){
            string quote=((char)34).ToString();
            var info=new ProcessStartInfo(Application.ExecutablePath,"--write-fake "+quote+concurrent+quote+" "+token+" 20"){UseShellExecute=false,CreateNoWindow=true};
            info.EnvironmentVariables["OPENTRACKPAD_DATA_DIR"]=AppPaths.DataDirectory;
            var process=Process.Start(info);children.Add(process);childProfiles[process]=AppPaths.DataDirectory;writers.Add(process);
        }
        foreach(var process in writers)Check(process.WaitForExit(10000) && process.ExitCode==0,"Independent state writer completed its 20 writes");
        Check(AtomicJson.WithLock<bool>(concurrent,delegate{return File.ReadAllText(concurrent)==File.ReadAllText(AtomicJson.RecoveryPath(concurrent));}),"Two real writer processes leave the main and recovery files consistent");
        Check(Convert.ToInt32(((Dictionary<string,object>)AtomicJson.Read(concurrent,null).Value)["iteration"])==19,"Concurrent writers retain a complete final JSON payload");
        Check(!input.Listening && !controller.DesktopHotkeysEnabled,"No controller integration action enables real input or global shortcuts");
    }
    [STAThread] static int Main(string[] args){
        if(args.Length==4 && args[0]=="--write-fake"){
            if(!AppPaths.IsIsolated)return 2;
            string target=Path.GetFullPath(args[1]),prefix=Path.GetFullPath(AppPaths.DataDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return 2;
            if(args[2]!="writer-a" && args[2]!="writer-b")return 2;
            try{for(int index=0;index<int.Parse(args[3]);index++)AtomicJson.Write(target,new Dictionary<string,object>{{"writer",args[2]},{"iteration",index}});return 0;}catch(Exception error){File.WriteAllText(target+"."+args[2]+".error.txt",error.ToString());return 1;}
        }
        if(args.Length!=2 || !AppPaths.IsIsolated)return 2;
        string report=Path.GetFullPath(args[0]);production=Path.GetFullPath(args[1]);otherRoot=Path.Combine(AppPaths.DataDirectory,"other-profile");
        Exception failure=null;IntPtr inputHandle=IntPtr.Zero;
        try {
            AtomicJson.Write(AppPaths.StateFile("gesture-options.json"),new Dictionary<string,object>{{"enabled",true},{"desktopHotkeys",false},{"extension",42}});
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Program.Background=true;
            using(var mutex=new Mutex(false,AppPaths.InstanceName("ThreeFingerDrag"))){
                if(!mutex.WaitOne(0))throw new InvalidOperationException("Disposable profile was unexpectedly already running");
                try {
                    using(controller=new AppController()){
                        inputHandle=((InputWindow)Field(controller,"input")).Handle;
                        using(var timer=new System.Windows.Forms.Timer()){
                            timer.Interval=30;timer.Tick+=delegate{timer.Stop();try{RunChecks();}catch(Exception error){failure=error;}finally{controller.ExitApplication();}};
                            timer.Start();Application.Run(controller);
                        }
                    }
                } finally {mutex.ReleaseMutex();}
            }
            if(failure!=null)throw failure;
            Check(!IsWindow(inputHandle),"Controller exit disposes its native input window");
            File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{success=true,checks=checks,isolated=true,legacyInstanceSignalled=false,globalInputInjected=false,windowsSettingsModified=false}));
            return 0;
        } catch(Exception error){
            File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{success=false,checks=checks,error=error.ToString()}));return 1;
        } finally {
            foreach(var child in children){try{if(!child.HasExited){if(childProfiles[child]==otherRoot)StopOther(child);else child.Kill();}}catch{try{child.Kill();}catch{}}finally{child.Dispose();}}
        }
    }
}

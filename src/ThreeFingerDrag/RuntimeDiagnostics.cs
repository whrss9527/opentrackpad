using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Web.Script.Serialization;
using OpenTrackpadShared;

namespace ThreeFingerDrag {
  static class RuntimeDiagnostics {
    public static void Run(string output) {
      var checks=new List<string>();
      Action<bool,string> check=delegate(bool passed,string name){if(!passed)throw new InvalidOperationException(name);checks.Add(name);};
      var gate=new RuntimeGate();
      check(gate.Paused,"Unknown session starts paused");
      gate.Snapshot(true,false,true);check(!gate.Paused,"Verified active session resumes");
      gate.Apply(RuntimeEvent.Lock);gate.Apply(RuntimeEvent.Suspend);gate.Apply(RuntimeEvent.Resume);
      check(gate.Paused && gate.Locked,"Power resume preserves session lock");
      gate.Apply(RuntimeEvent.Disconnect);gate.Apply(RuntimeEvent.Unlock);
      check(gate.Paused && !gate.Connected,"Unlock does not clear a disconnected session");
      gate.Apply(RuntimeEvent.Connect);check(!gate.Paused,"Reconnection honors unlocked state");
      gate.Apply(RuntimeEvent.Lock);gate.Apply(RuntimeEvent.Connect);check(gate.Paused,"Reconnection preserves session lock");
      gate.Snapshot(true,false,true);check(gate.Paused && gate.Locked,"Stale active snapshot cannot clear an explicit session lock");
      gate.Apply(RuntimeEvent.Unlock);gate.Apply(RuntimeEvent.Disconnect);gate.Snapshot(true,false,true);
      check(gate.Paused && !gate.Connected,"Stale connected snapshot cannot clear an explicit disconnect");
      gate.Apply(RuntimeEvent.Resume);check(gate.Paused,"Power resume cannot clear an explicit disconnect");
      gate.Apply(RuntimeEvent.Connect);gate.Apply(RuntimeEvent.Suspend);gate.Snapshot(true,false,true);
      check(gate.Paused && gate.Suspended,"Active snapshot cannot clear suspend before its resume event");
      gate.Apply(RuntimeEvent.Resume);check(!gate.Paused,"Matching activation events release only their own blockers");
      gate.Snapshot(false,false,true);check(gate.Paused,"Failed state query remains paused");
      gate.Snapshot(true,false,true);gate.Apply(RuntimeEvent.Suspend);check(gate.Paused,"Suspend gates active input");
      var retry=new RuntimeRetry();check(retry.Next()==5000 && retry.Next()==10000 && retry.Next()==20000 && retry.Next()==40000 && retry.Next()==60000 && retry.Next()==60000,"Activation retries back off to one query per minute");
      retry.Reset();check(retry.Next()==5000,"A new activation event resets the retry delay");
      check(AppPaths.ProfileId(@"C:\OpenTrackpad\One")!=AppPaths.ProfileId(@"C:\OpenTrackpad\Two"),"Independent profile instances have independent names");
      check(AppPaths.ProfileId(@"C:\OpenTrackpad\One")==AppPaths.ProfileId(@"c:\opentrackpad\one\"),"Profile names normalize Windows path case");
      bool rejected=false;try{AppPaths.StateFile("..\\escape.json");}catch(ArgumentException){rejected=true;}
      check(rejected,"State filenames reject path traversal");
      var originalEntries=new List<object>();
      foreach(string name in new[]{"ThreeFingerSlideEnabled","ThreeFingerTapEnabled","FourFingerSlideEnabled","FourFingerTapEnabled","TapAndDrag"})originalEntries.Add(new Dictionary<string,object>{{"name",name},{"exists",true},{"kind","DWord"},{"value",1}});
      var originalSnapshot=new Dictionary<string,object>{{"values",originalEntries}};
      check(GesturePreferences.DecodeOriginalValues(originalSnapshot).Count==5,"Original gesture snapshot validated as a complete set before restoration");
      originalEntries.Add(originalEntries[0]);rejected=false;
      try{GesturePreferences.DecodeOriginalValues(originalSnapshot);}catch(InvalidDataException){rejected=true;}
      check(rejected,"Duplicate original gesture entries rejected before any system write");
      originalEntries.RemoveAt(originalEntries.Count-1);originalEntries.RemoveAt(0);rejected=false;
      try{GesturePreferences.DecodeOriginalValues(originalSnapshot);}catch(InvalidDataException){rejected=true;}
      check(rejected,"Incomplete original gesture snapshots rejected before any system write");
      SnapshotChecks(check);CliChecks(check);FailureChecks(check);DragReadinessChecks(check);GestureChoiceChecks(check);IntentWarningChecks(check);
      string parent=Path.GetDirectoryName(Path.GetFullPath(output));Directory.CreateDirectory(parent);
      string fixture=Path.GetFullPath(Path.Combine(parent,"runtime-fixture-"+Guid.NewGuid().ToString("N")));
      Directory.CreateDirectory(fixture);
      try {
        string file=Path.Combine(fixture,"options.json");
        var values=new Dictionary<string,object>{{"enabled",false},{"desktopHotkeys",true},{"extension",42}};
        AtomicJson.Write(file,values);
        check(File.Exists(file) && File.Exists(AtomicJson.RecoveryPath(file)),"Settings and recovery copy saved");
        check(((Dictionary<string,object>)AtomicJson.Read(file,null).Value).ContainsKey("extension"),"Unknown configuration fields retained");
        File.WriteAllText(file,"{broken");var recovered=AtomicJson.Read(file,null);
        check(recovered.Recovered && Convert.ToInt32(((Dictionary<string,object>)recovered.Value)["extension"])==42,"Damaged primary reads valid recovery copy");
        AtomicJson.PreserveDamaged(file);check(Directory.GetFiles(fixture,"options.damaged-*.json").Length==1,"Repair retains original damaged data");
        File.WriteAllText(AtomicJson.RecoveryPath(file),"broken");check(AtomicJson.Read(file,null).Unreadable,"Both unreadable files are reported without overwriting");
        string legacy=Path.Combine(fixture,"legacy"),target=Path.Combine(fixture,"current");Directory.CreateDirectory(Path.Combine(legacy,"state"));
        foreach(string name in new[]{"gesture-options.json","gesture-configuration.json","gestures-before.json","gesture-effective-before.json","pointer-speed-before.json"})AtomicJson.Write(Path.Combine(legacy,"state",name),values);
        File.WriteAllText(Path.Combine(legacy,"state","do-not-import.json"),"{\"private\":true}");
        AtomicJson.Write(Path.Combine(legacy,"state","baseline.json"),new {originalDriver="fixture-driver"});
        int copied=AppPaths.ImportLegacyState(legacy,target);
        check(copied==6 && File.Exists(Path.Combine(target,"state","gesture-configuration.json")),"Legacy options, activation and original gesture snapshots imported");
        check(File.Exists(Path.Combine(target,"state","legacy-driver-recovery.json")) && !File.Exists(Path.Combine(target,"state","baseline.json")),"Old driver recovery metadata retained separately without claiming new-package rollback");
        check(!File.Exists(Path.Combine(target,"state","do-not-import.json")),"Migration imports only known configuration files");
        check(AppPaths.ImportLegacyState(legacy,target)==0,"Migration never overwrites newer data");
        if(AppPaths.IsIsolated)check(AppPaths.MigrateLegacyState()==0,"Isolated preview never migrates real user state");
        AtomicChecks(fixture,check);
        InvalidCliProcesses(fixture,check);
      }finally {
        string normalized=Path.GetFullPath(fixture);
        if(Path.GetDirectoryName(normalized)==parent && Path.GetFileName(normalized).StartsWith("runtime-fixture-",StringComparison.Ordinal) && Directory.Exists(normalized))Directory.Delete(normalized,true);
      }
      File.WriteAllText(output,new JavaScriptSerializer().Serialize(new {success=true,checks=checks,systemSettingsWritten=false,inputInjected=false,realUserDataRead=false}));
    }
    static Dictionary<string,object> OriginalSnapshot() {
      var entries=new List<object>();
      foreach(string name in new[]{"ThreeFingerSlideEnabled","ThreeFingerTapEnabled","FourFingerSlideEnabled","FourFingerTapEnabled","TapAndDrag"})entries.Add(new Dictionary<string,object>{{"name",name},{"exists",true},{"kind","DWord"},{"value",1}});
      return new Dictionary<string,object>{{"values",entries}};
    }
    static bool Invalid(Action action) {try{action();return false;}catch(InvalidDataException){return true;}}
    static void GestureChoiceChecks(Action<bool,string> check) {
      var original=GesturePreferences.DecodeOriginalValues(OriginalSnapshot());
      var current=new Dictionary<string,int?>(original);current["FourFingerSlideEnabled"]=2;current["FourFingerTapEnabled"]=3;current["TapAndDrag"]=null;
      var plan=GesturePreferences.ValuesToApply(true,original);
      check(plan.Count==2 && plan.ContainsKey("ThreeFingerSlideEnabled") && plan.ContainsKey("ThreeFingerTapEnabled"),"Enabling dragging writes only the two conflicting three-finger registry actions");
      var applied=new Dictionary<string,int?>(current);foreach(var entry in plan)applied[entry.Key]=entry.Value;
      check(applied["FourFingerSlideEnabled"]==2 && applied["FourFingerTapEnabled"]==3,"Applying the drag configuration preserves both current four-finger choices");
      check(applied["ThreeFingerSlideEnabled"]==0 && applied["ThreeFingerTapEnabled"]==0,"The drag configuration still disables both conflicting three-finger native actions");
      foreach(int nativeAction in new[]{0,1,2}) {
        var chosen=new Dictionary<string,int?>(current);chosen["FourFingerSlideEnabled"]=nativeAction;
        foreach(var entry in plan)chosen[entry.Key]=entry.Value;
        check(chosen["FourFingerSlideEnabled"]==nativeAction && GesturePreferences.DragPreferencesReady(new GesturePreferences.CacheValues{threeTap=0,threeSlide=0,fourSlide=(uint)nativeAction}),"Enabling dragging preserves and accepts the chosen native four-finger action: "+nativeAction);
      }
      check(original["FourFingerSlideEnabled"]==1 && current["FourFingerSlideEnabled"]==2,"Planning drag setup does not overwrite the full original snapshot or current values");
      var differentLive=new GesturePreferences.CacheValues{threeTap=0,threeSlide=0,fourSlide=3};
      check(GesturePreferences.DragPreferencesReady(differentLive) && differentLive.fourSlide==3 && applied["FourFingerSlideEnabled"]==2,"Separate registry and live-cache four-finger choices are preserved without normalizing either layer");
      foreach(uint flags in new[]{0u,8u,0xA5A55A5Au,uint.MaxValue}) {
        uint disabled=GesturePreferences.TapAndDragFlags(flags,false),enabled=GesturePreferences.TapAndDragFlags(flags,true);
        check((disabled&~8u)==(flags&~8u) && (disabled&8u)==0 && (enabled&~8u)==(flags&~8u) && (enabled&8u)!=0,"The native tap-and-drag update preserves every unrelated user flag: "+flags.ToString("X8"));
      }
      var restore=GesturePreferences.ValuesToApply(false,original);
      check(restore.Count==5 && restore["FourFingerSlideEnabled"]==1 && restore["FourFingerTapEnabled"]==1 && restore["TapAndDrag"]==1,"Explicit restoration retains the full original five-value semantics");
      restore["FourFingerSlideEnabled"]=3;check(original["FourFingerSlideEnabled"]==1,"A restore plan cannot mutate its retained original snapshot");
      var incomplete=new Dictionary<string,int?>(original);incomplete.Remove("FourFingerTapEnabled");
      check(Invalid(delegate{GesturePreferences.ValuesToApply(false,incomplete);}),"Explicit restoration still rejects incomplete four-finger original evidence");
      var simulated=new Dictionary<string,int?>(current);bool failed=false;
      try{GestureTransaction.Run<int>(delegate{foreach(var entry in plan)simulated[entry.Key]=entry.Value;throw new IOException("fixture drag-apply failure");},delegate{foreach(var entry in current)simulated[entry.Key]=entry.Value;});}catch(InvalidOperationException){failed=true;}
      bool restored=failed;foreach(var entry in current)restored&=simulated.ContainsKey(entry.Key) && simulated[entry.Key]==entry.Value;
      check(restored,"Failed drag setup restores the complete five-value pre-operation snapshot including both four-finger choices");
    }
    static void IntentWarningChecks(Action<bool,string> check) {
      check(!AppController.ConfigurationNeedsAttention(false,true,false,false),"An intentionally paused drag does not ask the user to configure it");
      check(!AppController.ConfigurationNeedsAttention(true,false,false,false),"A locked or disconnected session defers configuration attention");
      check(!AppController.ConfigurationNeedsAttention(true,true,true,false),"Isolated preview does not request real Windows gesture changes");
      check(AppController.ConfigurationNeedsAttention(true,true,false,false),"Requested dragging with conflicting native gestures still needs configuration");
      check(!AppController.ConfigurationNeedsAttention(true,true,false,true),"A ready active drag does not request configuration");
      const string conflict="Ctrl＋左右键注册失败，可能被其他程序占用。";
      check(AppController.ShortcutWarning(false,true,false,false,conflict)==null,"Deliberately disabled desktop shortcuts do not surface a stale conflict");
      check(AppController.ShortcutWarning(true,false,false,false,conflict)==null,"Session-paused desktop shortcuts do not report intentional unregistration");
      check(AppController.ShortcutWarning(true,true,true,false,null)==null,"Requested preview shortcuts are intentionally unregistered without warnings");
      check(AppController.ShortcutWarning(true,true,false,false,conflict)==conflict,"A requested real shortcut registration conflict remains actionable");
      check(AppController.ShortcutWarning(true,true,false,true,conflict)==conflict,"A real shortcut send failure is retained even while registration is active");
      check(AppController.ShortcutWarning(true,true,false,true,null)==null,"Healthy registered shortcuts do not produce attention warnings");
      check(!string.IsNullOrWhiteSpace(AppController.ShortcutWarning(true,true,false,false,null)),"Unexpectedly unregistered requested shortcuts keep a clear fallback warning");
    }
    static void DragReadinessChecks(Action<bool,string> check) {
      var desktops=new GesturePreferences.CacheValues{threeTap=0,threeSlide=0,fourSlide=2};
      check(GesturePreferences.DragPreferencesReady(desktops),"Four-finger desktop switching does not block three-finger dragging");
      check(desktops.fourSlide==2,"Checking drag readiness preserves the user's four-finger action");
      check(!GesturePreferences.DragPreferencesReady(new GesturePreferences.CacheValues{threeTap=1,threeSlide=0,fourSlide=2}),"A conflicting three-finger tap still blocks dragging with desktop switching");
      check(!GesturePreferences.DragPreferencesReady(new GesturePreferences.CacheValues{threeTap=0,threeSlide=1,fourSlide=2}),"A conflicting three-finger slide still blocks dragging with desktop switching");
      check(!GesturePreferences.DragPreferencesReady(null),"Unavailable three-finger live state remains safely paused");
      check(GesturePreferences.DragPreferencesReady(new GesturePreferences.CacheValues{threeTap=0,threeSlide=0,fourSlide=0}),"Four-finger gestures disabled in Windows do not block dragging");
      check(GesturePreferences.DragPreferencesReady(new GesturePreferences.CacheValues{threeTap=0,threeSlide=0,fourSlide=1}),"Four-finger app switching remains compatible with dragging");
    }
    static void FailureChecks(Action<bool,string> check) {
      using(var logger=new Logger()) {
        logger.SuppressDisk=true;int delivered=0;
        logger.LineAdded+=delegate{throw new InvalidOperationException("fixture closing diagnostic view");};
        logger.LineAdded+=delegate{delivered++;};
        logger.Error("fixture error details");
        check(delivered==1 && logger.Snapshot().Contains("fixture error details"),"A failed log subscriber cannot hide diagnostics or interrupt input cleanup");
        for(int i=0;i<350;i++)logger.Info("bounded fixture "+i);
        check(logger.Snapshot().Split(new[]{Environment.NewLine},StringSplitOptions.None).Length==300,"Exception diagnostics keep the existing bounded in-memory log");
      }
      string failure=InputWindow.RegistrationFailureText(5);
      check(failure.Contains("未能启动") && failure.Contains("重试") && !failure.Contains("Raw Input"),"Listener failure is localized and explains how to retry");
      string reason=AppController.FriendlyRestoreReason(new IOException("System.IO.IOException: fixture private path"));
      check(!reason.Contains("System.IO") && !reason.Contains("private path"),"Raw framework restore errors stay out of user status");
      reason=AppController.FriendlyRestoreReason(new InvalidDataException("旧版备份缺少原始轻点拖移状态，请到 Windows 设置手动恢复。"));
      check(reason.Contains("轻点拖移") && reason.Contains("手动恢复"),"Actionable Chinese legacy-backup recovery instructions remain visible");
      foreach(bool continuing in new[]{true,false}) {
        string message=FailureDialog.MessageFor(continuing);
        check(!string.IsNullOrWhiteSpace(message) && !message.Contains("Exception") && !message.Contains("System.") && !message.Contains(" at "),"Unexpected-error dialog uses fixed Chinese copy: "+(continuing?"paused":"startup"));
      }
    }
    static void SnapshotChecks(Action<bool,string> check) {
      var original=OriginalSnapshot();var decoded=GesturePreferences.DecodeOriginalValues(original);
      check(!GesturePreferences.OriginalTapAndDrag(original,decoded).HasValue,"Legacy registry values never invent the independent native tap-and-drag bit");
      original["tapAndDragEnabled"]=true;check(GesturePreferences.OriginalTapAndDrag(original,decoded)==true,"New snapshot retains a true native tap-and-drag bit");
      original["tapAndDragEnabled"]=false;check(GesturePreferences.OriginalTapAndDrag(original,decoded)==false,"New snapshot retains a false native tap-and-drag bit");
      original["tapAndDragEnabled"]=1;check(Invalid(delegate{GesturePreferences.OriginalTapAndDrag(original,decoded);}),"Malformed native-bit evidence is rejected before writes");
      foreach(object number in new object[]{null,true,"1",1.5,-2147483649L,4294967296L,double.NaN,double.PositiveInfinity}) {
        original=OriginalSnapshot();((Dictionary<string,object>)((List<object>)original["values"])[4])["value"]=number;
        check(Invalid(delegate{GesturePreferences.DecodeOriginalValues(original);}),"Original snapshot rejects invalid last-field DWORD: "+(number==null?"null":Convert.ToString(number)));
      }
      original=OriginalSnapshot();((Dictionary<string,object>)((List<object>)original["values"])[0])["value"]=uint.MaxValue;
      check(GesturePreferences.DecodeOriginalValues(original)["ThreeFingerSlideEnabled"]==-1,"Unsigned DWORD original retains all 32 bits");
      var effective=new Dictionary<string,object>{{"threeTap",0},{"threeSlide",0},{"fourSlide",uint.MaxValue}};
      check(GesturePreferences.DecodeEffectiveValues(effective).fourSlide==uint.MaxValue,"Complete effective snapshot accepts the unsigned DWORD boundary");
      foreach(object number in new object[]{null,true,"1",1.5,-1,4294967296L}) {
        effective["fourSlide"]=number;
        check(Invalid(delegate{GesturePreferences.DecodeEffectiveValues(effective);}),"Effective snapshot rejects invalid last-field value: "+(number==null?"null":Convert.ToString(number)));
      }
      effective.Remove("fourSlide");check(Invalid(delegate{GesturePreferences.DecodeEffectiveValues(effective);}),"Missing effective fields fail before any system mutation");
      int simulated=1;bool failed=false;
      try{GestureTransaction.Run<int>(delegate{simulated=2;throw new IOException("fixture mutation failure");},delegate{simulated=1;});}catch(InvalidOperationException error){failed=error.InnerException is IOException;}
      check(failed && simulated==1,"Failed gesture mutation restores the complete simulated previous state");
      int attempted=0;failed=false;
      try{GestureTransaction.RestoreAll(new Action[]{delegate{attempted++;throw new IOException("fixture restore failure");},delegate{attempted++;},delegate{attempted++;throw new InvalidOperationException("second fixture failure");}});}catch(AggregateException error){failed=error.InnerExceptions.Count==2;}
      check(failed && attempted==3,"Rollback attempts every independent restore and reports all failures");
      failed=false;try{GestureTransaction.Run<int>(delegate{throw new IOException("fixture failure");},delegate{throw new IOException("fixture restore failure");});}catch(InvalidOperationException error){failed=error.InnerException is AggregateException && error.Message.IndexOf("可能已更改",StringComparison.Ordinal)>=0;}
      check(failed,"Incomplete rollback is explicitly reported instead of claiming recovery success");
    }
    static void CliChecks(Action<bool,string> check) {
      CliOptions parsed;
      foreach(string[] values in new[]{new[]{"--unknown"},new[]{"--set-speed","13"},new[]{"--set-speed","0","report.json"},new[]{"--set-speed","21","report.json"},new[]{"--set-speed","1.5","report.json"},new[]{"--set-speed","10","report.json","extra"},new[]{"--query-speed"},new[]{"--shutdown","extra"},new[]{"--configure",""},new[]{"--query-config","--shutdown"},new[]{"--preview","--preview"}})
        check(!CliOptions.TryParse(values,out parsed),"Strict CLI rejects: "+string.Join(" ",values));
      foreach(string verb in new[]{"--self-test-runtime","--test-speed-response","--query-speed","--restore-speed","--test-speed","--test-speed-undo","--configure","--restore","--query-config","--self-test","--self-test-available","--render","--render-diagnostics","--render-appearance","--render-compact"}) {
        check(CliOptions.TryParse(new[]{verb,"report.json"},out parsed),"Strict CLI preserves valid verb: "+verb);
        check(!CliOptions.TryParse(new[]{verb},out parsed) && !CliOptions.TryParse(new[]{verb,"report.json","extra"},out parsed),"Strict CLI rejects incomplete/extra arguments for: "+verb);
      }
      check(CliOptions.TryParse(new[]{"--preview","--set-speed","1","report.json"},out parsed) && parsed.Preview && parsed.Arguments.Length==3,"Preview CLI retains exact normalized argument arity");
      check(CliOptions.TryParse(new[]{"--set-speed","20","report.json"},out parsed),"Strict CLI accepts the upper speed boundary");
      foreach(string verb in new[]{"--render","--render-diagnostics","--render-appearance","--render-compact"})check(CliOptions.TryParse(new[]{verb,"report.png"},out parsed) && parsed.Preview,"Rendering automatically isolates: "+verb);
    }
    static void AtomicChecks(string fixture,Action<bool,string> check) {
      string file=Path.Combine(fixture,"atomic.json");AtomicJson.Write(file,new{version=1});
      string oldPrimary=File.ReadAllText(file),oldMirror=File.ReadAllText(AtomicJson.RecoveryPath(file));bool rejected=false;
      using(var locked=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))try{AtomicJson.Write(file,new{version=2});}catch(IOException){rejected=true;}
      check(rejected && File.ReadAllText(file)==oldPrimary && File.ReadAllText(AtomicJson.RecoveryPath(file))==oldMirror,"Failed primary commit rolls the recovery copy back to its previous bytes");
      rejected=false;
      using(var locked=new FileStream(AtomicJson.RecoveryPath(file),FileMode.Open,FileAccess.Read,FileShare.Read))try{AtomicJson.Write(file,new{version=3});}catch(IOException){rejected=true;}
      check(rejected && File.ReadAllText(file)==oldPrimary && File.ReadAllText(AtomicJson.RecoveryPath(file))==oldMirror,"Locked recovery copy leaves both previous versions untouched");
      using(var acquired=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)) {
        Exception holderFailure=null;
        var holder=new Thread(delegate(){try{AtomicJson.WithLock(file,delegate{acquired.Set();release.WaitOne();return true;});}catch(Exception error){holderFailure=error;acquired.Set();}});
        holder.Start();try {
          if(!acquired.WaitOne(5000) || holderFailure!=null)throw new InvalidOperationException("Fixture failed to acquire its settings lock",holderFailure);
          rejected=false;try{AtomicJson.Write(file,new{version=4});}catch(IOException){rejected=true;}
          check(rejected,"An independent lock holder causes a clear write timeout without overwriting data");
          check(AtomicJson.Read(file,null).Unreadable,"Busy reads fail closed rather than silently loading defaults");
        }finally{release.Set();if(!holder.Join(5000))throw new InvalidOperationException("Fixture lock holder did not exit");}
      }
      check(File.ReadAllText(file)==oldPrimary && File.ReadAllText(AtomicJson.RecoveryPath(file))==oldMirror,"Lock contention preserves both original copies");
      Exception writeFailure=null;object failureLock=new object();
      ThreadStart first=delegate {try{for(int i=0;i<20;i++)AtomicJson.Write(file,new{writer=Thread.CurrentThread.ManagedThreadId,revision=i});}catch(Exception error){lock(failureLock){writeFailure=error;}}};
      var one=new Thread(first);var two=new Thread(first);one.Start();two.Start();one.Join();two.Join();
      if(writeFailure!=null)throw new InvalidOperationException("Concurrent fixture settings writes failed",writeFailure);
      check(AtomicJson.WithLock(file,delegate{return File.ReadAllText(file)==File.ReadAllText(AtomicJson.RecoveryPath(file));}),"Concurrent settings writers leave matching primary and recovery versions");
      check(Directory.GetFiles(fixture,"*.tmp").Length==0 && Directory.GetFiles(fixture,"*.rollback").Length==0,"Completed and rolled-back saves leave no abandoned staging files");
    }
    static void InvalidCliProcesses(string fixture,Action<bool,string> check) {
      string executable=System.Reflection.Assembly.GetExecutingAssembly().Location;
      if(!string.Equals(Path.GetFileName(executable),"OpenTrackpad.exe",StringComparison.OrdinalIgnoreCase))return;
      int count=0;
      foreach(string arguments in new[]{"--unknown","--set-speed 13","--set-speed 0 output.json","--set-speed 21 output.json","--set-speed 10 output.json extra","--query-speed","--shutdown extra","--render-appearance","--configure","--preview --preview"}) {
        var start=new ProcessStartInfo(executable,arguments){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=fixture};
        start.EnvironmentVariables["OPENTRACKPAD_DATA_DIR"]=Path.Combine(fixture,"invalid-cli-"+count++);
        start.EnvironmentVariables.Remove("OPENTRACKPAD_LEGACY_DIR");
        using(Process process=Process.Start(start)) {
          if(!process.WaitForExit(5000)){process.Kill();throw new InvalidOperationException("Invalid isolated CLI failed to exit promptly: "+arguments);}
          check(process.ExitCode==2,"Isolated invalid CLI exits 2 before IPC/system setup: "+arguments);
        }
      }
      check(Directory.GetDirectories(fixture,"invalid-cli-*").Length==0 && !File.Exists(Path.Combine(fixture,"output.json")),"Rejected CLI creates no application state or report files");
    }
  }
}

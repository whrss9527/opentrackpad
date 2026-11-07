using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using OpenTrackpadShared;

namespace ThreeFingerDrag {
  // Registry values are the Windows user's own touchpad preferences. No driver,
  // elevation, Explorer restart or security setting is involved.
  static class GesturePreferences {
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";
    const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "OpenTrackpadThreeFingerDrag";
    static readonly string[] Names = {"ThreeFingerSlideEnabled","ThreeFingerTapEnabled","FourFingerSlideEnabled","FourFingerTapEnabled","TapAndDrag"};
    static readonly int[] CacheIds = {15,17,18};
    static string StatePath {get{return AppPaths.StateDirectory;}}
    static bool previewAutostart;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    public static bool LastReloadPending;
    public static string LastReadinessDetail="not checked";

    public static object Query() {
      if(AppPaths.IsIsolated)return new {success=true,preview=true,autostartEnabled=previewAutostart,sessionReady=true,systemSettingsChanged=false};
      using(SettingsCache cache = new SettingsCache()) {
        TouchpadParameters parameters = GetParameters();
        return new { capturedAt=DateTime.Now.ToString("o"), values=CaptureValues(), cache=cache.Read(),cacheValues=cache.Dump(),
          tapAndDragEnabled=(parameters.userFlags & 8)!=0,
          autostartEnabled=HasAutostart(),sessionReady=IsSessionReady(),inputDesktop=Native.InputDesktopName(), size=Marshal.SizeOf(typeof(TouchpadParameters)) };
      }
    }

    public static string LastRecoveryWarning=string.Empty;

    public static object Apply(bool enabled) {
      LastReloadPending=false;LastRecoveryWarning=string.Empty;
      if(AppPaths.IsIsolated){if(!enabled)previewAutostart=false;return new {success=true,preview=true,enabled=enabled,systemSettingsChanged=false};}
      return SystemSettingOperation.Run(delegate {return ApplyCore(enabled);});
    }

    static object ApplyCore(bool enabled) {
      Directory.CreateDirectory(StatePath);
      string backupPath=Path.Combine(StatePath,"gestures-before.json"),effectivePath=Path.Combine(StatePath,"gesture-effective-before.json");
      JsonRead previous=AtomicJson.Read(backupPath,null),effectiveRead=AtomicJson.Read(effectivePath,null);
      if(previous.Unreadable)throw new InvalidDataException("原始手势备份无法读取，Windows 手势尚未改动，原文件已保留。");
      if(effectiveRead.Unreadable)throw new InvalidDataException("原始生效状态备份无法读取，Windows 手势尚未改动，原文件已保留。");
      var original=previous.Value as Dictionary<string,object>;
      Dictionary<string,int?> originalValues=null;
      bool? originalTap=null;
      if(previous.Value!=null) {
        if(original==null)throw new InvalidDataException("原始手势备份格式不正确，Windows 手势尚未改动。");
        originalValues=DecodeOriginalValues(original);originalTap=OriginalTapAndDrag(original,originalValues);
      }
      CacheValues originalEffective=effectiveRead.Value==null?null:DecodeEffectiveValues(effectiveRead.Value);
      string configurationFile=Path.Combine(StatePath,"gesture-configuration.json");
      if(originalValues==null && (effectiveRead.Found || File.Exists(configurationFile) || File.Exists(AtomicJson.RecoveryPath(configurationFile))))throw new InvalidDataException("原始手势备份缺失，不能用当前已修改状态猜测原值。其余资料已保留，请到 Windows 设置手动确认。");
      if(!enabled && originalValues==null)throw new InvalidDataException("未找到原始手势备份，Windows 手势尚未改动。请在 Windows 触摸板设置中手动恢复。");
      if(!enabled && originalEffective==null)throw new InvalidDataException("旧版备份缺少原始生效状态。已保留备份且未改动 Windows 手势；请到 Windows 触摸板设置手动恢复。");
      if(!enabled && !originalTap.HasValue)throw new InvalidDataException("旧版备份缺少原始轻点拖移状态。已保留备份且未改动 Windows 手势；请到 Windows 触摸板设置手动恢复。");
      if(enabled && originalValues!=null && !originalTap.HasValue)LastRecoveryWarning="旧版备份缺少原始轻点拖移状态；以后恢复此项时需要到 Windows 设置手动确认。";
      if(enabled && originalValues!=null && originalEffective==null)LastRecoveryWarning="旧版原始生效状态备份不完整；以后恢复手势时需要到 Windows 设置手动确认。";
      using(SettingsCache cache=new SettingsCache()) {
        TouchpadParameters beforeParameters=GetParameters();
        object[] beforeEntries=CaptureValues();
        var beforeValues=DecodeOriginalValues(new Dictionary<string,object>{{"values",beforeEntries}});
        CacheValues beforeEffective=cache.Read();
        if(originalValues==null) {
          AtomicJson.Write(backupPath,new {snapshotVersion=2,capturedAt=DateTime.Now.ToString("o"),path="HKCU:\\"+KeyPath,values=beforeEntries,tapAndDragEnabled=(beforeParameters.userFlags&8)!=0});
          originalTap=(beforeParameters.userFlags&8)!=0;
        }
        if(originalEffective==null && originalValues==null) {
          originalEffective=beforeEffective;
          AtomicJson.Write(effectivePath,beforeEffective);
        }
        var requestedValues=ValuesToApply(enabled,originalValues);
        IEnumerable<string> changedNames=requestedValues.Keys;
        StartupEntry previousStartup=enabled?null:CaptureStartup();
        object result=GestureTransaction.Run(delegate {
          WriteValues(requestedValues,changedNames);
          SetTapAndDrag(enabled?false:originalTap.Value);
          cache.Reload();
          CacheValues effective=cache.Read();
          if(enabled) {
            for(int attempt=0;attempt<20 && !DragPreferencesReady(effective);attempt++) {
              System.Threading.Thread.Sleep(50);effective=cache.Read();
            }
          }
          TouchpadParameters verified=GetParameters();
          bool expectedTap=!enabled && originalTap.Value;
          if(((verified.userFlags&8)!=0)!=expectedTap)throw new InvalidOperationException("轻点拖移设置尚未生效。");
          var applied=DecodeOriginalValues(new Dictionary<string,object>{{"values",CaptureValues()}});
          foreach(string name in changedNames) {
            int? expected=requestedValues[name];
            if(applied[name]!=expected)throw new InvalidOperationException("Windows 尚未保存完整手势设置。");
          }
          LastReloadPending=enabled?!DragPreferencesReady(effective):effective.threeTap!=originalEffective.threeTap || effective.threeSlide!=originalEffective.threeSlide || effective.fourSlide!=originalEffective.fourSlide;
          if(!enabled)SetAutostartCore(false);
          object recorded=new {success=true,capturedAt=DateTime.Now.ToString("o"),configuredLogon=Native.LogonSessionId(),mode=enabled?"three-finger-drag / windows-four-finger-gestures":"original-windows-gestures",values=CaptureValues(),cache=effective,requiresShellReload=LastReloadPending,tapAndDragEnabled=(verified.userFlags&8)!=0,autostartEnabled=HasAutostart(),backup=backupPath,restorationWarning=LastRecoveryWarning};
          AtomicJson.Write(Path.Combine(StatePath,"gesture-configuration.json"),recorded);
          return recorded;
        },delegate {
          var restores=new List<Action>();
          // Every independent value must be attempted even when another restore
          // fails. Retain the original backup and report all incomplete work.
          foreach(string savedName in Names) {
            string name=savedName;restores.Add(delegate {WriteValues(beforeValues,new[]{name});});
          }
          restores.Add(delegate {SetTapAndDrag((beforeParameters.userFlags&8)!=0);});
          restores.Add(delegate {cache.Reload();});
          if(previousStartup!=null)restores.Add(delegate {RestoreStartup(previousStartup);});
          restores.Add(delegate {
            var restored=DecodeOriginalValues(new Dictionary<string,object>{{"values",CaptureValues()}});
            foreach(string name in Names)if(restored[name]!=beforeValues[name])throw new InvalidOperationException("注册表手势值未恢复。");
          });
          restores.Add(delegate {if(((GetParameters().userFlags&8)!=0)!=((beforeParameters.userFlags&8)!=0))throw new InvalidOperationException("轻点拖移状态未恢复。");});
          restores.Add(delegate {var live=cache.Read();if(live.threeTap!=beforeEffective.threeTap || live.threeSlide!=beforeEffective.threeSlide || live.fourSlide!=beforeEffective.fourSlide)throw new InvalidOperationException("生效状态仍需在 Windows 设置中确认。");});
          GestureTransaction.RestoreAll(restores);
        });
        return result;
      }
    }
    static void SetTapAndDrag(bool enabled) {
      // Read immediately before changing our one owned bit; preserve current speed,
      // haptics, sensitivity and unrelated flags, including changes from Windows.
      TouchpadParameters parameters=GetParameters();
      parameters.userFlags=TapAndDragFlags(parameters.userFlags,enabled);
      if(!SystemParametersInfo(0xAF,(uint)Marshal.SizeOf(typeof(TouchpadParameters)),ref parameters,3))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"无法应用轻点拖移设置。");
    }
    internal static uint TapAndDragFlags(uint current,bool enabled){return enabled?current|8u:current&~8u;}
    static void WriteValues(Dictionary<string,int?> values,IEnumerable<string> names) {
      using(RegistryKey key=Registry.CurrentUser.CreateSubKey(KeyPath))foreach(string name in names) {
        int? value=values[name];if(value.HasValue)key.SetValue(name,value.Value,RegistryValueKind.DWord);else key.DeleteValue(name,false);
      }
    }
    internal static Dictionary<string,int?> ValuesToApply(bool enabled,Dictionary<string,int?> original) {
      if(enabled)return new Dictionary<string,int?>{{"ThreeFingerSlideEnabled",0},{"ThreeFingerTapEnabled",0}};
      if(original==null || original.Count!=Names.Length)throw new InvalidDataException("Original gesture snapshot is incomplete");
      foreach(string name in Names)if(!original.ContainsKey(name))throw new InvalidDataException("Original gesture snapshot is incomplete");
      return new Dictionary<string,int?>(original);
    }
    internal static bool? OriginalTapAndDrag(Dictionary<string,object> snapshot,Dictionary<string,int?> values) {
      object explicitValue;
      if(snapshot.TryGetValue("tapAndDragEnabled",out explicitValue)) {
        if(!(explicitValue is bool))throw new InvalidDataException("原始轻点拖移状态格式不正确，Windows 手势尚未改动。");
        return (bool)explicitValue;
      }
      // A registry DWORD does not prove the independent native bit at capture
      // time. Never infer an old native original from today's modified state.
      return null;
    }
    internal static CacheValues DecodeEffectiveValues(object snapshot) {
      var data=snapshot as Dictionary<string,object>;
      if(data==null)throw new InvalidDataException("原始生效状态备份格式不正确，Windows 手势尚未改动。");
      uint[] values=new uint[3];string[] names={"threeTap","threeSlide","fourSlide"};
      for(int i=0;i<names.Length;i++) {
        object value;if(!data.TryGetValue(names[i],out value) || value==null || value is bool || value is string)throw new InvalidDataException("原始生效状态备份不完整，Windows 手势尚未改动。");
        try{values[i]=(uint)IntegralNumber(value,0,uint.MaxValue);}catch{throw new InvalidDataException("原始生效状态备份含无效数值，Windows 手势尚未改动。");}
      }
      return new CacheValues{threeTap=values[0],threeSlide=values[1],fourSlide=values[2]};
    }

    public static bool HasAutostart() {
      if(AppPaths.IsIsolated)return previewAutostart;
      using(RegistryKey key=Registry.CurrentUser.OpenSubKey(RunPath)) return key!=null && key.GetValue(RunName)!=null;
    }

    public static bool IsSessionReady() {
      if(AppPaths.IsIsolated){LastReadinessDetail="isolated preview; no system gesture changes";return true;}
      try {
        LastReadinessDetail="checking saved values";
        using(RegistryKey key=Registry.CurrentUser.OpenSubKey(KeyPath)) {
          if(key==null || Convert.ToInt32(key.GetValue("ThreeFingerSlideEnabled",-1))!=0 ||
             Convert.ToInt32(key.GetValue("ThreeFingerTapEnabled",-1))!=0){LastReadinessDetail="saved three-finger gesture values are not ready";return false;}
        }
        string file=Path.Combine(StatePath,"gesture-configuration.json");
        if(!File.Exists(file) && !File.Exists(AtomicJson.RecoveryPath(file))){LastReadinessDetail="configuration file is missing";return false;}
        var data=ReadSnapshot(file);
        if(!Convert.ToString(data["mode"]).StartsWith("three-finger-drag")){LastReadinessDetail="native gesture mode is selected";return false;}
        bool sessionChanged=CanActivateAfterSignIn(
          !data.ContainsKey("requiresShellReload") || Convert.ToBoolean(data["requiresShellReload"]),
          data.ContainsKey("configuredLogon")?Convert.ToString(data["configuredLogon"]):string.Empty,
          Native.LogonSessionId(),DateTime.Parse(Convert.ToString(data["capturedAt"])).ToUniversalTime(),Native.BootTimeUtc);
        if(sessionChanged){LastReadinessDetail="saved activation or new sign-in verified";return true;}
        // The Settings app can apply gestures immediately in this same session.
        // A saved "pending" flag must not permanently block verified live values.
        using(SettingsCache cache=new SettingsCache()) {
          CacheValues live=cache.Read();
          LastReadinessDetail="live gestures: "+live.threeTap+"/"+live.threeSlide+"/"+live.fourSlide;
          return DragPreferencesReady(live);
        }
      }catch(Exception exception){LastReadinessDetail=exception.GetType().Name+": "+exception.Message;return false;}
    }

    internal static bool DragPreferencesReady(CacheValues values) {
      // Starting three-finger dragging must respect the user's independently
      // chosen Windows four-finger action (desktops, apps, volume or none).
      return values!=null && values.threeTap==0 && values.threeSlide==0;
    }
    internal static bool CanActivateAfterSignIn(bool pending,string original,string current,DateTime configuredUtc,DateTime bootUtc) {
      return !pending || (original.Length>0 && current.Length>0 && original!=current) || configuredUtc<bootUtc;
    }

    public static void SetAutostart(bool enabled) {
      if(AppPaths.IsIsolated){previewAutostart=enabled;return;}
      SystemSettingOperation.Run(delegate{SetAutostartCore(enabled);return true;});
    }
    static void SetAutostartCore(bool enabled) {
      using(RegistryKey key=Registry.CurrentUser.CreateSubKey(RunPath)) {
        if(enabled)key.SetValue(RunName,"\""+System.Windows.Forms.Application.ExecutablePath+"\" --background",RegistryValueKind.String);
        else key.DeleteValue(RunName,false);
      }
    }
    sealed class StartupEntry {public bool Exists;public object Value;public RegistryValueKind Kind;}
    static StartupEntry CaptureStartup() {
      using(RegistryKey key=Registry.CurrentUser.OpenSubKey(RunPath)) {
        object value=key==null?null:key.GetValue(RunName,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
        return new StartupEntry{Exists=value!=null,Value=value,Kind=value==null?RegistryValueKind.String:key.GetValueKind(RunName)};
      }
    }
    static void RestoreStartup(StartupEntry entry) {
      using(RegistryKey key=Registry.CurrentUser.CreateSubKey(RunPath)) {
        if(entry.Exists)key.SetValue(RunName,entry.Value,entry.Kind);else key.DeleteValue(RunName,false);
      }
      StartupEntry checkedEntry=CaptureStartup();
      if(checkedEntry.Exists!=entry.Exists || entry.Exists && (checkedEntry.Kind!=entry.Kind || !EquivalentRegistryValue(checkedEntry.Value,entry.Value)))throw new InvalidOperationException("登录启动项未能恢复。");
    }
    static bool EquivalentRegistryValue(object left,object right) {
      if(left is Array && right is Array)return System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals(left,right);
      return object.Equals(left,right);
    }

    static object[] CaptureValues() {
      var values=new List<object>();
      using(RegistryKey key=Registry.CurrentUser.OpenSubKey(KeyPath)) {
        foreach(string name in Names) {
          object value=key==null?null:key.GetValue(name,null);
          values.Add(new Dictionary<string,object>{{"name",name},{"exists",value!=null},{"value",value},{"kind",value==null?null:key.GetValueKind(name).ToString()}});
        }
      }
      return values.ToArray();
    }
    static Dictionary<string,object> ReadSnapshot(string path) {
      var result=AtomicJson.Read(path,null).Value as Dictionary<string,object>;
      if(result==null)throw new InvalidDataException("Saved gesture snapshot is missing or unreadable: "+Path.GetFileName(path));
      return result;
    }

    static void RestoreValues(string path) {
      var values=DecodeOriginalValues(ReadSnapshot(path));
      using(RegistryKey key=Registry.CurrentUser.CreateSubKey(KeyPath)) {
        foreach(var value in values) {
          if(value.Value.HasValue)key.SetValue(value.Key,value.Value.Value,RegistryValueKind.DWord);
          else key.DeleteValue(value.Key,false);
        }
      }
    }
    internal static Dictionary<string,int?> DecodeOriginalValues(Dictionary<string,object> snapshot) {
      if(snapshot==null || !snapshot.ContainsKey("values") || !(snapshot["values"] is System.Collections.IEnumerable))throw new InvalidDataException("Original gesture values are missing");
      var values=new Dictionary<string,int?>();
      foreach(object item in (System.Collections.IEnumerable)snapshot["values"]) {
        var value=item as Dictionary<string,object>;
        if(value==null || !value.ContainsKey("name") || !value.ContainsKey("exists") || !(value["exists"] is bool))throw new InvalidDataException("Invalid gesture snapshot entry");
        string name=value["name"] as string;
        if(name==null || Array.IndexOf(Names,name)<0 || values.ContainsKey(name))throw new InvalidDataException("Unexpected or duplicate gesture preference in backup");
        int? number=null;
        if((bool)value["exists"]) {
          if(!value.ContainsKey("kind") || Convert.ToString(value["kind"])!="DWord" || !value.ContainsKey("value"))throw new InvalidDataException("Unsupported gesture value type");
          long candidate=IntegralNumber(value["value"],int.MinValue,uint.MaxValue);
          number=unchecked((int)candidate);
        }
        values.Add(name,number);
      }
      if(values.Count!=Names.Length)throw new InvalidDataException("Original gesture snapshot is incomplete");
      return values;
    }

    static long IntegralNumber(object value,long minimum,long maximum) {
      if(value==null || value is bool || value is string || value is char)throw new InvalidDataException("Gesture value must be a number");
      decimal number;
      try{number=Convert.ToDecimal(value);}catch{throw new InvalidDataException("Invalid gesture numeric value");}
      if(number!=decimal.Truncate(number) || number<minimum || number>maximum)throw new InvalidDataException("Gesture value outside integer DWORD range");
      return (long)number;
    }

    static TouchpadParameters GetParameters() {
      TouchpadParameters parameters=new TouchpadParameters(); parameters.version=1;
      if(!SystemParametersInfo(0xAE,(uint)Marshal.SizeOf(typeof(TouchpadParameters)),ref parameters,0))
        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Precision Touchpad settings API unavailable");
      return parameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TouchpadParameters {
      public uint version,maxContacts,legacyFeatures,systemFlags,userFlags;
      public uint sensitivity,cursorSpeed,feedbackIntensity,clickForceSensitivity,rightClickWidth,rightClickHeight;
    }
    [DllImport("user32.dll",EntryPoint="SystemParametersInfoW",SetLastError=true)]
    static extern bool SystemParametersInfo(uint action,uint size,ref TouchpadParameters parameters,uint flags);

    public sealed class CacheValues {
      public uint threeTap,threeSlide,fourSlide;
    }

    sealed class SettingsCache : IDisposable {
      object shell;
      ISettingsCache cache;
      bool initialized;
      public SettingsCache() {
        int build;
        using(RegistryKey key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
          build=Convert.ToInt32(key.GetValue("CurrentBuildNumber"));
        if(build<22621 || build>26200) throw new NotSupportedException("This settings notifier is verified for Windows 11 builds 22621-26200");
        int initializedResult=CoInitializeEx(IntPtr.Zero,2);
        if(initializedResult<0)Marshal.ThrowExceptionForHR(initializedResult);
        initialized=true;
        Guid clsid=new Guid("c2f03a33-21f5-47fa-b4bb-156362a2f239"),providerIid=new Guid("6d5140c1-7436-11ce-8034-00aa006009fa");
        IServices provider;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid,IntPtr.Zero,4,ref providerIid,out provider));
        shell=provider;
        Guid service=new Guid("53660488-8855-460b-a9ab-5cfc6b5012ca"),iid=new Guid("4214f6fa-eb36-4e2f-9ca2-23fdc1832df7");
        int result=((IServices)shell).QueryService(ref service,ref iid,out cache);
        Marshal.ThrowExceptionForHR(result);
        // The local Windows shell must be able to read this caller's HKCU.
        // This changes only this process's trusted local COM proxy, not OS policy.
        Marshal.ThrowExceptionForHR(CoSetProxyBlanket(cache,10,0,IntPtr.Zero,3,3,IntPtr.Zero,0));
      }
      public CacheValues Read() {
        CacheValues result=new CacheValues();
        Marshal.ThrowExceptionForHR(cache.GetDWORD(15,out result.threeTap));
        Marshal.ThrowExceptionForHR(cache.GetDWORD(17,out result.threeSlide));
        Marshal.ThrowExceptionForHR(cache.GetDWORD(18,out result.fourSlide));
        return result;
      }
      public void Reload() {foreach(int id in CacheIds)Marshal.ThrowExceptionForHR(cache.OnSettingChanged(id));}
      public object Dump() {
        var result=new Dictionary<string,string>();
        for(int i=0;i<=40;i++){uint value;int hr=cache.GetDWORD(i,out value);result[i.ToString()]=hr.ToString("X8")+":"+value;}
        return result;
      }
      public void Dispose() {
        if(cache!=null)Marshal.ReleaseComObject(cache);
        if(shell!=null)Marshal.ReleaseComObject(shell);
        if(initialized)CoUninitialize();
      }
    }
    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr reserved,uint flags);
    [DllImport("ole32.dll")]
    static extern void CoUninitialize();
    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid clsid,IntPtr outer,uint context,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IServices result);
    [DllImport("ole32.dll")]
    static extern int CoSetProxyBlanket([MarshalAs(UnmanagedType.Interface)]ISettingsCache proxy,uint authn,uint authz,IntPtr principal,uint authLevel,uint impersonation,IntPtr authInfo,uint capabilities);
    [ComImport,Guid("6d5140c1-7436-11ce-8034-00aa006009fa"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IServices {
      [PreserveSig]int QueryService(ref Guid service,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out ISettingsCache result);
    }
    [ComImport,Guid("4214f6fa-eb36-4e2f-9ca2-23fdc1832df7"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISettingsCache {
      [PreserveSig]int OnSettingChanged(int setting);
      [PreserveSig]int GetBOOL(int setting,out int value);
      [PreserveSig]int GetDWORD(int setting,out uint value);
      [PreserveSig]int RegisterForSettingChange(IntPtr listener,out uint cookie);
      [PreserveSig]int UnregisterForSettingChange(uint cookie);
    }
  }
}

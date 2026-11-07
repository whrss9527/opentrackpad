using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using OpenTrackpadShared;

namespace ThreeFingerDrag {
  static class PointerSpeedPolicy {
    public const double ReferenceWidthMm=125.0,ReferenceHeightMm=80.0;
    public static int Clamp(int speed){return Math.Max(1,Math.Min(20,speed));}
    public static double DragGain(int speed){return Clamp(speed)/10.0;}
    public static double PhysicalMillimetres(int minimum,int maximum,uint units,uint exponent,double fallback) {
      int system=(int)(units&15),power=(int)(exponent&15);
      if(power>=8)power-=16;
      if(maximum<=minimum || (units>>4)!=1 || (system!=1 && system!=3))return fallback;
      double result=((double)maximum-minimum)*Math.Pow(10,power)*(system==1?10.0:25.4);
      return double.IsNaN(result) || double.IsInfinity(result) || result<5 || result>1000?fallback:result;
    }
  }

  [StructLayout(LayoutKind.Sequential)]
  struct TouchpadSpeedParameters {
    public uint version,maxContacts,legacyFeatures,systemFlags,userFlags;
    public uint sensitivity,cursorSpeed,feedbackIntensity,clickForceSensitivity,rightClickWidth,rightClickHeight;
  }

  static class TouchpadSpeed {
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static string BackupPath {get{return AppPaths.StateFile("pointer-speed-before.json");}}
    static int previewSpeed=10;
    public static TouchpadSpeedParameters Read() {
      if(AppPaths.IsIsolated)return new TouchpadSpeedParameters {version=1,cursorSpeed=(uint)previewSpeed};
      TouchpadSpeedParameters data=new TouchpadSpeedParameters {version=1};
      if(!SystemParametersInfo(0xAE,(uint)Marshal.SizeOf(typeof(TouchpadSpeedParameters)),ref data,0))
        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Cannot read touchpad speed");
      return data;
    }
    public static int GetCursorSpeed(){return PointerSpeedPolicy.Clamp((int)Read().cursorSpeed);}
    public static object Query(){TouchpadSpeedParameters data=Read();return new {capturedAt=DateTime.Now.ToString("o"),cursorSpeed=data.cursorSpeed,dragGain=PointerSpeedPolicy.DragGain((int)data.cursorSpeed),physicalCalibration=true,parameters=data,backup=BackupPath};}
    public static void EnsureBackup() {
      if(AppPaths.IsIsolated){EnsureBackupCore();return;}
      SystemSettingOperation.Run(delegate{EnsureBackupCore();return true;});
    }
    static void EnsureBackupCore() {
      var existing=AtomicJson.Read(BackupPath,delegate(object value){var data=value as Dictionary<string,object>;return data!=null && data.ContainsKey("cursorSpeed") && Convert.ToInt32(data["cursorSpeed"])>=1 && Convert.ToInt32(data["cursorSpeed"])<=20;});
      if(existing.Unreadable)throw new InvalidDataException("Original speed backup is unreadable; it has been preserved");
      if(existing.Value!=null)return;
      TouchpadSpeedParameters before=Read();Directory.CreateDirectory(Path.GetDirectoryName(BackupPath));
      AtomicJson.Write(BackupPath,new {capturedAt=DateTime.Now.ToString("o"),cursorSpeed=before.cursorSpeed,parameters=before});
    }
    public static SpeedChange Change(int value) {
      if(AppPaths.IsIsolated)return ChangeCore(value);
      return SystemSettingOperation.Run(delegate{return ChangeCore(value);});
    }
    static SpeedChange ChangeCore(int value) {
      EnsureBackupCore();
      TouchpadSpeedParameters data=Read();int requested=PointerSpeedPolicy.Clamp(value);
      int before=PointerSpeedPolicy.Clamp((int)data.cursorSpeed);
      if(before==requested)return new SpeedChange{Before=before,After=before};
      if(AppPaths.IsIsolated){previewSpeed=requested;return new SpeedChange{Before=before,After=requested};}
      data.cursorSpeed=(uint)requested;
      // Persist the setting without synchronously waiting for every desktop
      // window. A slow settings receiver must never block input processing.
      if(!SystemParametersInfo(0xAF,(uint)Marshal.SizeOf(typeof(TouchpadSpeedParameters)),ref data,1))
        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Cannot update touchpad speed");
      int verified=GetCursorSpeed();if(verified!=requested)throw new InvalidOperationException("Touchpad speed did not apply");
      if(!SendNotifyMessage(new IntPtr(0xffff),0x1A,IntPtr.Zero,IntPtr.Zero))
        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Speed was saved but the settings notification failed");
      return new SpeedChange{Before=before,After=verified};
    }
    public static object Apply(int value){Change(value);return Query();}
    public static int OriginalSpeed(){var read=AtomicJson.Read(BackupPath,null);var backup=read.Value as Dictionary<string,object>;if(backup==null || !backup.ContainsKey("cursorSpeed"))throw new InvalidDataException("Original speed backup is missing or unreadable");return PointerSpeedPolicy.Clamp(Convert.ToInt32(backup["cursorSpeed"]));}
    public static object Restore() {
      return Apply(OriginalSpeed());
    }
    public static object VerifyLiveApi() {
      TouchpadSpeedParameters before=Read(),changed=new TouchpadSpeedParameters(),restored;
      int original=PointerSpeedPolicy.Clamp((int)before.cursorSpeed),probe=original==20?19:original+1;
      try {
        Apply(probe);changed=Read();
        if(!SameOtherSettings(before,changed))throw new InvalidOperationException("Changing speed affected another touchpad setting");
      }finally{Apply(original);}
      restored=Read();
      if(restored.cursorSpeed!=before.cursorSpeed || !SameOtherSettings(before,restored))throw new InvalidOperationException("Original touchpad speed was not restored");
      return new {success=true,capturedAt=DateTime.Now.ToString("o"),originalSpeed=original,testedSpeed=probe,restoredSpeed=restored.cursorSpeed,otherSettingsPreserved=true,mouseEventsInjected=false};
    }
    public static object VerifyLiveUndo(){
      TouchpadSpeedParameters before=Read();int original=(int)before.cursorSpeed,probe=original==1?2:original-1;var coordinator=new SpeedCoordinator(original,Change,delegate(int value){});
      try{coordinator.Set(probe);if(!coordinator.CanUndo || coordinator.UndoTarget!=original)throw new InvalidOperationException("Undo target mismatch");if(!coordinator.Undo() || GetCursorSpeed()!=original || coordinator.CanUndo)throw new InvalidOperationException("Live undo did not restore the original speed");}
      finally{Change(original);}
      if(!SameOtherSettings(before,Read()))throw new InvalidOperationException("Undo changed another touchpad setting");
      return new{success=true,capturedAt=DateTime.Now.ToString("o"),originalSpeed=original,testedSpeed=probe,restoredSpeed=GetCursorSpeed(),otherSettingsPreserved=true,mouseEventsInjected=false};
    }
    static bool SameOtherSettings(TouchpadSpeedParameters a,TouchpadSpeedParameters b) {
      return a.userFlags==b.userFlags && a.sensitivity==b.sensitivity && a.feedbackIntensity==b.feedbackIntensity &&
        a.clickForceSensitivity==b.clickForceSensitivity && a.rightClickWidth==b.rightClickWidth && a.rightClickHeight==b.rightClickHeight;
    }
    [DllImport("user32.dll",EntryPoint="SystemParametersInfoW",SetLastError=true)]
    static extern bool SystemParametersInfo(uint action,uint size,ref TouchpadSpeedParameters data,uint flags);
    [DllImport("user32.dll",EntryPoint="SendNotifyMessageW",SetLastError=true)]
    static extern bool SendNotifyMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
  }
}

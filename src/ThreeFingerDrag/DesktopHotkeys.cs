using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenTrackpadShared;

namespace ThreeFingerDrag {
  struct ShortcutKey {
    public readonly ushort Key;
    public readonly bool Up;
    public ShortcutKey(ushort key,bool up){Key=key;Up=up;}
  }

  static class DesktopKeyPlan {
    public const ushort Left=0x25,Right=0x27,Control=0x11,Windows=0x5B;
    public static ShortcutKey[] Build(ushort arrow,bool controlHeld,bool windowsHeld) {
      if(arrow!=Left && arrow!=Right)throw new ArgumentException("Expected a left or right arrow");
      var result=new List<ShortcutKey>();
      if(!controlHeld)result.Add(new ShortcutKey(Control,false));
      if(!windowsHeld)result.Add(new ShortcutKey(Windows,false));
      result.Add(new ShortcutKey(arrow,false));result.Add(new ShortcutKey(arrow,true));
      if(!windowsHeld)result.Add(new ShortcutKey(Windows,true));
      if(!controlHeld)result.Add(new ShortcutKey(Control,true));
      return result.ToArray();
    }

    public static ShortcutKey[] Cleanup(ShortcutKey[] plan,int accepted) {
      var held=new List<ushort>();
      for(int i=0;i<Math.Min(plan.Length,Math.Max(0,accepted));i++) {
        if(plan[i].Up)held.Remove(plan[i].Key);
        else if(!held.Contains(plan[i].Key))held.Add(plan[i].Key);
      }
      held.Reverse();
      var result=new List<ShortcutKey>();
      foreach(ushort key in held)result.Add(new ShortcutKey(key,true));
      return result.ToArray();
    }
  }

  interface IShortcutKeys {
    bool IsDown(int key);
    int Send(ShortcutKey[] keys);
  }

  sealed class SystemShortcutKeys : IShortcutKeys {
    public bool IsDown(int key){return !AppPaths.IsIsolated && GetAsyncKeyState(key)<0;}
    public int Send(ShortcutKey[] keys) {
      if(AppPaths.IsIsolated)return keys.Length;
      if(keys.Length==0)return 0;
      var input=new KEYBOARD_INPUT[keys.Length];
      for(int i=0;i<keys.Length;i++) {
        input[i].type=1;input[i].key.vk=keys[i].Key;
        bool extended=keys[i].Key==DesktopKeyPlan.Left || keys[i].Key==DesktopKeyPlan.Right || keys[i].Key==DesktopKeyPlan.Windows;
        input[i].key.flags=(keys[i].Up?2u:0u)|(extended?1u:0u);
        input[i].key.extra=new IntPtr(0x4F545044);
      }
      return (int)SendInput((uint)input.Length,input,Marshal.SizeOf(typeof(KEYBOARD_INPUT)));
    }
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,[In]KEYBOARD_INPUT[] input,int size);
  }

  [StructLayout(LayoutKind.Sequential)]
  struct KEYBOARD_KEY {public ushort vk,scan;public uint flags,time;public IntPtr extra;}
  [StructLayout(LayoutKind.Explicit,Size=40)]
  struct KEYBOARD_INPUT {
    [FieldOffset(0)]public uint type;
    [FieldOffset(8)]public KEYBOARD_KEY key;
  }

  sealed class DesktopShortcutEngine {
    readonly IShortcutKeys keys;
    ShortcutKey[] pending=new ShortcutKey[0];
    public bool CanShutdown {get{return pending.Length==0;}}
    public int SuccessfulRequests {get;private set;}
    public DesktopShortcutEngine(IShortcutKeys keys){this.keys=keys;}
    public bool Switch(ushort arrow) {
      RetryRelease();if(!CanShutdown)return false;
      // Shift/Alt variants remain available to applications and are never remapped.
      if(keys.IsDown(0x10) || keys.IsDown(0x12))return false;
      ShortcutKey[] plan=DesktopKeyPlan.Build(arrow,keys.IsDown(0x11),keys.IsDown(0x5B)||keys.IsDown(0x5C));
      int accepted=keys.Send(plan);
      if(accepted==plan.Length){SuccessfulRequests++;return true;}
      pending=DesktopKeyPlan.Cleanup(plan,accepted);
      RetryRelease();return false;
    }
    public void RetryRelease() {
      if(pending.Length==0)return;
      int accepted=Math.Min(pending.Length,Math.Max(0,keys.Send(pending)));
      if(accepted==0)return;
      var remaining=new ShortcutKey[pending.Length-accepted];
      Array.Copy(pending,accepted,remaining,0,remaining.Length);pending=remaining;
    }
  }

  sealed class DesktopHotkeys : IDisposable {
    public const int Message=0x0312,LeftId=0x4F11,RightId=0x4F12;
    readonly IntPtr window;
    readonly Logger logger;
    readonly Action<string> status;
    readonly DesktopShortcutEngine engine=new DesktopShortcutEngine(new SystemShortcutKeys());
    readonly Timer releaseTimer=new Timer();
    public bool Enabled {get;private set;}
    public string LastError {get;private set;}
    public bool CanShutdown {get{return engine.CanShutdown;}}
    public int Requests {get{return engine.SuccessfulRequests;}}
    public DesktopHotkeys(IntPtr window,Logger logger,Action<string> status) {
      this.window=window;this.logger=logger;this.status=status;
      releaseTimer.Interval=80;
      releaseTimer.Tick+=delegate{engine.RetryRelease();if(engine.CanShutdown)releaseTimer.Stop();};
    }
    public bool SetEnabled(bool enabled) {
      if(AppPaths.IsIsolated){Enabled=false;LastError=string.Empty;return true;}
      UnregisterHotKey(window,LeftId);UnregisterHotKey(window,RightId);Enabled=false;LastError=string.Empty;
      RetryRelease();
      if(!enabled)return true;
      if(!engine.CanShutdown){LastError="正在释放上次快捷键，请稍后重试。";return false;}
      if(!RegisterHotKey(window,LeftId,0x4002,DesktopKeyPlan.Left) || !RegisterHotKey(window,RightId,0x4002,DesktopKeyPlan.Right)) {
        int error=Marshal.GetLastWin32Error();
        UnregisterHotKey(window,LeftId);UnregisterHotKey(window,RightId);
        LastError="Ctrl＋左右键注册失败（错误 "+error+"），可能被其他程序占用。";
        logger.Error(LastError);status(LastError);return false;
      }
      Enabled=true;logger.Info("Ctrl+Left/Right desktop hotkeys registered");return true;
    }
    public void Handle(int id) {
      if(!Enabled || (id!=LeftId && id!=RightId))return;
      bool success=engine.Switch(id==LeftId?DesktopKeyPlan.Left:DesktopKeyPlan.Right);
      if(!engine.CanShutdown)releaseTimer.Start();
      if(success){LastError=string.Empty;status(id==LeftId?"已发送切换到左侧桌面的快捷键。":"已发送切换到右侧桌面的快捷键。");}
      else {LastError="桌面切换快捷键未能发送；如当前窗口以管理员身份运行，可从托盘以管理员身份重启组件。";logger.Error(LastError);status(LastError);}
    }
    public void RetryRelease(){engine.RetryRelease();if(!engine.CanShutdown)releaseTimer.Start();}
    public void Dispose(){SetEnabled(false);releaseTimer.Stop();releaseTimer.Dispose();}
    [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll",SetLastError=true)]static extern bool UnregisterHotKey(IntPtr window,int id);
  }
}

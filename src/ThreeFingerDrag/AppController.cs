using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using OpenTrackpadShared;
using OpenTrackpadUi;

namespace ThreeFingerDrag {
  sealed class AppController : ApplicationContext, IDisposable {
    readonly Logger logger;
    readonly DragController drag;
    readonly SpeedCoordinator speedCoordinator;
    readonly InputWindow input;
    readonly DesktopHotkeys desktopHotkeys;
    readonly NotifyIcon tray;
    SettingsForm form;
    Rectangle lastBounds;
    bool hasBounds;
    bool exiting;
    bool exitRequested;
    bool restartAfterRelease;
    System.Windows.Forms.Timer exitTimer;
    readonly System.Windows.Forms.Timer configurationTimer=new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer speedTimer=new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer runtimeTimer=new System.Windows.Forms.Timer();
    readonly RuntimeGate runtime=new RuntimeGate();
    readonly RuntimeRetry runtimeRetry=new RuntimeRetry();
    readonly int uiThread=Thread.CurrentThread.ManagedThreadId;
    bool resumeCleanupPending;
    bool enabled = true;
    bool diagnostic;
    bool configurationReady;
    bool desktopHotkeysRequested;
    bool optionsWritable=true,optionsRecovered;
    string settingsWarning;
    Dictionary<string,object> savedOptions=new Dictionary<string,object>();
    DateTime lastStatusWrite=DateTime.MinValue;
    int unifiedSpeed=10;
    string currentStatus = "Starting...";
    EventWaitHandle showSignal;
    RegisteredWaitHandle showWait;
    EventWaitHandle stopSignal;
    RegisteredWaitHandle stopWait;
    DateTime lastAdminStatus=DateTime.MinValue;
    ToolStripMenuItem speedMenu;
    readonly string placementPath=AppPaths.StateFile("window-placement.json");
    readonly string optionsPath=AppPaths.StateFile("gesture-options.json");
    bool CanRun {get{return !runtime.Paused && !resumeCleanupPending && !exitRequested;}}

    public AppController() {
      logger = new Logger();
      AppPaths.MigrateLegacyState();
      LoadOptions();
      try {TouchpadSpeed.EnsureBackup();}catch(Exception error){logger.Error(error.Message);settingsWarning="原速度备份无法读取，已保留该文件。";}
      try {unifiedSpeed=TouchpadSpeed.GetCursorSpeed();}
      catch(Exception error){logger.Error(error.Message);settingsWarning="无法读取触摸板速度，请检查设备连接。";}
      drag = new DragController(logger, RequestAdminRestartPrompt);
      drag.Scale=PointerSpeedPolicy.DragGain(unifiedSpeed);
      speedCoordinator=new SpeedCoordinator(unifiedSpeed,TouchpadSpeed.Change,AdoptUnifiedSpeed);
      input = new InputWindow(logger, drag, OnCoreStatus);
      bool locked,connected;runtime.Snapshot(Native.ReadSession(out locked,out connected),locked,connected);
      configurationReady=CanRun && GesturePreferences.IsSessionReady();
      input.Enabled=false;
      input.ShowSettingsRequested=ShowSettings;
      input.ShutdownRequested=ExitApplication;
      input.FailurePauseRequested=ApplyFailurePause;
      desktopHotkeys=new DesktopHotkeys(input.Handle,logger,OnCoreStatus);
      input.HotkeyReceived=desktopHotkeys.Handle;
      input.RuntimeEventReceived=OnRuntimeEvent;
      desktopHotkeys.SetEnabled(desktopHotkeysRequested && CanRun);
      showSignal=new EventWaitHandle(false,EventResetMode.AutoReset,AppPaths.InstanceName("ThreeFingerDrag.Show"));
      showWait=ThreadPool.RegisterWaitForSingleObject(showSignal,delegate {Native.PostMessage(input.Handle,Native.WM_SHOW_SETTINGS,IntPtr.Zero,IntPtr.Zero);},null,Timeout.Infinite,false);
      stopSignal=new EventWaitHandle(false,EventResetMode.AutoReset,AppPaths.InstanceName("ThreeFingerDrag.Stop"));
      stopWait=ThreadPool.RegisterWaitForSingleObject(stopSignal,delegate {Native.PostMessage(input.Handle,Native.WM_EXIT_DRAG,IntPtr.Zero,IntPtr.Zero);},null,Timeout.Infinite,false);
      SystemEvents.SessionSwitch+=OnSessionSwitch;
      SystemEvents.PowerModeChanged+=OnPowerModeChanged;

      tray = new NotifyIcon();
      tray.Icon = LoadAppIcon();
      tray.Text = "Open Trackpad · 速度与手势";
      ContextMenuStrip menu = new ContextMenuStrip();
      menu.Items.Add("速度与手势设置", null, delegate { ShowSettings(); });
      speedMenu=new ToolStripMenuItem("统一速度 · "+unifiedSpeed+" / 20");
      speedMenu.DropDownItems.Add("精细 · 6",null,delegate{SetUnifiedSpeed(6);});
      speedMenu.DropDownItems.Add("均衡 · 10",null,delegate{SetUnifiedSpeed(10);});
      speedMenu.DropDownItems.Add("快速 · 17",null,delegate{SetUnifiedSpeed(17);});
      speedMenu.DropDownItems.Add(new ToolStripSeparator());
      var undoMenu=new ToolStripMenuItem("撤销调速");undoMenu.Click+=delegate{UndoSpeed();};speedMenu.DropDownItems.Add(undoMenu);
      speedMenu.DropDownItems.Add("恢复初始速度",null,delegate{RestoreSpeed();});menu.Items.Add(speedMenu);
      speedMenu.DropDownOpening+=delegate{int[] values={6,10,17};for(int i=0;i<values.Length;i++)((ToolStripMenuItem)speedMenu.DropDownItems[i]).Checked=unifiedSpeed==values[i];};
      var pauseMenu=new ToolStripMenuItem();pauseMenu.Click+=delegate{SetEnabled(!enabled);};menu.Items.Add(pauseMenu);
      var desktopMenu=new ToolStripMenuItem("Ctrl＋左右键切换桌面");desktopMenu.Click+=delegate{SetDesktopHotkeys(!desktopHotkeysRequested);};menu.Items.Add(desktopMenu);
      menu.Items.Add("诊断与维护",null,delegate{ShowSettings();form.ShowDiagnostics();});
      menu.Opening+=delegate{pauseMenu.Text=enabled?"暂停三指拖移":"启用三指拖移";desktopMenu.Checked=desktopHotkeysRequested;undoMenu.Enabled=speedCoordinator.CanUndo;undoMenu.Text=speedCoordinator.CanUndo?"撤销调速 · 回到 "+speedCoordinator.UndoTarget+" / 20":"撤销调速";};
      menu.Items.Add("恢复 Windows 原生手势", null, delegate { RestoreNativeGestures(); });
      var administratorMenu=new ToolStripMenuItem("以管理员身份重启");administratorMenu.Enabled=!AppPaths.IsIsolated;administratorMenu.Click+=delegate {RestartAsAdministrator();};menu.Items.Add(administratorMenu);
      menu.Items.Add("退出", null, delegate { ExitApplication(); });
      Palette.StyleMenu(menu);
      tray.ContextMenuStrip = menu;
      tray.DoubleClick += delegate { ShowSettings(); };
      tray.Visible = true;

      if(CanRun && enabled && configurationReady)input.Register();
      configurationTimer.Interval=2000;
      configurationTimer.Tick+=delegate {
        if(exiting || !CanRun || !enabled){configurationTimer.Stop();return;}
        if(!configurationReady)configurationReady=GesturePreferences.IsSessionReady();
        if(!configurationReady)return;
        bool active=ActivateInput();
        if(active)OnCoreStatus("三指拖移已启用；四指手势遵循 Windows 设置。");
      };
      UpdateRegistrationRetry();
      speedTimer.Interval=2000;
      speedTimer.Tick+=delegate {
        try {
          int current=TouchpadSpeed.GetCursorSpeed();
          if(current==unifiedSpeed)return;
          speedCoordinator.Synchronize(current);
          OnCoreStatus("统一速度已同步为 "+unifiedSpeed+" / 20（内置与苹果触摸板共用）。");
        }catch(Exception exception){logger.Diagnostic("Speed sync: "+exception.Message);}
      };
      runtimeTimer.Interval=5000;runtimeTimer.Tick+=delegate{OnRuntimeEvent((int)RuntimeEvent.Refresh);};
      if(CanRun)speedTimer.Start();
      ScheduleRuntimeRetry();
      if(!configurationReady && enabled)OnCoreStatus("等待系统应用手势设置：请在 Windows 触摸板设置中将三指轻扫和点击均设为“无”；生效后会自动启用。");
      else OnCoreStatus(!string.IsNullOrEmpty(input.LastRegistrationError)?input.LastRegistrationError:desktopHotkeysRequested && !desktopHotkeys.Enabled?desktopHotkeys.LastError:DragActive?"三指拖移已启用；四指手势遵循 Windows 设置。":"三指拖移已暂停，保留你的原有选择。");
      if(runtime.Paused)OnCoreStatus("会话已暂停；解锁或恢复连接后继续。");
      if(AppPaths.IsIsolated)OnCoreStatus("预览模式：设置仅保存在独立目录，不控制触摸板或系统快捷键。");
      if(settingsWarning!=null)OnCoreStatus(settingsWarning);
      logger.Info("ThreeFingerDrag optimized core started");
      if (!Program.Background) Application.Idle += ShowOnFirstIdle;
    }

    public bool EnabledValue { get { return enabled; } }
    public bool DragActive {get{return input!=null && input.Enabled && input.Listening;}}
    public bool NeedsConfiguration {get{return !configurationReady;}}
    public bool DiagnosticValue { get { return diagnostic; } }
    public int UnifiedSpeedValue { get { return unifiedSpeed; } }
    public bool CanUndoSpeed {get{return speedCoordinator.CanUndo;}}
    public int UndoSpeedTarget {get{return speedCoordinator.UndoTarget;}}
    public string DesktopHotkeyWarning {get{return desktopHotkeysRequested && (!desktopHotkeys.Enabled || !string.IsNullOrEmpty(desktopHotkeys.LastError))?(string.IsNullOrEmpty(desktopHotkeys.LastError)?"桌面快捷键未启用，请到诊断与维护查看。":desktopHotkeys.LastError):null;}}
    public string CurrentStatus { get { return currentStatus; } }
    public bool DesktopHotkeysEnabled {get{return desktopHotkeys.Enabled;}}
    public string InputWarning {get{return enabled && CanRun && configurationReady && !DragActive && !AppPaths.IsIsolated?input.LastRegistrationError:null;}}
    public bool DesktopHotkeysRequested {get{return desktopHotkeysRequested;}}
    public bool IsPreview {get{return AppPaths.IsIsolated;}}
    public bool RuntimePaused {get{return runtime.Paused || resumeCleanupPending;}}
    bool CanShutdown {get{return drag.CanShutdown && desktopHotkeys.CanShutdown;}}

    void ShowOnFirstIdle(object sender, EventArgs e) {
      Application.Idle -= ShowOnFirstIdle;
      ShowSettings();
    }

    public void ShowSettings() {
      if (form == null || form.IsDisposed) {
        if(!hasBounds)LoadPlacement();
        form = new SettingsForm(this, logger);
        form.FormClosed += delegate { form = null; };
        if (hasBounds && Screen.FromRectangle(lastBounds).WorkingArea.IntersectsWith(lastBounds)) {
          form.StartPosition = FormStartPosition.Manual;
          Rectangle area=Screen.FromRectangle(lastBounds).WorkingArea;
          form.MinimumSize=new Size(Math.Min(form.MinimumSize.Width,area.Width),Math.Min(form.MinimumSize.Height,area.Height));
          int width=Math.Min(area.Width,lastBounds.Width),height=Math.Min(area.Height,lastBounds.Height);
          form.Bounds=new Rectangle(Math.Max(area.Left,Math.Min(lastBounds.Left,area.Right-width)),Math.Max(area.Top,Math.Min(lastBounds.Top,area.Bottom-height)),width,height);
        }
      }
      form.Show();
      // A hidden background launch must not suppress the user's first settings window.
      Native.ShowWindow(form.Handle,5);
      form.ShowInTaskbar = true;
      if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
      form.Activate();
      form.UpdateStatus(currentStatus);
    }

    public void HideSettings() {
      if (form == null || form.IsDisposed) return;
      if (form.WindowState == FormWindowState.Normal) {
        lastBounds = form.Bounds;
        hasBounds = true;
        SavePlacement();
      }
      form.RequestDispose();
    }

    public void Render(string path) {
      ShowSettings();Application.DoEvents();
      using(Bitmap image=new Bitmap(form.Width,form.Height)) {
        form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));
        image.Save(path,System.Drawing.Imaging.ImageFormat.Png);
      }
      ExitApplication();
    }

    public void SetEnabled(bool value) {
      enabled = value;
      configurationReady=CanRun && GesturePreferences.IsSessionReady();
      ActivateInput();
      if(CanRun)speedTimer.Start();
      if (!value) input.Cancel("paused");
      string text = value
        ? (configurationReady?"三指拖移已启用；四指手势遵循 Windows 设置。":"等待系统应用手势设置；三指轻扫和点击设为“无”后会自动启用。")
        : "三指拖移已暂停。可点击“恢复原生手势”恢复 Windows 三指操作。";
      if(AppPaths.IsIsolated)text="预览模式：已记录本次选择，不控制系统输入。";
      else if(runtime.Paused)text="设置已保留，恢复会话后按你的选择继续。";
      else if(value && configurationReady && !DragActive && !string.IsNullOrEmpty(input.LastRegistrationError))text=input.LastRegistrationError;
      OnCoreStatus(text);
      logger.Info(text);
      if (form != null && !form.IsDisposed) form.SyncEnabled(value);
      SaveOptions();
    }

    bool ActivateInput() {
      bool active=false;
      if(CanRun && enabled && configurationReady)active=input.Register();else input.Suspend();
      UpdateRegistrationRetry();return active;
    }
    void UpdateRegistrationRetry() {
      configurationTimer.Interval=configurationReady?5000:2000;
      configurationTimer.Enabled=CanRun && enabled && !AppPaths.IsIsolated && (!configurationReady || !DragActive);
    }

    void AdoptUnifiedSpeed(int value) {
      bool changed=unifiedSpeed!=PointerSpeedPolicy.Clamp(value);
      unifiedSpeed=PointerSpeedPolicy.Clamp(value);
      drag.Scale=PointerSpeedPolicy.DragGain(unifiedSpeed);
      if(speedMenu!=null)speedMenu.Text="统一速度 · "+unifiedSpeed+" / 20";
      if(form!=null && !form.IsDisposed)form.SyncSpeed(unifiedSpeed);
      if(changed)SaveOptions();
    }

    public bool SetUnifiedSpeed(int value) {
      try {
        SpeedChange result=speedCoordinator.Set(value);
        OnCoreStatus(result.Changed?"统一速度："+unifiedSpeed+" / 20，已应用，可撤销。":"统一速度：当前已是 "+unifiedSpeed+" / 20。");return true;
      }catch(Exception exception) {
        logger.Error(exception.Message);OnCoreStatus("速度设置失败："+exception.Message);
        if(form!=null && !form.IsDisposed)form.SyncSpeed(unifiedSpeed);
        return false;
      }
    }

    public bool UndoSpeed(){try{if(!speedCoordinator.Undo())return false;OnCoreStatus("已撤销调速，回到 "+unifiedSpeed+" / 20。");return true;}catch(Exception error){logger.Error(error.Message);OnCoreStatus("撤销调速失败："+error.Message);return false;}}

    public void RestoreSpeed() {
      try {
        if(SetUnifiedSpeed(TouchpadSpeed.OriginalSpeed()))OnCoreStatus("已恢复初始速度："+unifiedSpeed+" / 20，可用“撤销调速”返回刚才的档位。");
      }catch(Exception exception){logger.Error(exception.Message);OnCoreStatus("恢复速度失败："+exception.Message);}
    }

    public void SetDesktopHotkeys(bool value) {
      desktopHotkeysRequested=value;
      bool success=desktopHotkeys.SetEnabled(value && CanRun);
      OnCoreStatus(!success?desktopHotkeys.LastError:value?"Ctrl＋左右键切换桌面已启用。":"桌面切换快捷键已关闭，Ctrl＋左右键恢复应用中的原有操作。");
      if(AppPaths.IsIsolated)OnCoreStatus("预览模式：不会注册全局桌面快捷键。");
      if(form!=null && !form.IsDisposed)form.SyncDesktopHotkeys(desktopHotkeysRequested);
      SaveOptions();
    }

    public void SetDiagnostic(bool value) {
      diagnostic = value;
      logger.Verbose = value;
      logger.Info(value ? "Diagnostic logging enabled" : "Diagnostic logging disabled");
    }

    public void OpenLogs() {OpenLogs(logger.OpenDirectory);}
    internal void OpenLogs(Action open) {try{open();}catch(Exception error){logger.Error(error.ToString());OnCoreStatus("日志目录暂时无法打开，请稍后重试。仍可复制诊断信息。");}}
    public string CopyableLog() { return logger.Snapshot(); }

    void OnCoreStatus(string text) {
      currentStatus = text;
      if(tray!=null){string tip="Open Trackpad · "+unifiedSpeed+"/20 · "+(DragActive?"三指拖移已启用":"三指拖移已暂停");if(tray.Text!=tip)tray.Text=tip;}
      if (form != null && !form.IsDisposed && form.Visible) form.UpdateStatus(text);
      try {
        if(text!=null && text.IndexOf("触点：",StringComparison.Ordinal)>=0 && (DateTime.UtcNow-lastStatusWrite).TotalSeconds<15)return;
        lastStatusWrite=DateTime.UtcNow;
        string path=AppPaths.StateFile("gesture-status.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path,new JavaScriptSerializer().Serialize(new {updatedAt=DateTime.Now.ToString("o"),enabled=DragActive,requestedEnabled=enabled,requiresSignIn=!configurationReady,readiness=GesturePreferences.LastReadinessDetail,inputError=input==null?null:input.LastRegistrationError,unifiedSpeed=unifiedSpeed,canUndoSpeed=speedCoordinator!=null && speedCoordinator.CanUndo,undoSpeedTarget=speedCoordinator!=null && speedCoordinator.CanUndo?(int?)speedCoordinator.UndoTarget:null,dragGain=drag==null?0:drag.Scale,physicalSpeedCalibration=true,status=text,dragStarts=drag==null?0:drag.DragStarts,dragReleases=drag==null?0:drag.DragReleases,desktopHotkeysEnabled=desktopHotkeys!=null && desktopHotkeys.Enabled,desktopHotkeyError=desktopHotkeys==null?null:desktopHotkeys.LastError,desktopSwitchRequests=desktopHotkeys==null?0:desktopHotkeys.Requests}));
      }catch{}
    }

    public void ConfigureGestureMode() {
      try { GesturePreferences.Apply(true);SetEnabled(true);OnCoreStatus(!string.IsNullOrEmpty(InputWarning)?InputWarning:GesturePreferences.LastRecoveryWarning.Length>0?GesturePreferences.LastRecoveryWarning:GesturePreferences.LastReloadPending?"等待系统应用手势设置。":"已配置三指拖移与四指手势；桌面快捷键保留你的选择。"); }
      catch(Exception exception){logger.Error(exception.Message);OnCoreStatus("配置失败："+exception.Message);}
    }

    public void RestoreNativeGestures() {RestoreNativeGestures(delegate{GesturePreferences.Apply(false);});}
    internal void RestoreNativeGestures(Action restore) {
      bool originalEnabled=enabled,originalHotkeys=desktopHotkeysRequested;
      // Pause only effective listeners. Persist the user's choices after the
      // Windows restoration succeeds, never before a fallible preflight.
      try {
        input.Suspend();desktopHotkeys.SetEnabled(false);configurationTimer.Stop();
        restore();enabled=false;desktopHotkeysRequested=false;
        input.Suspend();desktopHotkeys.SetEnabled(false);SaveOptions();
        if(form!=null && !form.IsDisposed){form.SyncEnabled(false);form.SyncDesktopHotkeys(false);}
        OnCoreStatus(GesturePreferences.LastRecoveryWarning.Length>0?GesturePreferences.LastRecoveryWarning:GesturePreferences.LastReloadPending?"原生手势已保存，Windows 重新登录后生效；开机启动已关闭。":"已恢复安装前的 Windows 手势设置，并关闭开机启动。");
      }catch(Exception exception) {
        enabled=originalEnabled;desktopHotkeysRequested=originalHotkeys;
        configurationReady=CanRun && GesturePreferences.IsSessionReady();ActivateInput();desktopHotkeys.SetEnabled(originalHotkeys && CanRun);
        logger.Error(exception.ToString());
        string reason=FriendlyRestoreReason(exception);
        OnCoreStatus("恢复未完成，原来的手势与快捷键选择已保留。"+reason);
      }
    }
    internal static string FriendlyRestoreReason(Exception error) {
      string message=error.Message;
      if(error is InvalidDataException && message.Length<=180)foreach(char value in message)if(value>='\u4e00' && value<='\u9fff')return message;
      return "可在诊断与维护中查看原因，再重试或到 Windows 设置手动恢复。";
    }

    public void SetAutostart(bool value){try{GesturePreferences.SetAutostart(value);OnCoreStatus(AppPaths.IsIsolated?"预览模式：不会修改登录启动项。":value?"已启用登录启动。":"已关闭登录启动。");}catch(Exception error){logger.Error(error.Message);OnCoreStatus("登录启动设置未能保存，请稍后重试。");}}

    void LoadOptions() {
      try {
        JsonRead loaded=AtomicJson.Read(optionsPath,delegate(object value){var candidate=value as Dictionary<string,object>;return candidate!=null && (!candidate.ContainsKey("enabled") || candidate["enabled"] is bool) && (!candidate.ContainsKey("desktopHotkeys") || candidate["desktopHotkeys"] is bool);});
        if(loaded.Unreadable){optionsWritable=false;enabled=false;settingsWarning="设置文件无法读取，已保留原文件；本次暂停输入功能。";return;}
        if(loaded.Value==null)return;
        var data=(Dictionary<string,object>)loaded.Value;savedOptions=data;optionsRecovered=loaded.Recovered;
        if(optionsRecovered)settingsWarning="已读取本机恢复副本；保存设置时会保留损坏的原文件。";
        if(data.ContainsKey("enabled"))enabled=Convert.ToBoolean(data["enabled"]);
        if(data.ContainsKey("desktopHotkeys"))desktopHotkeysRequested=Convert.ToBoolean(data["desktopHotkeys"]);
      }catch{}
    }

    void LoadPlacement(){try{var data=AtomicJson.Read(placementPath,null).Value as Dictionary<string,object>;if(data==null)return;lastBounds=new Rectangle(Convert.ToInt32(data["x"]),Convert.ToInt32(data["y"]),Convert.ToInt32(data["width"]),Convert.ToInt32(data["height"]));hasBounds=lastBounds.Width>=400 && lastBounds.Height>=350 && lastBounds.Width<=20000 && lastBounds.Height<=20000 && Math.Abs((long)lastBounds.X)<1000000 && Math.Abs((long)lastBounds.Y)<1000000;}catch{hasBounds=false;}}
    void SavePlacement(){try{AtomicJson.Write(placementPath,new{x=lastBounds.X,y=lastBounds.Y,width=lastBounds.Width,height=lastBounds.Height});}catch{}}

    void SaveOptions() {
      try {
        if(!optionsWritable){OnCoreStatus("本次设置未保存：原设置文件已保留，请先从恢复副本修复。");return;}
        var next=new Dictionary<string,object>(savedOptions);next["enabled"]=enabled;next["unifiedSpeed"]=unifiedSpeed;next["desktopHotkeys"]=desktopHotkeysRequested;
        if(optionsRecovered)AtomicJson.PreserveDamaged(optionsPath);
        AtomicJson.Write(optionsPath,next);savedOptions=next;optionsRecovered=false;
      }catch(Exception error){logger.Error(error.Message);OnCoreStatus("本次设置未能保存，请检查数据目录的写入权限。");}
    }

    void OnSessionSwitch(object sender,SessionSwitchEventArgs args) {
      RuntimeEvent value;
      switch(args.Reason) {
        case SessionSwitchReason.SessionLock:value=RuntimeEvent.Lock;break;
        case SessionSwitchReason.SessionUnlock:value=RuntimeEvent.Unlock;break;
        case SessionSwitchReason.RemoteDisconnect:case SessionSwitchReason.ConsoleDisconnect:value=RuntimeEvent.Disconnect;break;
        case SessionSwitchReason.RemoteConnect:case SessionSwitchReason.ConsoleConnect:case SessionSwitchReason.SessionLogon:value=RuntimeEvent.Connect;break;
        case SessionSwitchReason.SessionLogoff:value=RuntimeEvent.Logoff;break;
        default:return;
      }
      Native.PostMessage(input.Handle,Native.WM_RUNTIME_EVENT,new IntPtr((int)value),IntPtr.Zero);
    }
    void OnPowerModeChanged(object sender,PowerModeChangedEventArgs args) {
      if(args.Mode==PowerModes.Suspend || args.Mode==PowerModes.Resume)Native.PostMessage(input.Handle,Native.WM_RUNTIME_EVENT,new IntPtr((int)(args.Mode==PowerModes.Suspend?RuntimeEvent.Suspend:RuntimeEvent.Resume)),IntPtr.Zero);
    }
    void OnRuntimeEvent(int code) {
      if(exiting || exitRequested)return;
      RuntimeEvent value=(RuntimeEvent)code;runtime.Apply(value);
      if(value!=RuntimeEvent.Refresh)runtimeRetry.Reset();
      if(value==RuntimeEvent.Unlock || value==RuntimeEvent.Connect || value==RuntimeEvent.Resume || value==RuntimeEvent.Refresh) {
        bool locked,connected;runtime.Snapshot(Native.ReadSession(out locked,out connected),locked,connected);
      }
      if(!runtime.Paused) {
        // A secure desktop can reject mouse-up/key-up. Retain ownership and
        // finish that cleanup before re-registering any input listener.
        input.Cancel("release before session resumes");desktopHotkeys.RetryRelease();
        resumeCleanupPending=!CanShutdown;
      }
      if(!CanRun) {
        input.Suspend();desktopHotkeys.SetEnabled(false);configurationTimer.Stop();speedTimer.Stop();
        ScheduleRuntimeRetry();
        OnCoreStatus(resumeCleanupPending?"正在释放上次输入，释放成功后恢复监听。":"会话已暂停；解锁或恢复连接后继续。");return;
      }
      configurationReady=GesturePreferences.IsSessionReady();
      ActivateInput();
      desktopHotkeys.SetEnabled(desktopHotkeysRequested);
      speedTimer.Start();runtimeTimer.Stop();
      OnCoreStatus(AppPaths.IsIsolated?"预览模式：不会更改系统输入。":enabled && !string.IsNullOrEmpty(input.LastRegistrationError)?input.LastRegistrationError:DragActive?"已恢复三指拖移；保留原来的速度与快捷键选择。":"已恢复会话；三指拖移保留你的暂停或待配置状态。");
    }
    internal void PauseAfterFailure(Exception error) {
      // Do not persist a temporary emergency pause over the user's preferences.
      enabled=false;desktopHotkeysRequested=false;input.Enabled=false;
      try{logger.Error(error.ToString());}catch{}
      if(Thread.CurrentThread.ManagedThreadId==uiThread)ApplyFailurePause();else input.PostFailurePause();
    }
    void ApplyFailurePause() {
      TryFailureCleanup(delegate{configurationTimer.Stop();speedTimer.Stop();runtimeTimer.Stop();});
      TryFailureCleanup(delegate{input.Suspend();});
      TryFailureCleanup(delegate{desktopHotkeys.SetEnabled(false);});
      TryFailureCleanup(delegate{if(form!=null && !form.IsDisposed){form.SyncEnabled(false);form.SyncDesktopHotkeys(false);}});
      TryFailureCleanup(delegate{OnCoreStatus("遇到异常，已暂停新的输入操作。你可以在设置中重新启用，或复制诊断信息。");});
    }
    void TryFailureCleanup(Action operation){try{operation();}catch(Exception error){try{logger.Error(error.ToString());}catch{}}}
    void ScheduleRuntimeRetry() {
      // Explicit lock/disconnect/suspend wait for the matching event. A stale
      // query after activation retries at 5,10,20,40,60 seconds, then once/minute.
      runtimeTimer.Stop();
      if(runtime.ExplicitlyBlocked || exitRequested || exiting || !runtime.Paused && !resumeCleanupPending)return;
      runtimeTimer.Interval=runtimeRetry.Next();runtimeTimer.Start();
    }

    public void ExitApplication() {
      if (exiting) return;
      exitRequested=true;
      configurationTimer.Stop();
      speedTimer.Stop();
      runtimeTimer.Stop();
      input.Enabled=false;
      desktopHotkeys.SetEnabled(false);
      input.Cancel("app closing");
      if(!CanShutdown) {
        OnCoreStatus("正在释放本组件按下的按键，释放成功后自动退出。");
        if(exitTimer==null) {
          exitTimer=new System.Windows.Forms.Timer();exitTimer.Interval=80;
          exitTimer.Tick+=delegate {if(CanShutdown)FinishExit();};
        }
        exitTimer.Start();
        return;
      }
      FinishExit();
    }

    void FinishExit() {
      if(exiting || !exitRequested || !CanShutdown)return;
      if(restartAfterRelease) {
        if(AppPaths.IsIsolated){restartAfterRelease=false;exitRequested=false;OnRuntimeEvent((int)RuntimeEvent.Refresh);OnCoreStatus("预览模式不请求管理员权限。请退出预览后按需打开正式应用。");return;}
        try {
          ProcessStartInfo start=new ProcessStartInfo(Application.ExecutablePath);
          start.UseShellExecute=true;start.Verb="runas";start.Arguments="--relaunch";
          start.WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory;
          Process.Start(start);
        }catch(Exception exception) {
          restartAfterRelease=false;exitRequested=false;
          if(exitTimer!=null)exitTimer.Stop();
          OnRuntimeEvent((int)RuntimeEvent.Refresh);
          OnCoreStatus("管理员重启未完成："+exception.Message);
          return;
        }
      }
      exiting = true;
      if(form!=null && !form.IsDisposed && form.WindowState==FormWindowState.Normal){lastBounds=form.Bounds;hasBounds=true;SavePlacement();}
      if(exitTimer!=null){exitTimer.Stop();exitTimer.Dispose();}
      if (form != null && !form.IsDisposed) form.RequestDispose();
      tray.Visible = false;
      ExitThread();
    }

    bool RequestAdminRestartPrompt() {
      if((DateTime.UtcNow-lastAdminStatus).TotalSeconds>=10) {
        lastAdminStatus=DateTime.UtcNow;
        OnCoreStatus("此窗口以管理员身份运行；三指拖移需要相同权限，普通窗口不受影响。");
      }
      return false;
    }

    void RestartAsAdministrator() {
      if(AppPaths.IsIsolated){OnCoreStatus("预览模式不请求管理员权限。请退出预览后按需打开正式应用。");return;}
      restartAfterRelease=true;
      ExitApplication();
    }

    protected override void ExitThreadCore() {
      SystemEvents.SessionSwitch-=OnSessionSwitch;
      SystemEvents.PowerModeChanged-=OnPowerModeChanged;
      configurationTimer.Dispose();
      speedTimer.Dispose();
      runtimeTimer.Dispose();
      desktopHotkeys.Dispose();
      if(showWait!=null)showWait.Unregister(null);
      if(showSignal!=null)showSignal.Dispose();
      if(stopWait!=null)stopWait.Unregister(null);
      if(stopSignal!=null)stopSignal.Dispose();
      input.Dispose();
      tray.Dispose();
      logger.Dispose();
      base.ExitThreadCore();
    }

    static Icon LoadAppIcon() {
      try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
      catch { return (Icon)SystemIcons.Application.Clone(); }
    }

    public new void Dispose() {
      if (!exiting) ExitApplication();
      base.Dispose();
    }
  }
}

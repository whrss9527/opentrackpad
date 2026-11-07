using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OpenTrackpadUi;
using OpenTrackpadShared;
namespace ThreeFingerDrag {
  sealed class SettingsForm:Form {
    readonly AppController controller;readonly Logger logger;
    readonly Panel content=new Panel(),overview=new Panel(),advanced=new Panel(),appearance=new Panel();
    readonly Label stateLabel=Palette.Label("",9,Palette.Accent),speedValue=Palette.Label("",28,Palette.Ink),detail=Palette.Label("",9,Palette.Muted),backgroundState=Palette.Label("",8,Color.FromArgb(155,177,199));
    readonly Toggle enabledBox=new Toggle(),desktopKeysBox=new Toggle(),startupBox=new Toggle(),diagnosticBox=new Toggle();
    readonly SpeedSlider speed=new SpeedSlider();readonly ActionButton home=new ActionButton(),diagnostics=new ActionButton(),appearanceNav=new ActionButton();readonly TextBox logBox=new TextBox();
    readonly ChoiceButton themeChoice=new ChoiceButton(),fontChoice=new ChoiceButton();
    readonly Label appearanceStatus=Palette.Label("",9,Palette.Muted);
    readonly ActionButton saveAppearance=new ActionButton(),cancelAppearance=new ActionButton();
    AppearanceSettings committedAppearance=UiPreferences.Current;bool changingAppearance,applyingAppearance,appliedHighContrast;
    Label speedHint,speedTitle;
    TableLayoutPanel overviewRows,advancedRows,appearanceRows,logLayout;
    FlowLayoutPanel maintenanceActions;
    Panel logHeading;
    Label logTitle,logHint,runtimeBadge;
    ActionButton copyLogButton,openLogButton;
    readonly Dictionary<Panel,int> pageHeights=new Dictionary<Panel,int>();
    bool reflowing;
    Card speedArea;ActionButton restoreButton,hideButton,undoButton;FlowLayoutPanel presetRow;float layoutScale;bool arranging;
    readonly Label speedCaption=Palette.Label("",8,Palette.Muted);
    readonly Timer draftTimer=new Timer(),feedbackTimer=new Timer();readonly SpeedDraft speedDraft;
    readonly ToolTip tips=new ToolTip();
    string latestStatus="",feedback="",lastStatusForFeedback="";bool feedbackError;DateTime feedbackUntil;
    readonly List<ActionButton> presetButtons=new List<ActionButton>();
    bool syncing,allowDispose;
    public SettingsForm(AppController controller,Logger logger):this(controller,logger,null){}
    internal SettingsForm(AppController controller,Logger logger,Action<int> applySpeed){
      this.controller=controller;this.logger=logger;speedDraft=new SpeedDraft(delegate(int value){if(applySpeed!=null)applySpeed(value);else if(controller!=null)controller.SetUnifiedSpeed(value);});Text="Open Trackpad · 触摸板设置";Font=Palette.Font(9.5f,FontStyle.Regular);BackColor=Palette.Canvas;
      SuspendLayout();AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(1000,760);MinimumSize=new Size(880,540);StartPosition=FormStartPosition.CenterScreen;DoubleBuffered=true;
      try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
      var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=Padding.Empty};root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,202));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(root);
      Panel sidebar=new Panel{Dock=DockStyle.Fill,BackColor=Palette.Nav,Margin=Padding.Empty};root.Controls.Add(sidebar,0,0);sidebar.Controls.Add(new BrandMark{Location=new Point(22,28)});
      Label brand=Palette.Label("Open Trackpad",11,Palette.NavInk);brand.Name="brandName";brand.Tag="nav-title";brand.Font=Palette.Font(11,FontStyle.Bold);brand.SetBounds(75,27,121,28);sidebar.Controls.Add(brand);
      Label tagline=Palette.Label("触摸板控制中心",7.8f,Palette.NavMuted);tagline.SetBounds(76,56,120,22);sidebar.Controls.Add(tagline);
      home.AccessibleName="速度与手势";home.TabIndex=0;home.Text="速度与手势";home.Navigation=true;home.NavigationIcon="speed";home.Selected=true;home.SetBounds(16,138,170,44);home.Click+=delegate{ShowPage(false);};sidebar.Controls.Add(home);
      diagnostics.AccessibleName="诊断与维护";diagnostics.TabIndex=1;diagnostics.Text="诊断与维护";diagnostics.Navigation=true;diagnostics.NavigationIcon="maintenance";diagnostics.SetBounds(16,190,170,44);diagnostics.Click+=delegate{ShowPage(true);};sidebar.Controls.Add(diagnostics);
      appearanceNav.AccessibleName="外观与体验";appearanceNav.TabIndex=2;appearanceNav.Text="外观与体验";appearanceNav.Navigation=true;appearanceNav.NavigationIcon="appearance";appearanceNav.SetBounds(16,242,170,44);appearanceNav.Click+=delegate{ShowAppearance();};sidebar.Controls.Add(appearanceNav);sidebar.Tag="nav";
      var sideFoot=new Panel{Dock=DockStyle.Bottom,Height=94,BackColor=Palette.Nav};sidebar.Controls.Add(sideFoot);backgroundState.SetBounds(26,6,162,25);sideFoot.Controls.Add(backgroundState);
      Label foot=Palette.Label("版本 "+Application.ProductVersion,7.5f,Palette.NavMuted);foot.SetBounds(26,38,166,24);sideFoot.Controls.Add(foot);
      content.Dock=DockStyle.Fill;content.Padding=new Padding(30,24,30,20);content.Margin=Padding.Empty;root.Controls.Add(content,1,0);overview.Dock=advanced.Dock=appearance.Dock=DockStyle.Fill;content.Controls.Add(appearance);content.Controls.Add(advanced);content.Controls.Add(overview);
      sidebar.TabIndex=0;content.TabIndex=1;overview.TabIndex=0;advanced.TabIndex=1;appearance.TabIndex=2;
      BuildOverview();BuildAdvanced();BuildAppearance();ShowPage(false);if(logger!=null){logBox.Text=logger.Snapshot()+Environment.NewLine;logger.LineAdded+=AppendLog;}else logBox.Text="界面预览 · 未启动触点监听或快捷键。\r\n实际运行时，诊断日志将在此显示。";
      tips.SetToolTip(home,"速度与手势（Ctrl+1）");tips.SetToolTip(diagnostics,"诊断与维护（Ctrl+2）");tips.SetToolTip(speed,"拖动时预览，松手后应用。Esc可取消未应用的改动。");tips.SetToolTip(undoButton,"回到最近一次调速之前的档位。");tips.SetToolTip(restoreButton,"恢复最初备份的速度；恢复后仍可撤销。");
      UpdateStatus(controller==null?"预览":controller.CurrentStatus);FormClosing+=OnFormClosing;FormClosed+=delegate{if(logger!=null)logger.LineAdded-=AppendLog;draftTimer.Dispose();feedbackTimer.Dispose();tips.Dispose();UiPreferences.Preview(committedAppearance);};
      draftTimer.Interval=40;draftTimer.Tick+=delegate{speedDraft.Tick(DateTime.UtcNow,speed.IsInteracting);if(!speedDraft.Pending){draftTimer.Stop();UpdateSpeedNumber();}};
      feedbackTimer.Interval=750;feedbackTimer.Tick+=delegate{if(Visible)RefreshFooter();};Shown+=delegate{FitToWorkArea();feedbackTimer.Start();};
      ResumeLayout(true);
      using(Graphics graphics=CreateGraphics()){float factor=graphics.DpiX/96f;if(Math.Abs(factor-1)>0.01)Scale(new SizeF(factor,factor));layoutScale=factor;}
      Rectangle available=Screen.FromControl(this).WorkingArea;MinimumSize=new Size(Math.Min(MinimumSize.Width,available.Width-24),Math.Min(MinimumSize.Height,available.Height-24));Size=new Size(Math.Min(Width,available.Width-24),Math.Min(Height,available.Height-24));
      ConfigureScrolling(overview,714);ConfigureScrolling(advanced,650);ConfigureScrolling(appearance,650);
      ApplyAppearance();AlignSpeedArea();Resize+=delegate{AlignSpeedArea();ReflowRows();};
    }
    TableLayoutPanel Page(Panel host,string title,string subtitle){
      var page=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));page.RowStyles.Add(new RowStyle(SizeType.Absolute,90));page.RowStyles.Add(new RowStyle(SizeType.Percent,100));host.Controls.Add(page);
      Panel heading=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,Name="pageHeading"};page.Controls.Add(heading,0,0);Label name=Palette.Label(title,23,Palette.Ink);name.Font=Palette.Font(23,FontStyle.Regular);name.Tag="headline";name.SetBounds(0,0,600,48);heading.Controls.Add(name);
      name.Name="pageTitle";name.Anchor=AnchorStyles.Top|AnchorStyles.Left;
      Label sub=Palette.Label(subtitle,9,Palette.Muted);sub.Name="pageDescription";sub.SetBounds(1,49,620,30);sub.Anchor=AnchorStyles.Top|AnchorStyles.Left;heading.Controls.Add(sub);
      heading.SizeChanged+=delegate{name.Width=Math.Max(1,heading.ClientSize.Width);sub.Width=Math.Max(1,heading.ClientSize.Width-sub.Left);};return page;
    }
    void BuildOverview(){
      var page=Page(overview,"速度与手势","内置触摸板与 Magic Trackpad，一套设置。");
      var rows=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Margin=Padding.Empty};rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int h in new[]{218,88,88,88,78})rows.RowStyles.Add(new RowStyle(SizeType.Absolute,h));rows.RowStyles.Add(new RowStyle(SizeType.Percent,100));page.Controls.Add(rows,0,1);overviewRows=rows;
      Card speedCard=new Card{Dock=DockStyle.Fill,Size=new Size(738,206),Emphasis=true};speedArea=speedCard;rows.Controls.Add(speedCard,0,0);
      Label title=Palette.Label("统一速度",13,Palette.Ink);title.Font=Palette.Font(13,FontStyle.Bold);title.SetBounds(82,18,220,30);speedTitle=title;speedCard.Controls.Add(title);speedCard.Controls.Add(new ModuleIcon{Kind="trackpad",Location=new Point(24,20),Size=new Size(42,42)});
      Label hint=Palette.Label("指针与三指拖移同步调节 · 支持方向键微调",9,Palette.Muted);hint.SetBounds(82,51,417,26);speedHint=hint;speedCard.Controls.Add(hint);
      speedValue.SetBounds(558,6,150,58);speedValue.Anchor=AnchorStyles.Top|AnchorStyles.Right;speedValue.TextAlign=ContentAlignment.MiddleRight;speedCard.Controls.Add(speedValue);
      speedCaption.SetBounds(558,65,150,18);speedCaption.TextAlign=ContentAlignment.MiddleRight;speedCard.Controls.Add(speedCaption);
      speed.SetBounds(20,85,690,44);speed.Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right;speed.Font=Font;speed.Value=controller==null?10:controller.UnifiedSpeedValue;speedCard.Controls.Add(speed);
      speed.ValueChanged+=delegate{if(!syncing){speedDraft.Schedule(speed.Value,DateTime.UtcNow);draftTimer.Start();}UpdateSpeedNumber();};speed.InteractionCompleted+=delegate{FlushSpeed();};UpdateSpeedNumber();
      var presets=new FlowLayoutPanel{Location=new Point(24,143),Size=new Size(540,38),BackColor=Palette.Surface,Tag="surface"};presetRow=presets;speedCard.Controls.Add(presets);
      AddPreset(presets,"精细  6",6);AddPreset(presets,"均衡  10",10);AddPreset(presets,"快速  17",17);
      undoButton=Button("撤销调速",104,delegate{FlushSpeed();if(controller!=null)controller.UndoSpeed();});undoButton.SetBounds(475,143,104,32);undoButton.AccessibleName="撤销最近调速";undoButton.Enabled=controller!=null && controller.CanUndoSpeed;speedCard.Controls.Add(undoButton);
      var restore=Button("恢复初始速度",115,delegate{FlushSpeed();if(controller!=null)controller.RestoreSpeed();});restoreButton=restore;restore.SetBounds(594,143,115,32);restore.Anchor=AnchorStyles.Top|AnchorStyles.Left;speedCard.Controls.Add(restore);
      Card drag=SettingRow("三指拖移","轻移三根手指，拖动窗口、文件或选中文字。",enabledBox);enabledBox.AccessibleName="启用三指拖移";enabledBox.Checked=controller==null || controller.EnabledValue;enabledBox.CheckedChanged+=delegate{if(!syncing && controller!=null)controller.SetEnabled(enabledBox.Checked);};rows.Controls.Add(drag,0,1);
      Card four=SettingRow("四指手势","使用 Windows 中设置的手势。",null);Label keys=Palette.Label("←   →",16,Palette.Accent);keys.TextAlign=ContentAlignment.MiddleRight;keys.SetBounds(575,16,126,38);keys.Anchor=AnchorStyles.Top|AnchorStyles.Right;four.Controls.Add(keys);var systemGestures=Button("Windows 设置",120,delegate{Process.Start(new ProcessStartInfo("ms-settings:devices-touchpad"){UseShellExecute=true});});systemGestures.Name="windowsGestureSettings";systemGestures.SetBounds(585,20,120,36);systemGestures.Anchor=AnchorStyles.Top|AnchorStyles.Right;four.Controls.Add(systemGestures);keys.Visible=false;rows.Controls.Add(four,0,2);
      Card desk=SettingRow("Ctrl + ← / → 切换桌面","启用后，会替代文本编辑中的按词移动快捷键。",desktopKeysBox);desktopKeysBox.AccessibleName="Ctrl左右键切换虚拟桌面";desktopKeysBox.Checked=controller!=null && controller.DesktopHotkeysRequested;desktopKeysBox.CheckedChanged+=delegate{if(!syncing && controller!=null)controller.SetDesktopHotkeys(desktopKeysBox.Checked);};rows.Controls.Add(desk,0,3);
      Card startup=SettingRow("登录后自动运行","关闭窗口后，仍在托盘保持运行。",startupBox);startupBox.AccessibleName="登录后自动运行";startupBox.Checked=controller!=null && GesturePreferences.HasAutostart();startupBox.CheckedChanged+=delegate{if(!syncing && controller!=null)controller.SetAutostart(startupBox.Checked);};rows.Controls.Add(startup,0,4);
      Panel footer=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,Size=new Size(738,55)};rows.Controls.Add(footer,0,5);stateLabel.SetBounds(0,2,570,28);stateLabel.AutoEllipsis=true;footer.Controls.Add(stateLabel);ActionButton hide=Button("收起到托盘",120,delegate{if(NeedsAttention){ShowPage(true);return;}FlushSpeed();if(controller!=null)controller.HideSettings();else Close();});hideButton=hide;hide.SetBounds(610,0,120,34);hide.Anchor=AnchorStyles.Top|AnchorStyles.Left;footer.Controls.Add(hide);
    }
    void AddPreset(FlowLayoutPanel row,string text,int value){var button=Button(text,112,delegate{speed.Value=value;FlushSpeed();});button.Height=32;button.Tag=value;button.Primary=speed.Value==value;presetButtons.Add(button);row.Controls.Add(button);}
    Card SettingRow(string title,string subtitle,Toggle toggle){
      Card card=new Card{Dock=DockStyle.Fill,Padding=Padding.Empty,Tag="setting-row"};string kind=title.StartsWith("三指")?"drag":title.StartsWith("四指")?"windows":title.StartsWith("Ctrl")?"desktops":title=="详细日志"?"maintenance":"startup";card.Controls.Add(new ModuleIcon{Kind=kind,Location=new Point(24,19)});Label name=Palette.Label(title,11,Palette.Ink);name.Font=Palette.Font(11,FontStyle.Bold);name.Name="settingTitle";name.SetBounds(82,12,492,28);name.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;card.Controls.Add(name);
      Label sub=Palette.Label(subtitle,8.5f,Palette.Muted);sub.Name="settingDescription";sub.SetBounds(82,41,522,23);sub.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;card.Controls.Add(sub);if(toggle!=null){toggle.SetBounds(658,21,48,30);toggle.Anchor=AnchorStyles.Top|AnchorStyles.Right;card.Controls.Add(toggle);}return card;
    }
    void BuildAdvanced(){
      var page=Page(advanced,"诊断与维护","查看运行状态，按需排查问题与管理手势。");
      var rows=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty};
      rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
      rows.RowStyles.Add(new RowStyle(SizeType.Absolute,108));rows.RowStyles.Add(new RowStyle(SizeType.Percent,100));
      rows.RowStyles.Add(new RowStyle(SizeType.Absolute,88));rows.RowStyles.Add(new RowStyle(SizeType.Absolute,65));
      page.Controls.Add(rows,0,1);advancedRows=rows;

      Card status=new Card{Dock=DockStyle.Fill,Padding=Padding.Empty,Name="diagnosticStatus"};rows.Controls.Add(status,0,0);
      status.Controls.Add(new ModuleIcon{Kind="trackpad",Location=new Point(24,24)});
      Label title=Palette.Label("运行状态",11,Palette.Ink);title.Font=Palette.Font(11,FontStyle.Bold);title.SetBounds(82,16,200,28);status.Controls.Add(title);
      runtimeBadge=Palette.Label("",8.5f,Palette.Accent);runtimeBadge.Name="runtimeBadge";runtimeBadge.TextAlign=ContentAlignment.MiddleRight;runtimeBadge.SetBounds(480,16,222,28);status.Controls.Add(runtimeBadge);
      detail.Name="runtimeDescription";detail.SetBounds(82,46,620,37);detail.TextAlign=ContentAlignment.TopLeft;status.Controls.Add(detail);
      status.Resize+=delegate{if(layoutScale>0){int pad=(int)(24*layoutScale);runtimeBadge.Left=status.ClientSize.Width-pad-runtimeBadge.Width;runtimeBadge.Visible=status.Width>480*layoutScale;title.Width=Math.Max((int)(100*layoutScale),(runtimeBadge.Visible?runtimeBadge.Left:status.ClientSize.Width-pad)-title.Left);detail.Width=Math.Max(1,status.ClientSize.Width-pad-detail.Left);}};

      var logCard=new Card{Dock=DockStyle.Fill,Name="logCard"};rows.Controls.Add(logCard,0,1);
      logLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty,BackColor=Palette.Surface,Tag="surface"};
      logLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));logLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,62));logLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));logCard.Controls.Add(logLayout);
      logHeading=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,Tag="surface",BackColor=Palette.Surface};logLayout.Controls.Add(logHeading,0,0);
      logTitle=Palette.Label("运行记录",11,Palette.Ink);logTitle.Font=Palette.Font(11,FontStyle.Bold);logTitle.SetBounds(0,0,180,28);logHeading.Controls.Add(logTitle);
      logHint=Palette.Label("记录仅保存在本机。",8.5f,Palette.Muted);logHint.SetBounds(0,29,200,24);logHeading.Controls.Add(logHint);
      copyLogButton=Button("复制诊断",100,delegate{Clipboard.SetText(controller==null?logBox.Text:controller.CopyableLog());ShowFeedback("诊断信息已复制。",false);});copyLogButton.Name="copyDiagnostics";copyLogButton.AccessibleName="复制诊断信息";copyLogButton.SetBounds(430,6,100,34);logHeading.Controls.Add(copyLogButton);
      openLogButton=Button("打开日志",100,delegate{if(controller!=null)controller.OpenLogs();});openLogButton.Name="openLogs";openLogButton.SetBounds(540,6,100,34);openLogButton.Enabled=controller!=null;logHeading.Controls.Add(openLogButton);
      logBox.Name="diagnosticLog";logBox.AccessibleName="本机运行记录";logBox.Dock=DockStyle.Fill;logBox.Multiline=true;logBox.ReadOnly=true;logBox.BorderStyle=BorderStyle.None;logBox.ScrollBars=ScrollBars.Vertical;logBox.HideSelection=false;logBox.Font=Palette.Font(9.5f,FontStyle.Regular);logBox.BackColor=Palette.LogSurface;logBox.ForeColor=Palette.LogInk;logBox.Margin=Padding.Empty;logLayout.Controls.Add(logBox,0,1);

      Card verbose=SettingRow("详细日志","仅在排查问题时开启，日常使用无需打开。",diagnosticBox);rows.Controls.Add(verbose,0,2);
      diagnosticBox.AccessibleName="详细日志";diagnosticBox.Checked=controller!=null && controller.DiagnosticValue;
      diagnosticBox.CheckedChanged+=delegate{if(!syncing && controller!=null)controller.SetDiagnostic(diagnosticBox.Checked);};
      maintenanceActions=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,WrapContents=true};rows.Controls.Add(maintenanceActions,0,3);
      maintenanceActions.Controls.Add(Button("应用手势配置",125,delegate{if(controller!=null){controller.ConfigureGestureMode();syncing=true;startupBox.Checked=GesturePreferences.HasAutostart();syncing=false;}}));
      maintenanceActions.Controls.Add(Button("恢复原生手势",125,delegate{if(controller!=null){controller.RestoreNativeGestures();syncing=true;startupBox.Checked=GesturePreferences.HasAutostart();syncing=false;}}));
      maintenanceActions.Controls.Add(Button("驱动与硬件",115,delegate{if(controller!=null)Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"OpenTrackpad.DriverPanel.exe")){UseShellExecute=false,Arguments=AppPaths.IsIsolated?"--preview":String.Empty});}));
      maintenanceActions.Controls.Add(Button("退出后台",100,delegate{if(controller!=null)controller.ExitApplication();else Close();}));
      logHeading.Resize+=delegate{ReflowRows();};
    }
    static ActionButton Button(string text,int width,Action action){var button=new ActionButton{Text=text,Width=width,Margin=new Padding(0,0,10,0)};button.Click+=delegate{try{action();}catch(Exception error){SettingsForm window=button.FindForm() as SettingsForm;if(window!=null)window.ShowFeedback("操作未完成："+error.Message,true);}};return button;}
    void ShowPage(bool maintenance){appearance.Visible=false;appearanceNav.Selected=false;appearanceNav.Invalidate();if(maintenance && speedDraft.Pending){speed.CancelInteraction();FlushSpeed();}overview.Visible=!maintenance;advanced.Visible=maintenance;home.Selected=!maintenance;diagnostics.Selected=maintenance;home.Invalidate();diagnostics.Invalidate();if(maintenance && logger!=null)logBox.Text=logger.Snapshot()+Environment.NewLine;ReflowRows();}
    public void ShowDiagnostics(){ShowPage(true);}void UpdateSpeedNumber(){speedValue.Text=speed.Value+" / 20";string profile=speed.Value==6?"精细":speed.Value==10?"均衡":speed.Value==17?"快速":"自定义";speedCaption.Text=speedDraft.Pending?"松手后应用":profile;foreach(var button in presetButtons){button.Primary=(int)button.Tag==speed.Value;button.Invalidate();}if(undoButton!=null)undoButton.Enabled=controller!=null && controller.CanUndoSpeed;}
    void AlignSpeedArea(){
      if(layoutScale<=0 || speedArea==null || arranging)return;arranging=true;
      try{
        int pad=(int)(24*layoutScale),gap=(int)(12*layoutScale);bool compact=speedArea.ClientSize.Width<650*layoutScale;
        overviewRows.RowStyles[0].Height=(compact?256:218)*layoutScale;SetPageHeight(overview,compact?752:714);
        speedValue.Anchor=AnchorStyles.Top|AnchorStyles.Left;speedValue.Left=speedArea.ClientSize.Width-pad-speedValue.Width;speedCaption.Left=speedValue.Left;speedTitle.Width=Math.Max(1,Math.Min((int)(220*layoutScale),speedValue.Left-speedTitle.Left-gap));
        if(speedHint!=null)speedHint.Width=Math.Max(1,speedValue.Left-speedHint.Left-gap);
        speed.Anchor=AnchorStyles.Top|AnchorStyles.Left;speed.Width=Math.Max(1,speedArea.ClientSize.Width-(int)(40*layoutScale));
        restoreButton.Text=compact && speedArea.Width<520*layoutScale?"恢复初始":"恢复初始速度";restoreButton.AccessibleName="恢复初始速度";
        restoreButton.Top=undoButton.Top=(int)((compact?189:143)*layoutScale);
        if(compact){
          int actionWidth=Math.Min((int)(115*layoutScale),Math.Max(1,(speedArea.ClientSize.Width-2*pad-gap)/2));
          undoButton.Width=restoreButton.Width=actionWidth;undoButton.Left=pad;restoreButton.Left=speedArea.ClientSize.Width-pad-actionWidth;
          presetRow.Width=Math.Max(1,speedArea.ClientSize.Width-2*pad);
        }else{
          undoButton.Width=(int)(104*layoutScale);restoreButton.Width=(int)(115*layoutScale);
          restoreButton.Left=speedArea.ClientSize.Width-pad-restoreButton.Width;undoButton.Left=restoreButton.Left-gap-undoButton.Width;
          presetRow.Width=Math.Max(1,undoButton.Left-presetRow.Left-gap);
        }
        int margins=0;foreach(var button in presetButtons)margins+=button.Margin.Horizontal;
        int presetWidth=Math.Max(1,(presetRow.Width-margins)/3);if(!compact)presetWidth=Math.Min((int)(112*layoutScale),presetWidth);
        presetRow.SuspendLayout();foreach(var button in presetButtons)button.Width=presetWidth;presetRow.ResumeLayout();
        hideButton.Left=hideButton.Parent.ClientSize.Width-hideButton.Width-(int)(4*layoutScale);stateLabel.Width=Math.Max(1,hideButton.Left-gap);
      }finally{arranging=false;}
    }
    public void SyncEnabled(bool value){syncing=true;enabledBox.Checked=value;syncing=false;UpdateStatus(controller==null?"":controller.CurrentStatus);}public void SyncDesktopHotkeys(bool value){syncing=true;desktopKeysBox.Checked=value;syncing=false;}public void SyncSpeed(int value){if(speedDraft.Pending && speed.IsInteracting)return;syncing=true;speed.Value=PointerSpeedPolicy.Clamp(value);UpdateSpeedNumber();syncing=false;}
    void FlushSpeed(){draftTimer.Stop();speedDraft.Flush();UpdateSpeedNumber();}
    bool NeedsAttention{get{return (controller!=null && (controller.NeedsConfiguration || !string.IsNullOrEmpty(controller.InputWarning) || !string.IsNullOrEmpty(controller.DesktopHotkeyWarning))) || (feedbackError && DateTime.UtcNow<feedbackUntil);}}
    public void ShowFeedback(string text,bool error){feedback=text;feedbackError=error;feedbackUntil=DateTime.UtcNow.AddSeconds(error?10:4);RefreshFooter();}
    public void UpdateStatus(string text){if(IsDisposed)return;if(InvokeRequired){BeginInvoke(new Action<string>(UpdateStatus),text);return;}latestStatus=text;if(text!=lastStatusForFeedback){lastStatusForFeedback=text;bool error=text.Contains("失败") || text.Contains("未能") || text.Contains("未完成") || text.Contains("需要相同权限");if(error || text.StartsWith("统一速度：") || text.StartsWith("已恢复") || text.StartsWith("已撤销") || text.StartsWith("Ctrl＋")){ShowFeedback(text,error);return;}}RefreshFooter();}
    void RefreshFooter(){if(IsDisposed || hideButton==null)return;bool active=controller!=null && controller.DragActive;string warning=controller==null?null:controller.InputWarning??controller.DesktopHotkeyWarning;bool notice=DateTime.UtcNow<feedbackUntil;string concise=controller==null || controller.IsPreview?"●  安全预览 · 不会更改触摸板设置":controller.RuntimePaused?"●  已暂停 · 解锁后自动恢复":!string.IsNullOrEmpty(warning)?warning:notice?feedback:active?"●  三指拖移已启用 · 设置即时生效":controller!=null && controller.NeedsConfiguration?"●  手势配置待生效 · 查看详情":"●  三指拖移已暂停";if(stateLabel.Text!=concise)stateLabel.Text=concise;stateLabel.ForeColor=NeedsAttention?(UiPreferences.Dark?Color.FromArgb(236,194,120):Color.FromArgb(143,91,18)):Palette.Accent;string message=notice?feedback:latestStatus;if(detail.Text!=message)detail.Text=message;hideButton.Text=NeedsAttention?"查看详情":"收起到托盘";string background=controller==null || controller.IsPreview?"●  界面预览":active?"●  后台正在运行":"●  后台运行中 · 手势暂停";if(backgroundState.Text!=background)backgroundState.Text=background;if(undoButton!=null)undoButton.Enabled=controller!=null && controller.CanUndoSpeed;
      if(runtimeBadge!=null){runtimeBadge.Text=controller==null || controller.IsPreview?"独立预览":controller.RuntimePaused?"已暂停":NeedsAttention?"需要处理":active?"手势运行中":controller.EnabledValue?"等待输入":"手势已关闭";runtimeBadge.ForeColor=NeedsAttention?stateLabel.ForeColor:Palette.Accent;
        if(!notice){detail.Text=controller==null || controller.IsPreview?"当前为独立预览，所有操作仅作用于预览资料，不接管真实触摸板。":!string.IsNullOrEmpty(warning)?warning:active?"触摸板监听正常；关闭窗口后会在托盘继续运行。":message;}}
    }
    void ConfigureScrolling(Panel host,int height){host.AutoScroll=true;pageHeights[host]=height;host.Controls[0].Dock=DockStyle.Top;SetPageHeight(host,height);host.SizeChanged+=delegate{int value;if(pageHeights.TryGetValue(host,out value))SetPageHeight(host,value);};}
    void SetPageHeight(Panel host,int logicalHeight){if(!pageHeights.ContainsKey(host) || layoutScale<=0)return;pageHeights[host]=logicalHeight;int minimum=(int)(logicalHeight*layoutScale);if(host.AutoScrollMinSize.Height!=minimum)host.AutoScrollMinSize=new Size(0,minimum);int height=Math.Max(minimum,host.ClientSize.Height);if(host.Controls[0].Height!=height)host.Controls[0].Height=height;}
    void BuildAppearance(){
      var page=Page(appearance,"外观与体验","选择喜欢的字体与配色。所有设置仅保存在本机。");
      var rows=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Margin=Padding.Empty};rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int height in new[]{116,116,176,125})rows.RowStyles.Add(new RowStyle(SizeType.Absolute,height));rows.RowStyles.Add(new RowStyle(SizeType.Percent,100));page.Controls.Add(rows,0,1);appearanceRows=rows;
      themeChoice.Name="themeChoice";themeChoice.AccessibleName="外观模式";themeChoice.Options=new[]{"跟随 Windows","浅色","深色"};themeChoice.SelectedIndex=committedAppearance.Theme=="light"?1:committedAppearance.Theme=="dark"?2:0;
      fontChoice.Name="fontChoice";fontChoice.AccessibleName="界面字体";fontChoice.FontChoices=UiPreferences.Fonts;var labels=new List<string>();int selected=0;foreach(var choice in UiPreferences.Fonts){if(choice.Key==committedAppearance.Font)selected=labels.Count;labels.Add(choice.Label);}fontChoice.Options=labels.ToArray();fontChoice.SelectedIndex=selected;
      rows.Controls.Add(AppearanceRow("外观模式","浅色、深色，或随 Windows 自动切换。",themeChoice),0,0);
      rows.Controls.Add(AppearanceRow("界面字体","仅显示本机已安装字体，选择后立即预览。",fontChoice),0,1);
      var sample=new Card{Dock=DockStyle.Fill,Padding=Padding.Empty,Name="appearanceSample"};rows.Controls.Add(sample,0,2);var sampleTitle=Palette.Label("眼前清晰，手下顺畅。",20,Palette.Ink);sampleTitle.Font=Palette.Font(20,FontStyle.Bold);sampleTitle.SetBounds(24,22,650,42);sampleTitle.Anchor=AnchorStyles.Left|AnchorStyles.Top;sample.Controls.Add(sampleTitle);
      var sampleLine=Palette.Label("Open Trackpad   0123456789   Aa Bb",11,Palette.Muted);sampleLine.SetBounds(24,77,650,30);sampleLine.Anchor=AnchorStyles.Left|AnchorStyles.Top;sample.Controls.Add(sampleLine);var sampleHint=Palette.Label("主题和字体不会改变指针速度、手势或快捷键。",8.5f,Palette.Muted);sampleHint.SetBounds(24,117,650,25);sampleHint.Anchor=AnchorStyles.Left|AnchorStyles.Top;sample.Controls.Add(sampleHint);
      var actions=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};rows.Controls.Add(actions,0,3);saveAppearance.Name="saveAppearance";saveAppearance.Text="保存外观";saveAppearance.Primary=true;saveAppearance.SetBounds(0,8,124,38);saveAppearance.Click+=delegate{SaveAppearance();};actions.Controls.Add(saveAppearance);cancelAppearance.Name="cancelAppearance";cancelAppearance.Text="取消预览";cancelAppearance.SetBounds(136,8,118,38);cancelAppearance.Click+=delegate{CancelAppearance();};actions.Controls.Add(cancelAppearance);appearanceStatus.Name="appearanceStatus";appearanceStatus.SetBounds(0,59,710,55);appearanceStatus.Anchor=AnchorStyles.Left|AnchorStyles.Top;appearanceStatus.Text=UiPreferences.LoadWarning.Length>0?UiPreferences.LoadWarning:"选择后先预览；保存后，下次打开仍保留。";actions.Controls.Add(appearanceStatus);
      themeChoice.SelectionChanged+=delegate{PreviewAppearance();};fontChoice.SelectionChanged+=delegate{PreviewAppearance();};saveAppearance.Enabled=cancelAppearance.Enabled=false;
    }
    Card AppearanceRow(string title,string description,ChoiceButton choice){var card=new Card{Dock=DockStyle.Fill,Padding=Padding.Empty,Tag="appearance-row"};var name=Palette.Label(title,11,Palette.Ink);name.Name="settingTitle";name.Font=Palette.Font(11,FontStyle.Bold);name.SetBounds(24,16,370,30);card.Controls.Add(name);var hint=Palette.Label(description,8.5f,Palette.Muted);hint.Name="settingDescription";hint.SetBounds(24,52,370,42);card.Controls.Add(hint);choice.SetBounds(440,30,270,40);card.Controls.Add(choice);return card;}
    bool AppearanceDirty{get{var value=UiPreferences.Current;return value.Theme!=committedAppearance.Theme || value.Font!=committedAppearance.Font;}}
    void PreviewAppearance(){if(changingAppearance)return;var value=new AppearanceSettings{Theme=themeChoice.SelectedIndex==1?"light":themeChoice.SelectedIndex==2?"dark":"system",Font=UiPreferences.Fonts[fontChoice.SelectedIndex].Key};UiPreferences.Preview(value);ApplyAppearance();appearanceStatus.Text=AppearanceDirty?"正在预览 · 点击“保存外观”后记住，取消可返回原外观。":"当前使用已保存的外观。";saveAppearance.Enabled=cancelAppearance.Enabled=AppearanceDirty;}
    void SaveAppearance(){try{var value=UiPreferences.Current;if(controller!=null)UiPreferences.Save(value);committedAppearance=value.Copy();saveAppearance.Enabled=cancelAppearance.Enabled=false;appearanceStatus.Text=controller==null?"安全预览模式 · 未写入用户设置。":controller.IsPreview?"外观已保存到独立预览资料。":"外观已保存，下次打开继续使用。";appearanceStatus.ForeColor=Palette.Accent;}catch(Exception){CancelAppearance();appearanceStatus.Text="外观未能保存，已恢复原外观。请检查本机资料目录。";appearanceStatus.ForeColor=UiPreferences.Dark?Color.FromArgb(236,194,120):Color.FromArgb(143,91,18);}}
    void CancelAppearance(){UiPreferences.Preview(committedAppearance);changingAppearance=true;themeChoice.SelectedIndex=committedAppearance.Theme=="light"?1:committedAppearance.Theme=="dark"?2:0;for(int index=0;index<UiPreferences.Fonts.Count;index++)if(UiPreferences.Fonts[index].Key==committedAppearance.Font)fontChoice.SelectedIndex=index;changingAppearance=false;ApplyAppearance();saveAppearance.Enabled=cancelAppearance.Enabled=false;appearanceStatus.Text="已返回原外观。";}
    void ShowAppearance(){if(speedDraft.Pending){speed.CancelInteraction();FlushSpeed();}overview.Visible=advanced.Visible=false;appearance.Visible=true;home.Selected=diagnostics.Selected=false;appearanceNav.Selected=true;home.Invalidate();diagnostics.Invalidate();appearanceNav.Invalidate();ReflowRows();}
    public void ShowAppearancePage(){ShowAppearance();}
    void FitBrandName(){
      Control[] names=Controls.Find("brandName",true);if(names.Length==0)return;Control brand=names[0];
      for(float size=11;size>=8.5f;size-=.5f){Font candidate=Palette.Font(size,FontStyle.Bold);brand.Font=candidate;if(TextRenderer.MeasureText(brand.Text,candidate,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+(int)(4*layoutScale)<=brand.ClientSize.Width)break;}
    }
    void ApplyAppearance(){if(applyingAppearance || IsDisposed || Disposing)return;applyingAppearance=true;SuspendLayout();try{Palette.Apply(this);FitBrandName();speedValue.Font=Palette.Font(28,FontStyle.Regular);appliedHighContrast=SystemInformation.HighContrast;RefreshFooter();ReflowRows();}finally{ResumeLayout(true);applyingAppearance=false;}FitToWorkArea();Invalidate(true);}
    void FitToWorkArea(){if(layoutScale<=0 || WindowState!=FormWindowState.Normal)return;Rectangle area=Screen.FromControl(this).WorkingArea;int width=Math.Min(Width,Math.Max(320,area.Width-24)),height=Math.Min(Height,Math.Max(240,area.Height-24));MinimumSize=new Size(Math.Min(MinimumSize.Width,width),Math.Min(MinimumSize.Height,height));int x=Math.Max(area.Left+8,Math.Min(Left,area.Right-width-8)),y=Math.Max(area.Top+8,Math.Min(Top,area.Bottom-height-8));Bounds=new Rectangle(x,y,width,height);}
    void ReflowRows(){
      if(layoutScale<=0 || reflowing)return;reflowing=true;
      try{
        AlignSpeedArea();ReflowPanel(content);appearanceStatus.Width=Math.Max(1,appearanceStatus.Parent.ClientSize.Width-appearanceStatus.Left);
        if(logHeading!=null){
          int width=logHeading.ClientSize.Width,gap=(int)(10*layoutScale);bool compact=width<430*layoutScale;
          logLayout.RowStyles[0].Height=(compact?100:62)*layoutScale;
          copyLogButton.Top=openLogButton.Top=(int)((compact?56:6)*layoutScale);
          openLogButton.Left=compact?copyLogButton.Width+gap:Math.Max(0,width-openLogButton.Width);
          copyLogButton.Left=compact?0:Math.Max(0,openLogButton.Left-gap-copyLogButton.Width);
          logTitle.Width=logHint.Width=Math.Max(1,compact?width:copyLogButton.Left-gap);
        }
        if(maintenanceActions!=null){
          int width=maintenanceActions.ClientSize.Width,used=0,rowHeight=0,total=0;
          foreach(Control button in maintenanceActions.Controls){int needed=button.Width+button.Margin.Horizontal;if(used>0 && used+needed>width){total+=rowHeight;used=0;rowHeight=0;}used+=needed;rowHeight=Math.Max(rowHeight,button.Height+button.Margin.Vertical);}
          total+=rowHeight;advancedRows.RowStyles[3].Height=Math.Max(65*layoutScale,total+12*layoutScale);
          SetPageHeight(advanced,650+(int)Math.Ceiling(Math.Max(0,advancedRows.RowStyles[3].Height/layoutScale-65)));
        }
      }finally{reflowing=false;}
    }
    void ReflowPanel(Control root){
      foreach(Control child in root.Controls){
        if(child.Name=="pageHeading")foreach(Control label in child.Controls)label.Width=Math.Max(1,child.ClientSize.Width-label.Left);
        var card=child as Card;
        if(card!=null){
          string kind=Convert.ToString(card.Tag);
          if(card.Name=="appearanceSample")foreach(Control label in card.Controls)label.Width=Math.Max(1,card.ClientSize.Width-label.Left-(int)(24*layoutScale));
          if(kind=="setting-row" || kind=="appearance-row"){
            int pad=(int)(24*layoutScale),right=card.ClientSize.Width-pad;Control trailing=null;
            foreach(Control item in card.Controls)if(item is Toggle || item is ChoiceButton || item.Name=="windowsGestureSettings")trailing=item;
            bool stacked=kind=="appearance-row" && card.ClientSize.Width<520*layoutScale;
            if(trailing!=null){
              if(trailing is ChoiceButton){trailing.Width=stacked?Math.Max(1,card.ClientSize.Width-2*pad):Math.Max((int)(160*layoutScale),Math.Min((int)(270*layoutScale),(int)(card.ClientSize.Width*.43)));trailing.Top=(int)((stacked?104:30)*layoutScale);}
              trailing.Left=stacked?pad:right-trailing.Width;
            }
            foreach(Control item in card.Controls)if(item.Name=="settingTitle" || item.Name=="settingDescription"){
              int left=kind=="setting-row"?pad+(int)(58*layoutScale):pad;item.Left=left;
              item.Width=Math.Max(1,(stacked || trailing==null?right:trailing.Left-(int)(18*layoutScale))-left);
            }
            if(kind=="appearance-row" && appearanceRows!=null){appearanceRows.RowStyles[0].Height=appearanceRows.RowStyles[1].Height=(stacked?176:116)*layoutScale;SetPageHeight(appearance,stacked?770:650);}
          }
        }
        ReflowPanel(child);
      }
    }
    protected override void WndProc(ref Message message){base.WndProc(ref message);if((message.Msg==0x1a || message.Msg==0x31a) && IsHandleCreated && !IsDisposed && !Disposing){bool previous=UiPreferences.Dark;UiPreferences.RefreshSystemTheme();if(message.Msg==0x31a || previous!=UiPreferences.Dark || appliedHighContrast!=SystemInformation.HighContrast)ApplyAppearance();}}
    protected override bool ProcessCmdKey(ref Message message,Keys key){
      if(key==(Keys.Control|Keys.D1)){ShowPage(false);speed.Focus();return true;}
      if(key==(Keys.Control|Keys.D2)){ShowPage(true);logBox.Focus();logBox.SelectionStart=logBox.TextLength;logBox.SelectionLength=0;return true;}
      if(key==(Keys.Control|Keys.D3)){ShowAppearance();themeChoice.Focus();return true;}
      if(key==(Keys.Control|Keys.S) && appearance.Visible){if(AppearanceDirty)SaveAppearance();return true;}
      if(key==Keys.Escape){
        if(speedDraft.Pending){speedDraft.Cancel();draftTimer.Stop();speed.CancelInteraction();SyncSpeed(controller==null?10:controller.UnifiedSpeedValue);ShowFeedback("已取消尚未应用的调速。",false);}
        else if(appearance.Visible){CancelAppearance();ShowPage(false);speed.Focus();}
        else if(advanced.Visible){ShowPage(false);speed.Focus();}
        else if(controller!=null)controller.HideSettings();else RequestDispose();
        return true;
      }
      return base.ProcessCmdKey(ref message,key);
    }
    void AppendLog(string line){if(IsDisposed || !advanced.Visible)return;if(InvokeRequired){BeginInvoke(new Action<string>(AppendLog),line);return;}logBox.AppendText(line+Environment.NewLine);if(logBox.TextLength>65536)logBox.Text=logBox.Text.Substring(logBox.TextLength-49152);}
    void OnFormClosing(object sender,FormClosingEventArgs e){FlushSpeed();UiPreferences.Preview(committedAppearance);if(!allowDispose && e.CloseReason==CloseReason.UserClosing && controller!=null){e.Cancel=true;BeginInvoke(new Action(controller.HideSettings));}}
    public void RequestDispose(){if(IsDisposed)return;allowDispose=true;Close();}
  }
}

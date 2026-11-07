using System;
using OpenTrackpadUi;
using OpenTrackpadShared;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public sealed class TrackpadWindow : Form
{
    readonly string root = AppPaths.ProgramDirectory;
    readonly string dataRoot = AppPaths.DataDirectory;
    readonly string stateRoot = AppPaths.StateDirectory;
    readonly string resources = ResourceDirectory();
    readonly Label status = new Label();
    readonly Label detail = new Label();
    readonly TextBox log = new TextBox();
    readonly ActionButton install = new ActionButton();
    readonly ActionButton restore = new ActionButton();
    readonly ActionButton refresh = new ActionButton();
    bool busy;
    bool changingDriver;
    static bool renderMode;

    static string ResourceDirectory() {
        string requested=Environment.GetEnvironmentVariable("OPENTRACKPAD_RESOURCES_DIR");
        try{return String.IsNullOrWhiteSpace(requested)?AppPaths.ProgramDirectory:Path.GetFullPath(requested);}catch{return AppPaths.ProgramDirectory;}
    }
    bool HasPackage {get{return File.Exists(Path.Combine(resources,"driver-manifest.json")) && File.Exists(Path.Combine(resources,"driver","AmtPtpDevice.inf"));}}
    bool HasRecovery {get{return HasPackage && File.Exists(Path.Combine(stateRoot,"baseline.json"));}}

    public TrackpadWindow()
    {
        Text = "Open Trackpad · 驱动与硬件";
        SuspendLayout(); AutoScaleMode=AutoScaleMode.None;
        ClientSize=new Size(920,760);MinimumSize=new Size(700,540);StartPosition=FormStartPosition.CenterScreen;
        BackColor=Palette.Canvas;Font=Palette.Font(9.5f,FontStyle.Regular);DoubleBuffered=true;
        try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
        var rootLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(28),Margin=Padding.Empty};
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(int height in new[]{105,125,155})rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));Controls.Add(rootLayout);
        var header=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};rootLayout.Controls.Add(header,0,0);
        Label title=Palette.Label("驱动与硬件",24,Palette.Ink);title.Font=Palette.Font(24,FontStyle.Bold);title.SetBounds(0,0,700,50);header.Controls.Add(title);
        Label subtitle=Palette.Label("Open Trackpad  /  Magic Trackpad 2",9,Palette.Muted);subtitle.SetBounds(2,56,700,30);header.Controls.Add(subtitle);
        var daily=new Card{Dock=DockStyle.Fill};rootLayout.Controls.Add(daily,0,1);
        Label dailyTitle=Palette.Label("日常设置，从这里开始",12,Palette.Ink);dailyTitle.Font=Palette.Font(12,FontStyle.Bold);dailyTitle.SetBounds(24,16,490,34);daily.Controls.Add(dailyTitle);
        Label dailyHint=Palette.Label("统一速度、手势与桌面快捷键",9,Palette.Muted);dailyHint.SetBounds(24,58,490,28);daily.Controls.Add(dailyHint);
        var openSettings=new ActionButton{Text="打开速度与手势",Primary=true};openSettings.SetBounds(610,30,205,44);
        openSettings.Click+=delegate{Open(Path.Combine(root,"OpenTrackpad.exe"));};daily.Controls.Add(openSettings);
        var device=new Card{Dock=DockStyle.Fill};rootLayout.Controls.Add(device,0,2);
        status.Text=renderMode?"界面预览":"正在读取设备状态…";status.Tag="ink";status.Font=Palette.Font(13,FontStyle.Bold);status.ForeColor=Palette.Ink;status.Dock=DockStyle.Top;status.Height=44;status.BackColor=Color.Transparent;device.Controls.Add(status);
        detail.Text=renderMode?"独立预览，不会修改触摸板或驱动。":"Magic Trackpad 2\r\n日常手势设置不需要重新安装驱动。";detail.Tag="muted";detail.Font=Font;detail.ForeColor=Palette.Muted;detail.Dock=DockStyle.Bottom;detail.Height=58;detail.BackColor=Color.Transparent;device.Controls.Add(detail);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Margin=new Padding(0,0,0,12)};rootLayout.Controls.Add(actions,0,3);
        install.Text="切换到免费驱动";install.Width=174;install.Height=38;install.Margin=new Padding(0,0,10,0);install.Click+=delegate{RunAction("Install",true);};actions.Controls.Add(install);
        restore.Text="恢复原驱动";restore.Width=128;restore.Height=38;restore.Margin=new Padding(0,0,10,0);restore.Click+=delegate{RunAction("Rollback",true);};actions.Controls.Add(restore);
        refresh.Text="刷新状态";refresh.Width=112;refresh.Height=38;refresh.Margin=new Padding(0,0,10,0);refresh.Click+=delegate{RunAction("Status",false);};actions.Controls.Add(refresh);
        var windows=new ActionButton{Text="Windows 手势",Width=148,Margin=new Padding(0,0,10,0)};windows.Click+=delegate{Open("ms-settings:devices-touchpad");};actions.Controls.Add(windows);
        var haptics=new ActionButton{Text="电量与触觉",Width=132,Enabled=File.Exists(Path.Combine(resources,"AmtPtpControlPanel.exe"))};haptics.Click+=delegate{Open(Path.Combine(resources,"AmtPtpControlPanel.exe"));};actions.Controls.Add(haptics);
        log.Dock=DockStyle.Fill;log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;log.BackColor=Palette.LogSurface;log.ForeColor=Palette.LogInk;log.Font=Palette.Font(9,FontStyle.Regular);log.BorderStyle=BorderStyle.None;
        log.Text=AppPaths.IsIsolated?"独立预览\r\n\r\n此模式不会安装或恢复驱动，也不会改动系统设置。":HasPackage?"设备维护\r\n\r\n已找到可选驱动包，安装前会验证哈希和微软签名。驱动切换可能短暂断开设备，请保留另一种输入方式。":"可选驱动包未附带\r\n\r\n这不影响已经配置好的设备。内置精确式触摸板可直接使用日常设置；Apple 设备的额外驱动请参阅开源项目说明。\r\n\r\n应用不会自动下载驱动，也不会自动重启电脑。";rootLayout.Controls.Add(log,0,4);
        var links=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,12,0,0),Margin=Padding.Empty};rootLayout.Controls.Add(links,0,5);
        var sources=new LinkLabel{Text="开源项目",AutoSize=true,LinkColor=Palette.Accent,Margin=new Padding(0,0,28,0)};sources.Click+=delegate{Open("https://github.com/vitoplantamura/MagicTrackpad2ForWindows");};links.Controls.Add(sources);
        var files=new LinkLabel{Text="打开本机数据目录",AutoSize=true,LinkColor=Palette.Accent};files.Click+=delegate{Directory.CreateDirectory(dataRoot);Open(dataRoot);};links.Controls.Add(files);
        Shown+=delegate{FitScreen();if(!renderMode)RunAction("Status",false);};FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy && changingDriver && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;status.Text="驱动操作进行中，请等待完成后关闭。";}};
        ResumeLayout(true);using(var graphics=CreateGraphics()){float factor=graphics.DpiX/96;if(Math.Abs(factor-1)>.01)Scale(new SizeF(factor,factor));}
        float unit;using(var graphics=CreateGraphics())unit=graphics.DpiX/96;openSettings.Left=daily.Width-(int)(24*unit)-openSettings.Width;daily.BackColor=Palette.Canvas;
        Action arrange=delegate {int pad=(int)(24*unit);openSettings.Left=daily.ClientSize.Width-pad-openSettings.Width;int available=Math.Max(100,openSettings.Left-pad-16);dailyTitle.Width=dailyHint.Width=available;};
        daily.Resize+=delegate{arrange();};arrange();
        Activated+=delegate{UiPreferences.Reload();Palette.Apply(this);};
        UpdateAvailability();
    }

    void FitScreen(){Rectangle area=Screen.FromControl(this).WorkingArea;MinimumSize=new Size(Math.Min(MinimumSize.Width,area.Width-24),Math.Min(MinimumSize.Height,area.Height-24));Size=new Size(Math.Min(Width,area.Width-24),Math.Min(Height,area.Height-24));Left=Math.Max(area.Left+8,Math.Min(Left,area.Right-Width-8));Top=Math.Max(area.Top+8,Math.Min(Top,area.Bottom-Height-8));}
    void UpdateAvailability(){refresh.Enabled=!busy && !renderMode;install.Enabled=!busy && !renderMode && !AppPaths.IsIsolated && HasPackage;restore.Enabled=!busy && !renderMode && !AppPaths.IsIsolated && HasRecovery;}

    bool ConfirmDriverChange(string action) {
        using(var dialog=new Form{Text="Open Trackpad",ClientSize=new Size(500,260),AutoScaleMode=AutoScaleMode.Dpi,AutoScaleDimensions=new SizeF(96,96),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=false,BackColor=Palette.Canvas,Font=Font}) {
            var title=Palette.Label(action=="Install"?"安装已校验的驱动？":"恢复已备份的驱动？",15,Palette.Ink);title.Font=Palette.Font(15,FontStyle.Bold);title.SetBounds(24,20,450,40);dialog.Controls.Add(title);
            var body=Palette.Label("触摸板可能短暂断开，请保留另一种输入方式。\r\n接下来会请求 Windows 管理员权限。\r\n应用不会自动重启电脑。",9.5f,Palette.Muted);body.SetBounds(24,75,450,95);dialog.Controls.Add(body);
            var cancel=new ActionButton{Text="取消",DialogResult=DialogResult.Cancel};cancel.SetBounds(260,195,100,38);dialog.Controls.Add(cancel);
            var proceed=new ActionButton{Text="继续",DialogResult=DialogResult.OK,Primary=true};proceed.SetBounds(372,195,100,38);dialog.Controls.Add(proceed);dialog.AcceptButton=cancel;dialog.CancelButton=cancel;
            return dialog.ShowDialog(this)==DialogResult.OK;
        }
    }
    static string Quote(string value){int trailing=0;for(int index=value.Length-1;index>=0 && value[index]=='\\';index--)trailing++;return "\""+value+new String('\\',trailing)+"\"";}

    sealed class DeviceState {public bool Connected,Primary,Healthy,Free,Paid;}
    static bool IsTarget(string id){return Regex.IsMatch(id,@"(?i)(PID&0265|PID_0265)") && Regex.IsMatch(id,@"(?i)(0001004C|VID_05AC|VID&000[12]05AC)");}
    static bool IsPrimary(string id){return Regex.IsMatch(id,@"(?i)^HID\\.*PID(?:&|_)0265.*(?:&|_)COL01");}
    static string Property(ManagementBaseObject row,string name){object value=row[name];return value==null?String.Empty:Convert.ToString(value);}
    static DeviceState ReadDeviceState(){
        var state=new DeviceState();bool allHealthy=true;
        using(var search=new ManagementObjectSearcher("root\\CIMV2","SELECT PNPDeviceID,Status,Present,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '%0265%'")){
            search.Options.Timeout=TimeSpan.FromSeconds(15);
            using(var devices=search.Get())foreach(ManagementBaseObject row in devices)using(row){
                string id=Property(row,"PNPDeviceID");if(!IsTarget(id) || !(row["Present"] is bool) || !(bool)row["Present"])continue;
                state.Connected=true;
                if(IsPrimary(id)){state.Primary=true;allHealthy=allHealthy && Property(row,"Status")=="OK" && Property(row,"ConfigManagerErrorCode")=="0";}
            }
        }
        if(state.Connected)using(var search=new ManagementObjectSearcher("root\\CIMV2","SELECT DeviceID,DriverProviderName FROM Win32_PnPSignedDriver WHERE DeviceID LIKE '%0265%'")){
            search.Options.Timeout=TimeSpan.FromSeconds(15);
            using(var drivers=search.Get())foreach(ManagementBaseObject row in drivers)using(row){
                string id=Property(row,"DeviceID");if(!IsTarget(id))continue;string provider=Property(row,"DriverProviderName");
                if(IsPrimary(id) && provider.IndexOf("Vito Plantamura",StringComparison.OrdinalIgnoreCase)>=0)state.Free=true;
                if(provider.Equals("Magic Utilities Pty Ltd",StringComparison.OrdinalIgnoreCase))state.Paid=true;
            }
        }
        state.Healthy=state.Primary && allHealthy;return state;
    }
    void ReadStatus(){
        busy=true;changingDriver=false;UpdateAvailability();status.Text="正在检测设备…";
        Task.Factory.StartNew(delegate{
            DeviceState device=null;try{device=ReadDeviceState();}catch{}
            if(IsDisposed || !IsHandleCreated)return;
            try{BeginInvoke((Action)delegate{
                if(IsDisposed)return;busy=false;UpdateAvailability();
                if(device==null){status.Text="暂时无法读取设备状态";detail.Text="请稍后刷新，或在 Windows 设置中检查连接。";return;}
                status.Text=!device.Connected?"暂未检测到 Magic Trackpad 2":!device.Primary?"设备已连接，等待触摸板输入接口":device.Free && device.Paid?"检测到驱动绑定冲突":device.Free?"开源驱动已绑定":device.Paid?"当前使用 Magic Utilities 驱动":device.Healthy?"触摸板接口正常":"触摸板接口需要检查";
                detail.Text=!device.Connected?"请检查电源、蓝牙或连接线。\r\n内置触摸板请回到日常速度与手势设置。":!device.Primary?"请轻触触摸板唤醒设备，再刷新状态。\r\n仅检测到蓝牙父接口不代表设备故障。":"设备已连接。滚动、拖移与触觉请在设备上实际体验。";
                log.Text="本次检测只读取 Windows 设备信息，不启动脚本或更改驱动。\r\n\r\n"+(HasPackage?"已找到可选驱动包。安装前仍需验证文件与签名。":"未附带可选驱动包；已经正常工作的设备无需重新安装。")+"\r\n\r\n用户设置和日志保存在本机资料目录。";
                install.Enabled=install.Enabled && device.Connected && !device.Free;
            });}catch(InvalidOperationException){}
        });
    }

    void Open(string target)
    {
        if(renderMode)return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch {status.Text="未能打开目标";log.Text="请确认对应文件或 Windows 功能可用，再重试。";}
    }

    void RunAction(string action, bool elevated)
    {
        if (busy || renderMode) return;
        if(action=="Status"){ReadStatus();return;}
        if(elevated && (AppPaths.IsIsolated || !HasPackage || action=="Rollback" && !HasRecovery))return;
        if(elevated && !ConfirmDriverChange(action))return;
        Directory.CreateDirectory(stateRoot);
        changingDriver=elevated;
        busy = true; install.Enabled = restore.Enabled = refresh.Enabled = false;
        status.Text = elevated ? "等待管理员确认并执行操作…" : "正在检测设备…";
        string result = Path.Combine(stateRoot, "driver-ui-" + Guid.NewGuid().ToString("N") + ".json");
        Task.Factory.StartNew(delegate {
            string error = null;
            try
            {
                var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe"));
                info.Arguments = "-NoProfile -NonInteractive -File " + Quote(Path.Combine(root, "Manage-Trackpad.ps1")) + " -Action " + action + " -DataDirectory "+Quote(dataRoot)+" -PackageRoot "+Quote(resources)+" -ResultPath " + Quote(result);
                info.WorkingDirectory = root; info.WindowStyle = ProcessWindowStyle.Hidden;
                info.UseShellExecute = elevated;
                if (elevated) info.Verb = "runas";
                else { info.CreateNoWindow = true; info.RedirectStandardError = true; info.RedirectStandardOutput = true; }
                using (var process = Process.Start(info))
                {
                    Task<string> stdout=null,stderr=null;
                    if(!elevated){stdout=process.StandardOutput.ReadToEndAsync();stderr=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(45000)){process.Kill();throw new TimeoutException();}}
                    process.WaitForExit();
                    if(!elevated){stdout.GetAwaiter().GetResult();string reported=stderr.GetAwaiter().GetResult();if(process.ExitCode!=0)error=reported;}
                }
            }
            catch (System.ComponentModel.Win32Exception ex) { error = ex.NativeErrorCode == 1223 ? "管理员确认已取消，未执行驱动切换。" : "未能启动设备检测组件，请确认应用包完整。"; }
            catch (TimeoutException) { error = "设备检测耗时较长，请稍后重试。"; }
            catch (Exception) { error = "操作暂时无法完成，请检查应用包是否完整。"; }
            if(IsDisposed || !IsHandleCreated)return;
            try { BeginInvoke((Action)delegate {
                if(IsDisposed)return;
                busy = changingDriver = false; UpdateAvailability();
                try
                {
                    if (!File.Exists(result) || new FileInfo(result).Length>1048576) { status.Text = "操作未完成"; log.Text = error==null?"没有收到操作结果，请稍后重试。":error.Length>900?error.Substring(0,900):error; return; }
                    var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(result, Encoding.UTF8));
                    bool success = data.ContainsKey("Success") && (bool)data["Success"];
                    string active = data.ContainsKey("Active") ? Convert.ToString(data["Active"]) : "unknown";
                    string label = active == "free" ? "免费开源驱动已绑定" : active == "magic" ? "当前使用 Magic Utilities 付费驱动" : active == "mixed" ? "检测到两种驱动同时绑定" : "当前使用系统驱动或未检测到设备";
                    bool reboot = data.ContainsKey("RebootRequired") && (bool)data["RebootRequired"];
                    status.Text = !success ? "操作未完成，请查看说明" : reboot ? "等待重启验证" : label;
                    if(success && !reboot && active=="system" && data.ContainsKey("Connected") && (bool)data["Connected"] && data.ContainsKey("PrimaryDetected") && !(bool)data["PrimaryDetected"])status.Text="设备已连接，等待触摸板输入接口";
                    if (data.ContainsKey("Connected")) detail.Text = (bool)data["Connected"] ? "Magic Trackpad 2 已连接 · PID 0265\r\n驱动绑定检测通过后，还需在设备上验证滚动和手势。" : "未检测到已连接的 Magic Trackpad 2，请检查蓝牙连接。";
                    if(data.ContainsKey("PrimaryDetected") && !(bool)data["PrimaryDetected"] && data.ContainsKey("Connected") && (bool)data["Connected"])detail.Text="请轻触触摸板唤醒设备，再刷新状态。\r\n内置触摸板请回到速度与手势设置。";
                    if(action=="Status" && success && data.ContainsKey("Message"))log.Text=Convert.ToString(data["Message"])+"\r\n\r\n"+(HasPackage?"可选驱动包已就绪，应用不会自动切换驱动。":"未附带可选驱动包。已经正常工作的设备无需重新安装驱动。");
                    if (action != "Status" || !success) log.Text = Convert.ToString(data["Message"]) + "\r\n\r\n操作日志保存在本机数据目录的 state 文件夹。";
                    install.Enabled = install.Enabled && success && active != "free" && data.ContainsKey("Connected") && (bool)data["Connected"];
                    File.Delete(result);
                }
                catch (Exception) { status.Text = "读取结果失败"; log.Text = "操作结果暂时无法读取，请重新检测。"; }
            }); } catch(InvalidOperationException) { }
        });
    }

    [STAThread]
    public static void Main(string[] args)
    {
        string verb=args.Length==0?String.Empty:args[0];
        bool pathArgument=args.Length==2 && !String.IsNullOrWhiteSpace(args[1]);
        bool valid=args.Length==0 || verb=="--preview" && args.Length==1 || verb=="--self-test" && (args.Length==1 || pathArgument) || (verb=="--render" || verb=="--query-device") && pathArgument || (verb=="--help" || verb=="--version") && args.Length==1;
        if(!valid){Console.Error.WriteLine("Use --preview, --render <png>, --query-device <json>, --self-test [json], --version or --help.");Environment.ExitCode=2;return;}
        if(verb=="--help"){Console.WriteLine("Open Trackpad hardware panel. --query-device reads device status; --preview never changes settings.");return;}
        if(verb=="--version"){Console.WriteLine("Open Trackpad "+Application.ProductVersion);return;}
        if(verb=="--query-device"){
            try{DeviceState found=ReadDeviceState();File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{success=true,connected=found.Connected,primaryDetected=found.Primary,healthy=found.Healthy,openSourceDriver=found.Free,magicUtilitiesDriver=found.Paid,readOnly=true}),new UTF8Encoding(false));}
            catch{Environment.ExitCode=1;}
            return;
        }
        renderMode=verb=="--self-test" || verb=="--render" || verb=="--preview";
        if(renderMode && String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENTRACKPAD_DATA_DIR")))Environment.SetEnvironmentVariable("OPENTRACKPAD_DATA_DIR",Path.Combine(Path.GetTempPath(),"OpenTrackpad-driver-preview-"+Guid.NewGuid().ToString("N")));
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length > 0 && args[0] == "--self-test")
        {
            using (var window = new TrackpadWindow())
            {
                bool passed=window.install.Parent!=null && !window.install.Enabled && !window.restore.Enabled;
                if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{success=passed,isolated=true,driverChangesDisabled=true}),new UTF8Encoding(false));
                if(!passed)Environment.Exit(1);
            }
            return;
        }
        if (args.Length > 1 && args[0] == "--render")
        {
            renderMode = true;
            using (var window = new TrackpadWindow())
            {
                window.Show(); Application.DoEvents();
                using (var bitmap = new Bitmap(window.ClientSize.Width, window.ClientSize.Height))
                {
                    window.Controls[0].DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                }
                window.Close();
            }
            return;
        }
        Application.Run(new TrackpadWindow());
    }
}

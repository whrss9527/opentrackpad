using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenTrackpadShared;

namespace ThreeFingerDrag {
  sealed class InputWindow : NativeWindow, IDisposable {
    readonly Logger logger;
    readonly DragController drag;
    readonly Action<string> statusSink;
    readonly Dictionary<IntPtr, HidDevice> devices = new Dictionary<IntPtr, HidDevice>();
    readonly Timer staleTimer = new Timer();
    IntPtr buffer = IntPtr.Zero;
    int capacity;
    int rawReports;
    IntPtr owner = IntPtr.Zero;
    DateTime lastStatus = DateTime.MinValue;
    bool disposed;
    bool listening;
    public bool Listening {get{return listening;}}
    public bool Enabled = true;
    public Action ShowSettingsRequested;
    public Action ShutdownRequested;
    public Action<int> HotkeyReceived;
    public Action<int> RuntimeEventReceived;

    public InputWindow(Logger logger, DragController drag, Action<string> statusSink) {
      this.logger = logger;
      this.drag = drag;
      this.statusSink = statusSink;
      CreateParams parameters = new CreateParams();
      parameters.Caption = "ThreeFingerDrag.Input";
      parameters.Parent = new IntPtr(-3);
      CreateHandle(parameters);
      staleTimer.Interval = 80;
      staleTimer.Tick += delegate {
        drag.ReleaseIfStale();
        if (!drag.IsTracking) { owner = IntPtr.Zero; staleTimer.Stop(); }
      };
    }

    public void Register() {
      if(AppPaths.IsIsolated){statusSink("预览模式：不注册触摸板输入，不更改系统设置。");return;}
      drag.ForceRelease("before input registration");
      if(!drag.CanShutdown){Enabled=false;staleTimer.Start();statusSink("正在释放上次拖移，稍后恢复监听。");return;}
      if(listening)return;
      RAWINPUTDEVICE[] devices = new RAWINPUTDEVICE[1];
      devices[0].usUsagePage = 0x0D;
      devices[0].usUsage = 0x05;
      devices[0].dwFlags = Native.RIDEV_INPUTSINK | Native.RIDEV_DEVNOTIFY;
      devices[0].hwndTarget = Handle;
      if (!Native.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)))) {
        int error = Marshal.GetLastWin32Error();
        statusSink("Raw Input registration failed: " + error);
        logger.Error("RegisterRawInputDevices failed: " + error);
        return;
      }
      statusSink("三指触点监听已启动。");
      listening=true;
      logger.Info("Registered HID touchpad Raw Input");
    }

    protected override void WndProc(ref Message message) {
      if(message.Msg==Native.WM_RUNTIME_EVENT) {
        if(RuntimeEventReceived!=null)RuntimeEventReceived(message.WParam.ToInt32());
      } else if(message.Msg==DesktopHotkeys.Message) {
        if(HotkeyReceived!=null)HotkeyReceived(message.WParam.ToInt32());
      } else if(message.Msg == Native.WM_SHOW_SETTINGS) {
        if(ShowSettingsRequested!=null)ShowSettingsRequested();
      } else if(message.Msg==Native.WM_EXIT_DRAG) {
        if(ShutdownRequested!=null)ShutdownRequested();
      } else if(message.Msg == Native.WM_CANCEL_DRAG) {
        Cancel("session changed");
      } else if (message.Msg == Native.WM_INPUT) {
        if (Enabled) HandleInput(message.LParam);
        else {
          drag.ForceRelease("paused");
          if (drag.IsTracking) staleTimer.Start();
          else staleTimer.Stop();
        }
      } else if (message.Msg == Native.WM_INPUT_DEVICE_CHANGE && message.WParam.ToInt32() == Native.GIDC_REMOVAL) {
        if (owner == message.LParam) {
          drag.ForceRelease("input device removed");
          if (!drag.IsTracking) owner = IntPtr.Zero;
          else staleTimer.Start();
        }
        HidDevice device;
        if (devices.TryGetValue(message.LParam, out device)) {
          device.Dispose();
          devices.Remove(message.LParam);
        }
      }
      base.WndProc(ref message);
    }

    public void Cancel(string reason) {
      drag.ForceRelease(reason);
      if(!drag.IsTracking){owner=IntPtr.Zero;staleTimer.Stop();}
      else staleTimer.Start();
    }
    public void ResetDevices(string reason) {
      Cancel(reason);
      foreach(HidDevice device in devices.Values)device.Dispose();
      devices.Clear();
    }
    public void Suspend() {
      Enabled=false;ResetDevices("runtime paused");
      if(!listening)return;
      RAWINPUTDEVICE[] registration=new RAWINPUTDEVICE[1];
      registration[0].usUsagePage=0x0D;registration[0].usUsage=0x05;registration[0].dwFlags=1;
      if(Native.RegisterRawInputDevices(registration,1,(uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))listening=false;
    }

    void EnsureBuffer(int needed) {
      if (capacity >= needed) return;
      int next = Math.Max(needed, capacity == 0 ? 256 : capacity * 2);
      IntPtr replacement = Marshal.AllocHGlobal(next);
      if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
      buffer = replacement;
      capacity = next;
    }

    void HandleInput(IntPtr inputHandle) {
      uint size = 0;
      uint headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
      if (Native.GetRawInputData(inputHandle, Native.RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0) return;
      int prefix=Marshal.SizeOf(typeof(RAWINPUTHEADER))+Marshal.SizeOf(typeof(RAWHID));
      if(size<(uint)prefix || size>1024*1024){Cancel("invalid input allocation size");return;}
      EnsureBuffer((int)size);
      uint copied=Native.GetRawInputData(inputHandle, Native.RID_INPUT, buffer, ref size, headerSize);
      if (copied == uint.MaxValue || copied != size || size<(uint)prefix || size>(uint)capacity) {
        logger.Error("GetRawInputData failed: " + Marshal.GetLastWin32Error());
        return;
      }

      try {
        RAWINPUTHEADER header = (RAWINPUTHEADER)Marshal.PtrToStructure(buffer, typeof(RAWINPUTHEADER));
        if (header.dwType != Native.RIM_TYPEHID || header.hDevice == IntPtr.Zero) return;
        rawReports++;
        if (rawReports == 1) logger.Info("First raw HID report received");
        RAWHID hid = (RAWHID)Marshal.PtrToStructure(
          IntPtr.Add(buffer, Marshal.SizeOf(typeof(RAWINPUTHEADER))), typeof(RAWHID));

        HidDevice device;
        if (!devices.TryGetValue(header.hDevice, out device)) {
          device = HidDevice.Create(header.hDevice, logger);
          devices[header.hDevice] = device;
        }
        if (!device.Ok) {
          ThrottledStatus("HID data received, but finger coordinates could not be parsed. Reports: " + rawReports);
          return;
        }

        int offset = Marshal.SizeOf(typeof(RAWINPUTHEADER)) + Marshal.SizeOf(typeof(RAWHID));
        if (hid.dwSizeHid == 0 || hid.dwCount > ((long)size-offset)/hid.dwSizeHid) {
          drag.ForceRelease("invalid raw input size");
          if (drag.IsTracking) staleTimer.Start();
          return;
        }
        for (uint i = 0; i < hid.dwCount; i++) {
          if (owner != IntPtr.Zero && owner != header.hDevice) continue;
          List<TouchContact> contacts = device.Parse(
            IntPtr.Add(buffer, offset + (int)(i * hid.dwSizeHid)), (int)hid.dwSizeHid);
          if (contacts == null) continue;
          ThrottledRunningStatus(device.DisplayName,contacts.Count);
          if (owner == IntPtr.Zero && contacts.Count != 3) continue;
          if (owner == IntPtr.Zero) owner = header.hDevice;
          drag.Handle(contacts);
          if (drag.IsTracking && !staleTimer.Enabled) staleTimer.Start();
          if (!drag.IsTracking) { owner = IntPtr.Zero; staleTimer.Stop(); }
        }
      } catch (Exception exception) {
        drag.ForceRelease("input parsing error");
        if (drag.IsTracking) staleTimer.Start();
        logger.Error(exception.ToString());
        statusSink("Input processing failed. Copy the diagnostics for troubleshooting.");
      }
    }

    void ThrottledStatus(string text) {
      if ((DateTime.UtcNow - lastStatus).TotalMilliseconds < 800) return;
      lastStatus = DateTime.UtcNow;
      statusSink(text);
    }

    void ThrottledRunningStatus(string deviceName,int contactCount) {
      if ((DateTime.UtcNow - lastStatus).TotalMilliseconds < 800) return;
      lastStatus = DateTime.UtcNow;
      statusSink(deviceName+"：三指拖移运行中；触点：" + contactCount + "，报告：" + rawReports);
    }

    public void Dispose() {
      if (disposed) return;
      disposed = true;
      Suspend();
      staleTimer.Stop();
      staleTimer.Dispose();
      drag.ForceRelease("input disposed");
      foreach (HidDevice device in devices.Values) device.Dispose();
      devices.Clear();
      if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
      buffer = IntPtr.Zero;
      capacity = 0;
      DestroyHandle();
    }
  }

  sealed class HidDevice : IDisposable {
    const ushort DigitizerPage = 0x0D;
    const ushort GenericDesktopPage = 0x01;
    const ushort TipSwitch = 0x42;
    const ushort ContactIdentifier = 0x51;
    const ushort ContactCount = 0x54;
    const ushort X = 0x30;
    const ushort Y = 0x31;
    const int HidOk = 0x00110000;
    IntPtr preparsed;
    readonly List<FingerLink> links;
    readonly List<TouchContact> contacts = new List<TouchContact>(5);
    readonly ushort[] usages = new ushort[16];
    readonly Logger logger;
    bool disposed;
    public bool Ok;
    public string Name;
    public int ReportLength;
    public byte ReportId;
    public double WidthMm {get{return links.Count==0?0:links[0].WidthMm;}}
    public double HeightMm {get{return links.Count==0?0:links[0].HeightMm;}}
    public string DisplayName {get {
      string name=(Name??string.Empty).ToUpperInvariant();
      return name.Contains("GXT7863")?"内置触摸板":name.Contains("0265")?"苹果触摸板":"精确式触摸板";
    }}

    HidDevice(IntPtr preparsed, List<FingerLink> links, Logger logger, bool ok) {
      this.preparsed = preparsed;
      this.links = links;
      this.logger = logger;
      Ok = ok;
    }

    public static HidDevice Create(IntPtr handle, Logger logger) {
      string name = GetName(handle);
      logger.Diagnostic("Device: " + name);
      uint size = 0;
      Native.GetRawInputDeviceInfo(handle, Native.RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
      if (size == 0) {
        logger.Error("No preparsed HID data");
        return new HidDevice(IntPtr.Zero, new List<FingerLink>(), logger, false);
      }

      IntPtr data = Marshal.AllocHGlobal((int)size);
      if (Native.GetRawInputDeviceInfo(handle, Native.RIDI_PREPARSEDDATA, data, ref size) == uint.MaxValue) {
        logger.Error("RIDI_PREPARSEDDATA failed");
        Marshal.FreeHGlobal(data);
        return new HidDevice(IntPtr.Zero, new List<FingerLink>(), logger, false);
      }

      HIDP_CAPS caps;
      int status = Native.HidP_GetCaps(data, out caps);
      if (status != HidOk) {
        logger.Error("HidP_GetCaps 0x" + status.ToString("X8"));
        Marshal.FreeHGlobal(data);
        return new HidDevice(IntPtr.Zero, new List<FingerLink>(), logger, false);
      }

      logger.Diagnostic("Caps UsagePage=0x" + caps.UsagePage.ToString("X2") +
        " Usage=0x" + caps.Usage.ToString("X2") + " Values=" + caps.NumberInputValueCaps);
      VC[] values = GetValueCaps(data, caps, logger);
      List<FingerLink> links = BuildLinks(values);
      logger.Info("Finger coordinate links: " + links.Count);
      return new HidDevice(data, links, logger, caps.UsagePage == DigitizerPage && caps.Usage == 5 && links.Count >= 3) {
        Name = name, ReportLength = caps.InputReportByteLength,
        ReportId = links.Count > 0 ? links[0].ReportId : (byte)0
      };
    }

    public List<TouchContact> Parse(IntPtr report, int length) {
      contacts.Clear();
      if (length <= 0) throw new ArgumentException("Empty HID input report");
      if (Marshal.ReadByte(report) != ReportId) return null;
      if (length != ReportLength) throw new ArgumentException("Unexpected HID input report length");
      int reportedCount;
      if (!TryValue(report, length, DigitizerPage, 0, ContactCount, out reportedCount)) throw new ArgumentException("Missing HID contact count");
      if (reportedCount < 0 || reportedCount > links.Count) throw new ArgumentException("Fragmented or invalid HID contact count");
      if (reportedCount == 0) return contacts;
      foreach (FingerLink link in links) {
        bool tip;
        if (!TryTip(report, length, link.Id, out tip) || !tip) continue;
        int x, y;
        if (!TryValue(report, length, GenericDesktopPage, link.Id, X, out x) ||
            !TryValue(report, length, GenericDesktopPage, link.Id, Y, out y)) continue;
        if (x < link.X0 || x > link.X1 || y < link.Y0 || y > link.Y1) continue;
        int id;
        if (!TryValue(report, length, DigitizerPage, link.Id, ContactIdentifier, out id)) continue;
        contacts.Add(new TouchContact(id, x, y, link));
      }

      int count;
      if (logger.Verbose && contacts.Count == 0 &&
          TryValue(report, length, DigitizerPage, 0, ContactCount, out count) && count > 0) {
        logger.Diagnostic("ContactCount=" + count + " but parsed contacts=0");
      }
      return contacts;
    }

    bool TryValue(IntPtr report, int length, ushort page, ushort link, ushort usage, out int value) {
      uint raw;
      int status = Native.HidP_GetUsageValue(HIDP_REPORT_TYPE.HidP_Input,
        page, link, usage, out raw, preparsed, report, length);
      value = unchecked((int)raw);
      return status == HidOk;
    }

    bool TryTip(IntPtr report, int length, ushort link, out bool down) {
      uint count = (uint)usages.Length;
      int status = Native.HidP_GetUsages(HIDP_REPORT_TYPE.HidP_Input,
        DigitizerPage, link, usages, ref count, preparsed, report, length);
      down = false;
      if (status != HidOk) return false;
      bool confidence = false;
      for (int i = 0; i < count; i++) {
        if (usages[i] == TipSwitch) down = true;
        if (usages[i] == 0x47) confidence = true;
      }
      down = down && confidence;
      return true;
    }

    static VC[] GetValueCaps(IntPtr data, HIDP_CAPS caps, Logger logger) {
      ushort count = caps.NumberInputValueCaps;
      if (count == 0) return new VC[0];
      VC[] values = new VC[count];
      int status = Native.HidP_GetValueCaps(HIDP_REPORT_TYPE.HidP_Input,
        values, ref count, data);
      if (status != HidOk) {
        logger.Error("HidP_GetValueCaps 0x" + status.ToString("X8"));
        return new VC[0];
      }
      VC[] result = new VC[count];
      Array.Copy(values, result, count);
      return result;
    }

    static List<FingerLink> BuildLinks(VC[] values) {
      List<ushort> ids = new List<ushort>();
      foreach (VC cap in values) {
        if (cap.LinkCollection != 0 &&
            (Has(cap, GenericDesktopPage, X) || Has(cap, GenericDesktopPage, Y)) &&
            !ids.Contains(cap.LinkCollection)) ids.Add(cap.LinkCollection);
      }
      ids.Sort();

      List<FingerLink> result = new List<FingerLink>();
      foreach (ushort id in ids) {
        VC? x = null, y = null;
        foreach (VC cap in values) {
          if (cap.LinkCollection == id && Has(cap, GenericDesktopPage, X)) x = cap;
          if (cap.LinkCollection == id && Has(cap, GenericDesktopPage, Y)) y = cap;
        }
        if (x.HasValue && y.HasValue) {
          result.Add(new FingerLink(id, x.Value.LogicalMin, x.Value.LogicalMax,
            y.Value.LogicalMin, y.Value.LogicalMax, x.Value.ReportID,
            PointerSpeedPolicy.PhysicalMillimetres(x.Value.PhysicalMin,x.Value.PhysicalMax,x.Value.Units,x.Value.UnitsExp,PointerSpeedPolicy.ReferenceWidthMm),
            PointerSpeedPolicy.PhysicalMillimetres(y.Value.PhysicalMin,y.Value.PhysicalMax,y.Value.Units,y.Value.UnitsExp,PointerSpeedPolicy.ReferenceHeightMm)));
        }
      }
      return result;
    }

    static bool Has(VC cap, ushort page, ushort usage) {
      if (cap.UsagePage != page) return false;
      return cap.IsRange != 0
        ? usage >= cap.RangeUsageMin && usage <= cap.RangeUsageMax
        : cap.NotRangeUsage == usage;
    }

    static string GetName(IntPtr handle) {
      uint size = 0;
      Native.GetRawInputDeviceInfo(handle, Native.RIDI_DEVICENAME, IntPtr.Zero, ref size);
      if (size == 0) return "(unnamed)";
      IntPtr buffer = Marshal.AllocHGlobal((int)size * 2);
      try {
        return Native.GetRawInputDeviceInfo(handle, Native.RIDI_DEVICENAME, buffer, ref size) == uint.MaxValue
          ? "(name failed)"
          : (Marshal.PtrToStringUni(buffer) ?? string.Empty);
      } finally {
        Marshal.FreeHGlobal(buffer);
      }
    }

    public void Dispose() {
      if (disposed) return;
      disposed = true;
      if (preparsed != IntPtr.Zero) Marshal.FreeHGlobal(preparsed);
      preparsed = IntPtr.Zero;
      Ok = false;
    }
  }

  sealed class FingerLink {
    public readonly ushort Id;
    public readonly int X0, X1, Y0, Y1;
    public readonly byte ReportId;
    public readonly double WidthMm,HeightMm;
    public FingerLink(ushort id, int x0, int x1, int y0, int y1, byte reportId)
      :this(id,x0,x1,y0,y1,reportId,PointerSpeedPolicy.ReferenceWidthMm,PointerSpeedPolicy.ReferenceHeightMm){}
    public FingerLink(ushort id, int x0, int x1, int y0, int y1, byte reportId,double widthMm,double heightMm) {
      Id = id; X0 = x0; X1 = x1; Y0 = y0; Y1 = y1; ReportId = reportId;
      WidthMm=widthMm;HeightMm=heightMm;
    }
  }

  struct TouchContact {
    public readonly int Id, X, Y;
    readonly FingerLink link;
    public TouchContact(int id, int x, int y, FingerLink link) {
      Id = id; X = x; Y = y; this.link = link;
    }
    public double NX { get { return Normalize(X, link.X0, link.X1); } }
    public double NY { get { return Normalize(Y, link.Y0, link.Y1); } }
    public double MotionX {get{return NX*link.WidthMm/PointerSpeedPolicy.ReferenceWidthMm;}}
    public double MotionY {get{return NY*link.HeightMm/PointerSpeedPolicy.ReferenceHeightMm;}}
    static double Normalize(int value, int min, int max) {
      if (max <= min) return value;
      double result = (double)(value - min) / (max - min);
      return result < 0 ? 0 : result > 1 ? 1 : result;
    }
  }

}

using System;
using System.Runtime.InteropServices;
using System.Text;
using OpenTrackpadShared;

namespace ThreeFingerDrag {
  static class Native {
    public const int WM_INPUT = 0x00FF;
    public const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
    public const int WM_SHOW_SETTINGS = 0x8001;
    public const int WM_CANCEL_DRAG = 0x8002;
    public const int WM_EXIT_DRAG = 0x8003;
    public const int WM_RUNTIME_EVENT = 0x8004;
    public const int GIDC_REMOVAL = 2;
    public const int RID_INPUT = 0x10000003;
    public const int RIDI_PREPARSEDDATA = 0x20000005;
    public const int RIDI_DEVICENAME = 0x20000007;
    public const int RIM_TYPEHID = 2;
    public const int RIDEV_INPUTSINK = 0x00000100;
    public const int RIDEV_DEVNOTIFY = 0x00002000;
    const int SM_CXVIRTUALSCREEN = 78;
    const int SM_CYVIRTUALSCREEN = 79;
    const uint INPUT_MOUSE = 0;
    const uint MOUSEEVENTF_MOVE = 1;
    const uint MOUSEEVENTF_LEFTDOWN = 2;
    const uint MOUSEEVENTF_LEFTUP = 4;
    const uint MOUSEEVENTF_MOVE_NOCOALESCE = 0x2000;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint TOKEN_QUERY = 0x0008;
    const int TOKEN_ELEVATION_CLASS = 20;
    const int CURSOR_SHOWING = 0x00000001;
    const int IDC_IBEAM = 32513;
    static readonly int InputSize = Marshal.SizeOf(typeof(INPUT));
    static readonly int CursorInfoSize = Marshal.SizeOf(typeof(CURSORINFO));
    static readonly IntPtr IBeamCursor = LoadCursor(IntPtr.Zero, new IntPtr(IDC_IBEAM));

    public static int ScreenWidth {
      get { return LogicalScreenExtent(GetSystemMetrics(SM_CXVIRTUALSCREEN),GetDpiForSystem()); }
    }
    public static int ScreenHeight {
      get { return LogicalScreenExtent(GetSystemMetrics(SM_CYVIRTUALSCREEN),GetDpiForSystem()); }
    }
    // Keep the established drag gain when the settings UI becomes DPI-aware.
    internal static int LogicalScreenExtent(int pixels,uint dpi){return Math.Max(1,(int)Math.Round(pixels*96.0/(dpi==0?96:dpi)));}
    [DllImport("user32.dll")]static extern uint GetDpiForSystem();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr window,int command);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[] devices, ref uint count, uint size);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);

    public static bool LeftIsDown() { return GetAsyncKeyState(1) < 0; }
    public static string LogonSessionId() {
      IntPtr token;
      if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,out token))return string.Empty;
      try{TOKEN_STATISTICS statistics;int length;
        if(!GetTokenStatistics(token,10,out statistics,Marshal.SizeOf(typeof(TOKEN_STATISTICS)),out length))return string.Empty;
        return statistics.authenticationHigh.ToString("X8")+statistics.authenticationLow.ToString("X8");
      }finally{CloseHandle(token);}
    }
    public static DateTime BootTimeUtc {get{return DateTime.UtcNow.AddMilliseconds(-(double)GetTickCount64());}}
    public static bool ReadSession(out bool locked,out bool connected) {
      locked=true;connected=false;
      if(AppPaths.IsIsolated){locked=false;connected=true;return true;}
      IntPtr data;int bytes;
      if(!WTSQuerySessionInformation(IntPtr.Zero,-1,25,out data,out bytes))return false;
      try {
        if(data==IntPtr.Zero || bytes<20 || Marshal.ReadInt32(data)!=1)return false;
        int state=Marshal.ReadInt32(data,12),flags=Marshal.ReadInt32(data,16);
        if(flags!=0 && flags!=1)return false;
        connected=state==0;locked=flags==0;
        // A secure UAC desktop is not a session unlock; retry once it closes.
        if(connected && !locked && !string.Equals(InputDesktopName(),"Default",StringComparison.OrdinalIgnoreCase))return false;
        return true;
      }finally { WTSFreeMemory(data); }
    }
    [DllImport("wtsapi32.dll",EntryPoint="WTSQuerySessionInformationW",SetLastError=true)]
    static extern bool WTSQuerySessionInformation(IntPtr server,int session,int kind,out IntPtr data,out int bytes);
    [DllImport("wtsapi32.dll")]static extern void WTSFreeMemory(IntPtr data);
    [DllImport("kernel32.dll")]
    static extern ulong GetTickCount64();
    [DllImport("advapi32.dll",EntryPoint="GetTokenInformation",SetLastError=true)]
    static extern bool GetTokenStatistics(IntPtr token,int informationClass,out TOKEN_STATISTICS information,int length,out int returned);
    public static void InitializeLocalComSecurity() {
      int result=CoInitializeSecurity(IntPtr.Zero,-1,IntPtr.Zero,IntPtr.Zero,0,3,IntPtr.Zero,0,IntPtr.Zero);
      if(result<0 && result!=unchecked((int)0x80010119))Marshal.ThrowExceptionForHR(result);
    }
    [DllImport("ole32.dll")]
    static extern int CoInitializeSecurity(IntPtr descriptor,int authServices,IntPtr services,IntPtr reserved,uint authLevel,uint impersonation,IntPtr authList,uint capabilities,IntPtr reserved2);
    public static string InputDesktopName() {
      IntPtr desktop=OpenInputDesktop(0,false,1);
      if(desktop==IntPtr.Zero)return "unavailable";
      try{StringBuilder name=new StringBuilder(128);uint needed;
        return GetUserObjectInformation(desktop,2,name,256,out needed)?name.ToString():"unavailable";
      }finally{CloseDesktop(desktop);}
    }
    [DllImport("user32.dll",SetLastError=true)]
    static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="GetUserObjectInformationW")]
    static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder data,uint length,out uint needed);
    [DllImport("user32.dll")]
    static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(
      [In] RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(
      IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetRawInputDeviceInfo(
      IntPtr device, uint command, IntPtr data, ref uint size);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);

    [DllImport("hid.dll")]
    public static extern int HidP_GetValueCaps(
      HIDP_REPORT_TYPE type, [Out] VC[] values, ref ushort count, IntPtr preparsed);

    [DllImport("hid.dll")]
    public static extern int HidP_GetUsageValue(
      HIDP_REPORT_TYPE type, ushort page, ushort link, ushort usage,
      out uint value, IntPtr preparsed, IntPtr report, int length);

    [DllImport("hid.dll")]
    public static extern int HidP_GetUsages(
      HIDP_REPORT_TYPE type, ushort page, ushort link, [Out] ushort[] usages,
      ref uint count, IntPtr preparsed, IntPtr report, int length);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, ref INPUT input, int size);

    [DllImport("user32.dll")]
    static extern bool GetCursorInfo(ref CURSORINFO cursorInfo);

    [DllImport("user32.dll")]
    static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [DllImport("imm32.dll")]
    static extern bool ImmDisableIME(uint threadId);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentProcessId();

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(
      IntPtr tokenHandle,
      int tokenInformationClass,
      out TOKEN_ELEVATION tokenInformation,
      int tokenInformationLength,
      out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    public static bool MoveBy(int dx, int dy) {
      if(AppPaths.IsIsolated)return true;
      if (dx == 0 && dy == 0) return true;

      INPUT input = new INPUT();
      input.type = INPUT_MOUSE;
      input.mi.dx = dx;
      input.mi.dy = dy;
      input.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE;
      return SendInput(1, ref input, InputSize) == 1;
    }
    public static bool LeftDown() { return Mouse(MOUSEEVENTF_LEFTDOWN); }
    public static bool LeftUp() { return Mouse(MOUSEEVENTF_LEFTUP); }

    static bool Mouse(uint flags) {
      if(AppPaths.IsIsolated)return true;
      INPUT input = new INPUT();
      input.type = INPUT_MOUSE;
      input.mi.dwFlags = flags;
      return SendInput(1, ref input, InputSize) == 1;
    }

    public static bool IsTextSelectionCursor() {
      CURSORINFO info = new CURSORINFO();
      info.cbSize = CursorInfoSize;
      return IBeamCursor != IntPtr.Zero && GetCursorInfo(ref info) &&
        (info.flags & CURSOR_SHOWING) != 0 && info.hCursor == IBeamCursor;
    }

    public static void DisableImeForProcess() {
      ImmDisableIME(uint.MaxValue);
    }

    public static bool IsCurrentProcessElevated() {
      IntPtr token;
      if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out token)) return false;
      try {
        TOKEN_ELEVATION elevation;
        int returned;
        if (!GetTokenInformation(token, TOKEN_ELEVATION_CLASS, out elevation,
            Marshal.SizeOf(typeof(TOKEN_ELEVATION)), out returned)) return false;
        return elevation.TokenIsElevated != 0;
      } finally {
        CloseHandle(token);
      }
    }

    public static bool ForegroundWindowRequiresElevation() {
      if (IsCurrentProcessElevated()) return false;

      IntPtr hwnd = GetForegroundWindow();
      if (hwnd == IntPtr.Zero) return false;

      uint processId;
      GetWindowThreadProcessId(hwnd, out processId);
      if (processId == 0 || processId == GetCurrentProcessId()) return false;

      bool known;
      int error;
      bool elevated = TryIsProcessElevated(processId, out known, out error);
      if (known) return elevated;
      return error == 5;
    }

    static bool TryIsProcessElevated(uint processId, out bool known, out int error) {
      known = false;
      error = 0;
      IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
      if (process == IntPtr.Zero) {
        error = Marshal.GetLastWin32Error();
        return false;
      }

      try {
        IntPtr token;
        if (!OpenProcessToken(process, TOKEN_QUERY, out token)) {
          error = Marshal.GetLastWin32Error();
          return false;
        }

        try {
          TOKEN_ELEVATION elevation;
          int returned;
          if (!GetTokenInformation(token, TOKEN_ELEVATION_CLASS, out elevation,
              Marshal.SizeOf(typeof(TOKEN_ELEVATION)), out returned)) {
            error = Marshal.GetLastWin32Error();
            return false;
          }
          known = true;
          return elevation.TokenIsElevated != 0;
        } finally {
          CloseHandle(token);
        }
      } finally {
        CloseHandle(process);
      }
    }
  }

  enum HIDP_REPORT_TYPE { HidP_Input = 0, HidP_Output = 1, HidP_Feature = 2 }

  [StructLayout(LayoutKind.Sequential)]
  struct RAWINPUTDEVICE {
    public ushort usUsagePage, usUsage;
    public int dwFlags;
    public IntPtr hwndTarget;
  }

  [StructLayout(LayoutKind.Sequential)]
  struct RAWINPUTDEVICELIST {
    public IntPtr hDevice;
    public uint dwType;
  }

  [StructLayout(LayoutKind.Sequential)]
  struct RAWINPUTHEADER {
    public uint dwType, dwSize;
    public IntPtr hDevice, wParam;
  }

  [StructLayout(LayoutKind.Sequential)]
  struct RAWHID {
    public uint dwSizeHid, dwCount;
  }

  [StructLayout(LayoutKind.Sequential)]
  struct HIDP_CAPS {
    public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength,
      FeatureReportByteLength, R0, R1, R2, R3, R4, R5, R6, R7, R8, R9, R10,
      R11, R12, R13, R14, R15, R16, NumberLinkCollectionNodes,
      NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
      NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
      NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
  }

  [StructLayout(LayoutKind.Sequential)]
  struct VC {
    public ushort UsagePage;
    public byte ReportID, IsAlias;
    public ushort BitField, LinkCollection, LinkUsage, LinkUsagePage;
    public byte IsRange, IsStringRange, IsDesignatorRange, IsAbsolute, HasNull, Reserved;
    public ushort BitSize, ReportCount, R0, R1, R2, R3, R4;
    public uint UnitsExp, Units;
    public int LogicalMin, LogicalMax, PhysicalMin, PhysicalMax;
    public ushort RangeUsageMin, RangeUsageMax, RangeStringMin, RangeStringMax,
      RangeDesignatorMin, RangeDesignatorMax, RangeDataIndexMin, RangeDataIndexMax;
    public ushort NotRangeUsage { get { return RangeUsageMin; } }
  }

  [StructLayout(LayoutKind.Sequential)]
  struct POINT { public int X, Y; }

  [StructLayout(LayoutKind.Sequential)]
  struct CURSORINFO {
    public int cbSize, flags;
    public IntPtr hCursor;
    public POINT screenPosition;
  }

  [StructLayout(LayoutKind.Sequential)]
  struct INPUT { public uint type; public MOUSEINPUT mi; }

  [StructLayout(LayoutKind.Sequential)]
  struct MOUSEINPUT {
    public int dx, dy;
    public uint mouseData, dwFlags, time;
    public IntPtr dwExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  struct TOKEN_ELEVATION {
    public int TokenIsElevated;
  }
  [StructLayout(LayoutKind.Sequential)]
  struct TOKEN_STATISTICS {
    public uint tokenLow;public int tokenHigh;
    public uint authenticationLow;public int authenticationHigh;
    public long expiration;
    public int type,impersonation;
    public uint dynamicCharged,dynamicAvailable,groups,privileges,modifiedLow;
    public int modifiedHigh;
  }
}

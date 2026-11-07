using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

namespace ThreeFingerDrag {
  static class GestureDiagnostics {
    static readonly List<string> checks = new List<string>();
    static void Check(bool ok, string name) {
      if (!ok) throw new InvalidOperationException(name);
      checks.Add(name);
    }

    public static void Run(string output,bool requireApple=true) {
      checks.Clear();
      EngineTests();
      ShortcutTests();
      SpeedTests();
      SpeedExperienceTests();
      uint count = 0;
      uint entrySize = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
      Native.GetRawInputDeviceList(null, ref count, entrySize);
      RAWINPUTDEVICELIST[] entries = new RAWINPUTDEVICELIST[count];
      uint received = Native.GetRawInputDeviceList(entries, ref count, entrySize);
      Check(received != uint.MaxValue, "Raw Input device enumeration");
      var devices = new List<object>();
      int appleProfiles = 0;
      int internalProfiles = 0;
      using (Logger logger = new Logger() {SuppressDisk=true}) {
        foreach (RAWINPUTDEVICELIST entry in entries) {
          if (entry.dwType != Native.RIM_TYPEHID) continue;
          using (HidDevice device = HidDevice.Create(entry.hDevice, logger)) {
            if (!device.Ok) continue;
            devices.Add(new { name=device.Name, label=device.DisplayName, reportId=device.ReportId, reportLength=device.ReportLength,widthMm=device.WidthMm,heightMm=device.HeightMm });
            if (device.ReportId == 5 && device.ReportLength == 112 && device.Name.ToUpperInvariant().Contains("0265")) {
              appleProfiles++;
              ParserTests(device);
            }
            if(device.Name.ToUpperInvariant().Contains("GXT7863")) {
              internalProfiles++;
              DescriptorParserTests(entry.hDevice,device);
            }
          }
        }
      }
      if(requireApple || appleProfiles>0)Check(appleProfiles > 0, "Live Magic Trackpad PTP parser profile located");
      var result = new {
        success=true, capturedAt=DateTime.Now.ToString("o"),
        checks=checks.ToArray(), touchpads=devices, appleProfiles=appleProfiles,internalProfiles=internalProfiles,
        skipped=appleProfiles==0?new[]{"Apple Magic Trackpad is offline; live Apple descriptor tests were not run"}:new string[0],
        method="Synthetic reports parsed using actual live device preparsed data; input engine tested with a fake mouse sink",
        mouseEventsInjected=false, physicalGestureTested=false
      };
      File.WriteAllText(output, new JavaScriptSerializer().Serialize(result));
    }

    // Build reports with Windows' HID encoder, independently of the input parser.
    // No reports are sent to the hardware and no system mouse events are injected.
    static void DescriptorParserTests(IntPtr handle,HidDevice device) {
      uint bytes=0;
      Native.GetRawInputDeviceInfo(handle,Native.RIDI_PREPARSEDDATA,IntPtr.Zero,ref bytes);
      IntPtr data=Marshal.AllocHGlobal((int)bytes);
      try {
        Native.GetRawInputDeviceInfo(handle,Native.RIDI_PREPARSEDDATA,data,ref bytes);
        HIDP_CAPS caps;
        RequireHid(Native.HidP_GetCaps(data,out caps));
        ushort count=caps.NumberInputValueCaps;
        VC[] values=new VC[count];RequireHid(Native.HidP_GetValueCaps(HIDP_REPORT_TYPE.HidP_Input,values,ref count,data));
        byte[] three=EncodedFrame(data,values,device,3,0,true,true);
        Check(ParseCount(device,three)==3,"Built-in HID: three valid contacts from the live descriptor");
        Check(ParseCount(device,EncodedFrame(data,values,device,0,0,true,true))==0,"Built-in HID: empty frame releases");
        Check(ParseCount(device,EncodedFrame(data,values,device,3,0,false,true))==0,"Built-in HID: raised fingers do not drag");
        Check(ParseCount(device,EncodedFrame(data,values,device,3,0,true,false))==0,"Built-in HID: palm confidence is respected");
        Check(ParseCount(device,EncodedFrame(data,values,device,4,0,true,true))==4,"Built-in HID: four fingers remain available to Windows");
        Check(ParseCount(device,EncodedFrame(data,values,device,5,0,true,true))==5,"Built-in HID: all five contact slots are parsed");
        bool rejected=false;
        try{ParseCount(device,new byte[]{device.ReportId,0,0});}catch(ArgumentException){rejected=true;}
        Check(rejected,"Built-in HID: short report is rejected");
        byte[] other=new byte[]{(byte)(device.ReportId+1)};
        Check(ParseCount(device,other)==-1,"Built-in HID: unrelated report is ignored");
        using(Logger logger=new Logger {SuppressDisk=true}) {
          FakeMouse mouse=new FakeMouse();
          DragController engine=new DragController(logger,null,mouse,delegate{return DateTime.UtcNow;});
          engine.Handle(ParseCopy(device,three));
          engine.Handle(ParseCopy(device,EncodedFrame(data,values,device,3,60,true,true)));
          Check(engine.IsDown && mouse.Presses==1 && mouse.Moves>0,"Built-in HID: actual descriptor coordinates start a drag");
          engine.Handle(ParseCopy(device,EncodedFrame(data,values,device,4,60,true,true)));
          Check(!engine.IsDown && mouse.Releases==1,"Built-in HID: fourth finger ends dragging before native gestures");
          engine.Handle(ParseCopy(device,three));
          engine.Handle(ParseCopy(device,EncodedFrame(data,values,device,3,60,true,true)));
          engine.Handle(ParseCopy(device,EncodedFrame(data,values,device,0,0,true,true)));
          Check(!engine.IsTracking && mouse.Releases==2,"Built-in HID: lifting fingers reliably releases the drag");
        }
      }finally{Marshal.FreeHGlobal(data);}
    }

    static List<TouchContact> ParseCopy(HidDevice device,byte[] report) {
      IntPtr buffer=Marshal.AllocHGlobal(report.Length);
      try{Marshal.Copy(report,0,buffer,report.Length);return new List<TouchContact>(device.Parse(buffer,report.Length));}
      finally{Marshal.FreeHGlobal(buffer);}
    }

    static byte[] EncodedFrame(IntPtr data,VC[] values,HidDevice device,int count,int delta,bool tip,bool confidence) {
      IntPtr report=Marshal.AllocHGlobal(device.ReportLength);
      try {
        RequireHid(HidP_InitializeReportForID(0,device.ReportId,data,report,device.ReportLength));
        RequireHid(HidP_SetUsageValue(0,13,0,0x54,(uint)count,data,report,device.ReportLength));
        var links=new List<ushort>();
        foreach(VC cap in values)if(cap.UsagePage==1 && cap.NotRangeUsage==0x30 && cap.LinkCollection!=0 && !links.Contains(cap.LinkCollection))links.Add(cap.LinkCollection);
        links.Sort();
        if(count>links.Count)throw new InvalidOperationException("Not enough live HID contact slots");
        for(int i=0;i<count;i++) {
          ushort link=links[i];
          RequireHid(HidP_SetUsageValue(0,13,link,0x51,(uint)(i+1),data,report,device.ReportLength));
          foreach(VC cap in values) {
            if(cap.UsagePage!=1 || cap.LinkCollection!=link || (cap.NotRangeUsage!=0x30 && cap.NotRangeUsage!=0x31))continue;
            int coordinate=cap.LogicalMin+(cap.LogicalMax-cap.LogicalMin)/4+i*(cap.LogicalMax-cap.LogicalMin)/10+delta;
            RequireHid(HidP_SetUsageValue(0,1,link,cap.NotRangeUsage,(uint)coordinate,data,report,device.ReportLength));
          }
          var flags=new List<ushort>();if(tip)flags.Add(0x42);if(confidence)flags.Add(0x47);
          uint flagCount=(uint)flags.Count;
          if(flagCount>0)RequireHid(HidP_SetUsages(0,13,link,flags.ToArray(),ref flagCount,data,report,device.ReportLength));
        }
        byte[] result=new byte[device.ReportLength];Marshal.Copy(report,result,0,result.Length);return result;
      }finally{Marshal.FreeHGlobal(report);}
    }

    static void RequireHid(int status){if(status!=0x00110000)throw new InvalidOperationException("HID encoder status: 0x"+status.ToString("X8"));}
    [DllImport("hid.dll")]static extern int HidP_InitializeReportForID(int type,byte id,IntPtr data,IntPtr report,int length);
    [DllImport("hid.dll")]static extern int HidP_SetUsageValue(int type,ushort page,ushort link,ushort usage,uint value,IntPtr data,IntPtr report,int length);
    [DllImport("hid.dll")]static extern int HidP_SetUsages(int type,ushort page,ushort link,[In]ushort[] usages,ref uint count,IntPtr data,IntPtr report,int length);

    static void ParserTests(HidDevice device) {
      byte[] three = Frame(3);
      Check(ParseCount(device, three) == 3, "Live HID: three valid contacts");
      Check(ParseCount(device, Frame(0)) == 0, "Live HID: empty frame releases immediately");
      byte[] noTips = Frame(3);
      noTips[1]=noTips[22]=noTips[43]=1;
      Check(ParseCount(device, noTips) == 0, "Live HID: cleared TipSwitch is empty");
      byte[] palm = Frame(3); palm[43]=2;
      Check(ParseCount(device, palm) == 2, "Live HID: confidence false rejects palm");
      byte[] origin = Frame(3);
      origin[6]=origin[7]=origin[8]=origin[9]=0;
      Check(ParseCount(device, origin) == 3, "Live HID: coordinate origin remains valid");
      byte[] unrelated = new byte[] {4};
      Check(ParseCount(device, unrelated) == -1, "Live HID: unrelated report ignored");
      bool rejected = false;
      try { ParseCount(device, new byte[] {5,0,0}); } catch (ArgumentException) { rejected=true; }
      Check(rejected, "Live HID: short report rejected");
      byte[] invalid = Frame(0); invalid[108]=6; rejected=false;
      try { ParseCount(device, invalid); } catch (ArgumentException) { rejected=true; }
      Check(rejected, "Live HID: impossible contact count rejected");
      Check(ParseCount(device, Frame(4)) == 4, "Live HID: four-finger frame preserved");
      Check(ParseCount(device, Frame(3)) == 3 && ParseCount(device, Frame(0)) == 0,
        "Live HID: ordered press/release reports");
    }

    static int ParseCount(HidDevice device, byte[] data) {
      IntPtr pointer = Marshal.AllocHGlobal(data.Length);
      try {
        Marshal.Copy(data, 0, pointer, data.Length);
        var contacts = device.Parse(pointer, data.Length);
        return contacts == null ? -1 : contacts.Count;
      } finally { Marshal.FreeHGlobal(pointer); }
    }

    static byte[] Frame(int count) {
      byte[] data = new byte[112]; data[0]=5; data[108]=(byte)count;
      for (int i=0;i<count;i++) {
        int offset=1+i*21; data[offset]=3;
        Buffer.BlockCopy(BitConverter.GetBytes((uint)(10+i)),0,data,offset+1,4);
        Buffer.BlockCopy(BitConverter.GetBytes((ushort)(1000+i*500)),0,data,offset+5,2);
        Buffer.BlockCopy(BitConverter.GetBytes((ushort)(1000+i*500)),0,data,offset+7,2);
      }
      return data;
    }

    static List<TouchContact> Contacts(int count, int delta, bool replace) {
      var result = new List<TouchContact>();
      FingerLink link = new FingerLink(1,0,10000,0,10000,5);
      for(int i=0;i<count;i++) result.Add(new TouchContact(i+(replace ? 10 : 0), 1000+i*600+delta, 1000+i*600+delta,link));
      return result;
    }

    static void EngineTests() {
      DateTime now = DateTime.UtcNow;
      FakeMouse mouse = new FakeMouse();
      using (Logger logger = new Logger() {SuppressDisk=true}) {
        DragController drag = new DragController(logger,null,mouse,delegate{return now;});
        drag.Handle(Contacts(3,0,false));
        Check(!drag.IsDown && mouse.Presses==0,"Stationary three-finger contact does not click");
        drag.Handle(Contacts(3,100,false));
        Check(drag.IsDown && mouse.Presses==1 && mouse.Moves==1,"Three moving fingers start one drag");
        drag.Handle(Contacts(3,200,false));
        Check(mouse.Presses==1 && mouse.Moves==2,"Continuous motion does not press repeatedly");
        drag.Handle(Contacts(4,200,false));
        Check(!drag.IsDown && mouse.Releases==1,"Four fingers release before native gesture");
        drag.Handle(Contacts(3,0,false)); drag.Handle(Contacts(3,100,false));
        drag.Handle(Contacts(3,100,true));
        Check(!drag.IsDown && mouse.Releases==2,"Contact replacement releases and rebases");
        drag.Handle(Contacts(0,0,false));
        mouse.AllowPress=false;
        drag.Handle(Contacts(3,0,false)); drag.Handle(Contacts(3,100,false));
        Check(!drag.IsDown,"Rejected press is not owned");
        mouse.AllowPress=true;
        drag.Handle(Contacts(3,0,false)); drag.Handle(Contacts(3,100,false));
        mouse.AllowRelease=false;
        drag.Handle(Contacts(0,0,false));
        Check(drag.IsDown,"Rejected release remains pending");
        Check(!drag.CanShutdown,"Exit waits while a mouse release is pending");
        mouse.AllowRelease=true; now=now.AddMilliseconds(220); drag.ReleaseIfStale();
        Check(!drag.IsDown,"Pending release is retried");
        Check(drag.CanShutdown,"Exit may complete after the owned button is released");
        drag.Handle(Contacts(3,0,false)); drag.Handle(Contacts(3,100,false));
        now=now.AddMilliseconds(220); drag.ReleaseIfStale();
        Check(!drag.IsTracking,"Missing reports do not leave a held button");
        drag.Handle(Contacts(3,0,false)); drag.Handle(Contacts(3,100,false));
        int moves=mouse.Moves;
        drag.Handle(Contacts(3,8000,false));
        Check(mouse.Moves==moves,"Discontinuous coordinates do not jump");
        drag.ForceRelease("test end");
        mouse.RequiresAdmin=true;
        drag.Handle(Contacts(3,0,false)); drag.Handle(Contacts(3,100,false));
        Check(!drag.IsDown,"Protected window does not create a false press");
      }
      Check(Marshal.SizeOf(typeof(INPUT))==40,"SendInput x64 structure layout");
      Check(Marshal.SizeOf(typeof(VC))==72,"HID value-cap structure layout");
      DateTime configured=DateTime.UtcNow,boot=configured.AddDays(-1);
      Check(!GesturePreferences.CanActivateAfterSignIn(true,"same","same",configured,boot),"Pending configuration stays inactive in the same logon");
      Check(GesturePreferences.CanActivateAfterSignIn(true,"old","new",configured,boot),"New logon activates persisted gestures");
      Check(!GesturePreferences.CanActivateAfterSignIn(true,"","",configured,boot),"Unknown logon does not bypass pending configuration");
      Check(GesturePreferences.CanActivateAfterSignIn(true,"same","same",configured,configured.AddMinutes(1)),"New boot activates persisted gestures");
      Check(GesturePreferences.CanActivateAfterSignIn(false,"same","same",configured,boot),"Verified live preferences activate immediately");
      Check(GesturePreferences.LivePreferencesReady(new GesturePreferences.CacheValues {threeTap=0,threeSlide=0,fourSlide=1}),"Live Settings changes unblock dragging in the same logon");
      Check(!GesturePreferences.LivePreferencesReady(new GesturePreferences.CacheValues {threeTap=1,threeSlide=0,fourSlide=1}),"Native three-finger tap conflict keeps dragging paused");
      Check(!GesturePreferences.LivePreferencesReady(new GesturePreferences.CacheValues {threeTap=0,threeSlide=1,fourSlide=1}),"Native three-finger swipe conflict keeps dragging paused");
      Check(!GesturePreferences.LivePreferencesReady(new GesturePreferences.CacheValues {threeTap=0,threeSlide=0,fourSlide=2}),"Four-finger app switching requires the requested native action");
      Check(!GesturePreferences.LivePreferencesReady(null),"Unavailable live preferences do not bypass the configuration gate");
    }

    static void ShortcutTests() {
      Check(Marshal.SizeOf(typeof(KEYBOARD_INPUT))==40,"Desktop shortcuts: native x64 input layout");
      ShortcutKey[] plan=DesktopKeyPlan.Build(DesktopKeyPlan.Left,true,false);
      Check(plan.Length==4 && plan[0].Key==0x5B && !plan[0].Up && plan[3].Key==0x5B && plan[3].Up,"Desktop shortcuts: held Ctrl is preserved");
      ShortcutKey[] released=DesktopKeyPlan.Build(DesktopKeyPlan.Right,false,false);
      Check(released.Length==6 && released[0].Key==0x11 && !released[0].Up && released[5].Key==0x11 && released[5].Up,"Desktop shortcuts: an already released Ctrl is temporarily restored");
      Check(released[2].Key==0x27 && !released[2].Up && released[3].Key==0x27 && released[3].Up,"Desktop shortcuts: right selects the right desktop");
      ShortcutKey[] heldWindows=DesktopKeyPlan.Build(DesktopKeyPlan.Left,true,true);
      Check(heldWindows.Length==2 && heldWindows[0].Key==0x25 && heldWindows[1].Up,"Desktop shortcuts: user-held Windows key is never released");
      ShortcutKey[] cleanup=DesktopKeyPlan.Cleanup(released,2);
      Check(cleanup.Length==2 && cleanup[0].Key==0x5B && cleanup[1].Key==0x11 && cleanup[0].Up && cleanup[1].Up,"Desktop shortcuts: partial input releases only injected keys");
      Check(DesktopKeyPlan.Cleanup(plan,0).Length==0 && DesktopKeyPlan.Cleanup(plan,plan.Length).Length==0,"Desktop shortcuts: rejected or complete batches have no held keys");
      FakeShortcutKeys keys=new FakeShortcutKeys();keys.Held.Add(0x11);
      DesktopShortcutEngine engine=new DesktopShortcutEngine(keys);
      Check(engine.Switch(DesktopKeyPlan.Left) && engine.SuccessfulRequests==1 && engine.CanShutdown,"Desktop shortcuts: complete input is counted once");
      keys=new FakeShortcutKeys();keys.Results.Enqueue(0);engine=new DesktopShortcutEngine(keys);
      Check(!engine.Switch(DesktopKeyPlan.Right) && engine.SuccessfulRequests==0 && engine.CanShutdown,"Desktop shortcuts: blocked input is not reported as successful");
      keys=new FakeShortcutKeys();keys.Held.Add(0x11);keys.Results.Enqueue(1);keys.Results.Enqueue(0);keys.Results.Enqueue(0);
      engine=new DesktopShortcutEngine(keys);
      Check(!engine.Switch(DesktopKeyPlan.Left) && !engine.CanShutdown,"Desktop shortcuts: failed cleanup defers exit");
      Check(!engine.Switch(DesktopKeyPlan.Right) && keys.Sent.Count==3 && !engine.CanShutdown,"Desktop shortcuts: another switch cannot bypass pending cleanup");
      engine.RetryRelease();
      Check(engine.CanShutdown && keys.Sent[3].Length==1 && keys.Sent[3][0].Key==0x5B && keys.Sent[3][0].Up,"Desktop shortcuts: delayed cleanup preserves physical Ctrl");
      keys=new FakeShortcutKeys();keys.Held.Add(0x10);engine=new DesktopShortcutEngine(keys);
      Check(!engine.Switch(DesktopKeyPlan.Left) && keys.Sent.Count==0,"Desktop shortcuts: Ctrl Shift arrow is not remapped");
      keys.Held.Clear();keys.Held.Add(0x12);
      Check(!engine.Switch(DesktopKeyPlan.Right) && keys.Sent.Count==0,"Desktop shortcuts: Ctrl Alt arrow is not remapped");
    }

    static void SpeedExperienceTests(){
      DateTime now=DateTime.UtcNow;var sent=new List<int>();var draft=new SpeedDraft(delegate(int value){sent.Add(value);});
      draft.Schedule(6,now);draft.Schedule(10,now);draft.Schedule(17,now);
      draft.Tick(now.AddSeconds(2),true);Check(sent.Count==0 && draft.Pending,"Speed draft: live dragging never writes intermediate values");
      draft.Flush();draft.Flush();Check(sent.Count==1 && sent[0]==17 && !draft.Pending,"Speed draft: release commits only the latest value once");
      draft.Schedule(99,now);draft.Tick(now.AddMilliseconds(119),false);Check(sent.Count==1,"Speed draft: accessibility changes wait for idle");draft.Tick(now.AddMilliseconds(120),false);Check(sent.Count==2 && sent[1]==20,"Speed draft: idle commit clamps to the valid range");
      draft.Schedule(1,now);draft.Cancel();draft.Tick(now.AddSeconds(3),false);Check(sent.Count==2 && !draft.Pending,"Speed draft: cancellation never reaches the system");
      int native=16,calls=0,adopted=16;bool fail=false;
      var coordinator=new SpeedCoordinator(16,delegate(int value){calls++;if(fail)throw new InvalidOperationException("simulated");var change=new SpeedChange{Before=native,After=value};native=value;return change;},delegate(int value){adopted=value;});
      Check(!coordinator.Undo() && calls==0,"Speed undo: empty history performs no native call");
      coordinator.Set(6);Check(coordinator.CanUndo && coordinator.UndoTarget==16 && adopted==6,"Speed undo: a change remembers the actual previous system value");
      coordinator.Set(6);Check(coordinator.CanUndo && coordinator.UndoTarget==16,"Speed undo: repeated preset keeps the useful undo target");
      Check(coordinator.Undo() && native==16 && !coordinator.CanUndo,"Speed undo: undo restores once and clears history");
      coordinator.Set(10);native=18;coordinator.Synchronize(18);Check(!coordinator.CanUndo && adopted==18,"Speed undo: external Windows changes invalidate stale history");
      native=19;coordinator.Set(6);Check(coordinator.UndoTarget==19,"Speed undo: external changes between polls are respected");
      fail=true;bool rejected=false;try{coordinator.Set(12);}catch(InvalidOperationException){rejected=true;}Check(rejected && coordinator.Current==6 && coordinator.UndoTarget==19,"Speed undo: failed changes preserve the last working state");
      rejected=false;try{coordinator.Undo();}catch(InvalidOperationException){rejected=true;}Check(rejected && coordinator.CanUndo,"Speed undo: failed undo remains retryable");
      fail=false;Check(coordinator.Undo() && native==19,"Speed undo: retry succeeds without losing the original target");
    }

    static void SpeedTests() {
      Check(Native.LogicalScreenExtent(2880,192)==1440 && Native.LogicalScreenExtent(1800,192)==900,"High DPI UI preserves the established drag distance");
      Check(Native.LogicalScreenExtent(1440,96)==1440 && Native.LogicalScreenExtent(1440,0)==1440,"Screen normalization handles standard and unavailable DPI");
      Check(Marshal.SizeOf(typeof(TouchpadSpeedParameters))==44 && Marshal.OffsetOf(typeof(TouchpadSpeedParameters),"cursorSpeed").ToInt32()==24,"Unified speed: Windows parameter structure layout");
      Check(PointerSpeedPolicy.Clamp(0)==1 && PointerSpeedPolicy.Clamp(30)==20,"Unified speed: valid Windows speed range");
      Check(Math.Abs(PointerSpeedPolicy.DragGain(10)-1)<0.000001 && Math.Abs(PointerSpeedPolicy.DragGain(2)-0.2)<0.000001,"Unified speed: pointer setting controls drag gain");
      bool monotonic=true;for(int i=1;i<20;i++)monotonic &= PointerSpeedPolicy.DragGain(i+1)>PointerSpeedPolicy.DragGain(i);
      Check(monotonic,"Unified speed: higher settings always increase drag speed");
      Check(Math.Abs(PointerSpeedPolicy.PhysicalMillimetres(0,1600,17,14,125)-160)<0.000001,"Unified speed: Apple centimetre units convert to millimetres");
      Check(Math.Abs(PointerSpeedPolicy.PhysicalMillimetres(0,492,19,14,125)-124.968)<0.000001,"Unified speed: built-in inch units convert to millimetres");
      Check(Math.Abs(PointerSpeedPolicy.PhysicalMillimetres(0,321,19,unchecked((uint)-2),80)-81.534)<0.000001,"Unified speed: signed HID exponent is decoded");
      Check(PointerSpeedPolicy.PhysicalMillimetres(0,1000,0,0,125)==125,"Unified speed: missing physical units use a safe fallback");
      Check(PointerSpeedPolicy.PhysicalMillimetres(10,0,17,14,80)==80,"Unified speed: invalid physical extents are rejected");
      FingerLink apple=new FingerLink(1,0,160000,0,114900,5,160,114.9);
      FingerLink builtin=new FingerLink(1,0,124968,0,81534,4,124.968,81.534);
      TouchContact a=new TouchContact(1,5000,5000,apple),b=new TouchContact(1,5000,5000,builtin);
      Check(Math.Abs(a.MotionX-b.MotionX)<0.000001,"Unified speed: equal horizontal millimetres have equal motion");
      Check(Math.Abs(a.MotionY-b.MotionY)<0.000001,"Unified speed: equal vertical millimetres have equal motion");
      using(Logger logger=new Logger {SuppressDisk=true}) {
        FakeMouse first=new FakeMouse(),second=new FakeMouse(),fast=new FakeMouse();
        DragController da=new DragController(logger,null,first,delegate{return DateTime.UtcNow;});
        DragController db=new DragController(logger,null,second,delegate{return DateTime.UtcNow;});
        DragController df=new DragController(logger,null,fast,delegate{return DateTime.UtcNow;});
        da.Scale=db.Scale=PointerSpeedPolicy.DragGain(2);df.Scale=PointerSpeedPolicy.DragGain(10);
        da.Handle(PhysicalContacts(apple,0));db.Handle(PhysicalContacts(builtin,0));df.Handle(PhysicalContacts(apple,0));
        da.Handle(PhysicalContacts(apple,5000));db.Handle(PhysicalContacts(builtin,5000));df.Handle(PhysicalContacts(apple,5000));
        Check(first.TotalX==second.TotalX && first.TotalY==second.TotalY && first.Moves>0,"Unified speed: both pads produce identical pixels for equal physical travel");
        Check(fast.TotalX>first.TotalX && fast.TotalY>first.TotalY,"Unified speed: changing the shared speed changes actual drag output");
        da.ForceRelease("speed test");db.ForceRelease("speed test");df.ForceRelease("speed test");
        Check(da.CanShutdown && db.CanShutdown && df.CanShutdown,"Unified speed: calibration preserves normal drag release");
      }
    }

    static List<TouchContact> PhysicalContacts(FingerLink link,int distance) {
      var result=new List<TouchContact>();
      for(int i=0;i<3;i++)result.Add(new TouchContact(i,10000+i*5000+distance,10000+i*5000+distance,link));
      return result;
    }

    sealed class FakeShortcutKeys : IShortcutKeys {
      public readonly HashSet<int> Held=new HashSet<int>();
      public readonly Queue<int> Results=new Queue<int>();
      public readonly List<ShortcutKey[]> Sent=new List<ShortcutKey[]>();
      public bool IsDown(int key){return Held.Contains(key);}
      public int Send(ShortcutKey[] keys){Sent.Add(keys);return Results.Count>0?Results.Dequeue():keys.Length;}
    }

    sealed class FakeMouse : IDragMouse {
      public bool AllowPress=true,AllowRelease=true,RequiresAdmin;
      public int Presses,Moves,Releases,TotalX,TotalY;
      public bool RequiresElevation {get{return RequiresAdmin;}}
      public bool PhysicalLeftDown {get{return false;}}
      public int Width {get{return 1920;}}
      public int Height {get{return 1080;}}
      public bool Press(){if(!AllowPress)return false;Presses++;return true;}
      public bool Move(int x,int y){Moves++;TotalX+=x;TotalY+=y;return true;}
      public bool Release(){if(!AllowRelease)return false;Releases++;return true;}
    }
  }
}

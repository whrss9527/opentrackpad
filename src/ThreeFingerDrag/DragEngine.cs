// Local changes for Open Trackpad. Upstream input parser: yuyisheshen/ThreeFingerDrag-Windows (MIT).
using System;
using System.Collections.Generic;

namespace ThreeFingerDrag {
  interface IDragMouse {
    bool RequiresElevation { get; }
    bool PhysicalLeftDown { get; }
    int Width { get; }
    int Height { get; }
    bool Press();
    bool Move(int x, int y);
    bool Release();
  }

  sealed class SystemDragMouse : IDragMouse {
    public bool RequiresElevation { get { return Native.ForegroundWindowRequiresElevation(); } }
    public bool PhysicalLeftDown { get { return Native.LeftIsDown(); } }
    public int Width { get { return Native.ScreenWidth; } }
    public int Height { get { return Native.ScreenHeight; } }
    public bool Press() { return Native.LeftDown(); }
    public bool Move(int x, int y) { return Native.MoveBy(x, y); }
    public bool Release() { return Native.LeftUp(); }
  }

  sealed class DragController {
    readonly Logger logger;
    readonly Func<bool> adminPrompt;
    readonly IDragMouse mouse;
    readonly Func<DateTime> clock;
    readonly HashSet<int> ids = new HashSet<int>();
    readonly HashSet<int> nextIds = new HashSet<int>();
    bool down, armed, releasePending;
    DateTime last;
    double anchorX, anchorY, previousX, previousY, carryX, carryY;
    int width, height;
    const double ActivationDistance = 0.0025;
    const double MaximumStep = 0.20;
    public double Scale = 1.0;
    public bool IsDown { get { return down; } }
    public bool IsTracking { get { return down || armed || releasePending; } }
    public bool CanShutdown { get { return !down; } }
    public int DragStarts { get; private set; }
    public int DragReleases { get; private set; }

    public DragController(Logger logger, Func<bool> adminPrompt)
      : this(logger, adminPrompt, new SystemDragMouse(), delegate { return DateTime.UtcNow; }) { }

    public DragController(Logger logger, Func<bool> adminPrompt, IDragMouse mouse, Func<DateTime> clock) {
      this.logger = logger; this.adminPrompt = adminPrompt; this.mouse = mouse; this.clock = clock;
    }

    public void Handle(List<TouchContact> contacts) {
      last = clock();
      if (releasePending) { Release("retry before new gesture"); return; }
      if (contacts.Count != 3) { Release("contacts=" + contacts.Count); return; }
      nextIds.Clear();
      double x = 0, y = 0;
      foreach (TouchContact contact in contacts) { nextIds.Add(contact.Id); x += contact.MotionX; y += contact.MotionY; }
      if (nextIds.Count != 3) { Release("duplicate contact ids"); return; }
      x /= 3; y /= 3;
      if (!armed && !down) { Arm(x, y); return; }
      if (!ids.SetEquals(nextIds)) {
        Release("contact replacement");
        if (!down) Arm(x, y);
        return;
      }
      double dx = x - previousX, dy = y - previousY;
      previousX = x; previousY = y;
      if (Math.Abs(dx) > MaximumStep || Math.Abs(dy) > MaximumStep) {
        carryX = carryY = 0;
        anchorX = x; anchorY = y;
        logger.Info("Discarded discontinuous touch movement");
        return;
      }
      if (!down) {
        dx = x - anchorX; dy = y - anchorY;
        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < ActivationDistance) return;
        if (mouse.RequiresElevation) {
          if (adminPrompt != null) adminPrompt();
          armed = false;
          return;
        }
        if (mouse.PhysicalLeftDown) { armed = false; return; }
        if (!mouse.Press()) { armed = false; logger.Error("Mouse press was rejected"); return; }
        down = true; DragStarts++;
        width = Math.Max(1, mouse.Width); height = Math.Max(1, mouse.Height);
        logger.Info("Three-finger drag started");
      }
      double moveX = dx * width * Scale + carryX, moveY = dy * height * Scale + carryY;
      int ix = (int)Math.Round(moveX), iy = (int)Math.Round(moveY);
      carryX = moveX - ix; carryY = moveY - iy;
      if ((ix != 0 || iy != 0) && !mouse.Move(ix, iy)) {
        logger.Error("Mouse movement was rejected; releasing owned button");
        Release("move rejected");
      }
    }

    void Arm(double x, double y) {
      armed = true; anchorX = previousX = x; anchorY = previousY = y;
      carryX = carryY = 0; ids.Clear();
      foreach (int id in nextIds) ids.Add(id);
    }

    public void ReleaseIfStale() {
      if (releasePending || (IsTracking && (clock() - last).TotalMilliseconds > 180)) Release("input timeout");
    }

    public void ForceRelease(string reason) { Release(reason); }

    void Release(string reason) {
      armed = false; ids.Clear(); carryX = carryY = 0;
      if (!down) { releasePending = false; return; }
      if (!mouse.Release()) {
        if (!releasePending) logger.Error("Mouse release was rejected; will retry");
        releasePending = true;
        return;
      }
      down = false; releasePending = false; DragReleases++;
      logger.Info("Drag released: " + reason);
    }
  }
}

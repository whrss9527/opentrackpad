using System;

namespace ThreeFingerDrag {
  sealed class SpeedChange {
    public int Before,After;
    public bool Changed {get{return Before!=After;}}
  }

  // Group a pointer/keyboard gesture into one system write. Accessibility value
  // changes also coalesce, but still commit after a short idle period.
  sealed class SpeedDraft {
    readonly Action<int> commit;
    int value;bool pending;DateTime due;
    public SpeedDraft(Action<int> commit){this.commit=commit;}
    public bool Pending {get{return pending;}}
    public void Schedule(int value,DateTime now){this.value=PointerSpeedPolicy.Clamp(value);pending=true;due=now.AddMilliseconds(120);}
    public void Tick(DateTime now,bool interacting){if(pending && !interacting && now>=due)Flush();}
    public void Flush(){if(!pending)return;int latest=value;pending=false;commit(latest);}
    public void Cancel(){pending=false;}
  }

  sealed class SpeedCoordinator {
    readonly Func<int,SpeedChange> apply;
    readonly Action<int> adopted;
    int previous;
    public int Current {get;private set;}
    public bool CanUndo {get;private set;}
    public int UndoTarget {get{return previous;}}
    public SpeedCoordinator(int initial,Func<int,SpeedChange> apply,Action<int> adopted){Current=PointerSpeedPolicy.Clamp(initial);this.apply=apply;this.adopted=adopted;}
    public SpeedChange Set(int value){
      SpeedChange result=apply(PointerSpeedPolicy.Clamp(value));
      if(result.Changed){previous=result.Before;CanUndo=true;}
      else if(Current!=result.After)CanUndo=false;
      Current=result.After;adopted(Current);return result;
    }
    public bool Undo(){if(!CanUndo)return false;SpeedChange result=apply(previous);Current=result.After;CanUndo=false;adopted(Current);return true;}
    public bool Synchronize(int value){value=PointerSpeedPolicy.Clamp(value);if(value==Current)return false;Current=value;CanUndo=false;adopted(Current);return true;}
  }
}

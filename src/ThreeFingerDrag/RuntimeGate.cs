namespace ThreeFingerDrag {
  enum RuntimeEvent { Lock=1,Unlock=2,Disconnect=3,Connect=4,Logoff=5,Suspend=6,Resume=7,Refresh=8 }
  sealed class RuntimeGate {
    bool lockEvent,disconnectEvent;
    public bool Known {get;private set;}
    public bool Locked {get;private set;}
    public RuntimeGate() { Locked=true; }
    public bool Connected {get;private set;}
    public bool Suspended {get;private set;}
    public bool Paused {get{return !Known || Locked || !Connected || Suspended;}}
    public bool ExplicitlyBlocked {get{return lockEvent || disconnectEvent || Suspended;}}
    public void Snapshot(bool known,bool locked,bool connected) {
      Known=known;if(known){Locked=locked || lockEvent;Connected=connected && !disconnectEvent;}
    }
    public void Apply(RuntimeEvent value) {
      switch(value) {
        case RuntimeEvent.Lock:lockEvent=true;Known=true;Locked=true;break;
        case RuntimeEvent.Unlock:lockEvent=false;Locked=false;break;
        case RuntimeEvent.Disconnect:disconnectEvent=true;Known=true;Connected=false;break;
        case RuntimeEvent.Connect:disconnectEvent=false;Connected=true;break;
        case RuntimeEvent.Logoff:lockEvent=true;disconnectEvent=true;Known=true;Locked=true;Connected=false;break;
        case RuntimeEvent.Suspend:Suspended=true;break;
        case RuntimeEvent.Resume:Suspended=false;break;
      }
    }
  }
  sealed class RuntimeRetry {
    int interval=5000;
    public void Reset(){interval=5000;}
    public int Next(){int result=interval;interval=System.Math.Min(60000,interval*2);return result;}
  }
}

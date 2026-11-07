using System;
using System.Collections.Generic;
using System.Threading;
using OpenTrackpadShared;

namespace ThreeFingerDrag {
  static class SystemSettingOperation {
    static int active;
    public static T Run<T>(Func<T> operation) {
      if(Interlocked.CompareExchange(ref active,1,0)!=0)throw new InvalidOperationException("正在应用另一项输入设置，请稍后再试。");
      try{return AtomicJson.WithLock(AppPaths.StateFile("system-input-operation"),operation);}
      finally{Interlocked.Exchange(ref active,0);}
    }
  }
  static class GestureTransaction {
    public static void RestoreAll(IEnumerable<Action> operations) {
      var failures=new List<Exception>();
      foreach(Action operation in operations)try{operation();}catch(Exception failure){failures.Add(failure);}
      if(failures.Count>0)throw new AggregateException("部分设置仍需在 Windows 触摸板设置中确认。",failures);
    }
    public static T Run<T>(Func<T> mutation,Action restore) {
      try{return mutation();}
      catch(Exception failure) {
        try{restore();}
        catch(Exception rollback){throw new InvalidOperationException("操作未完成，部分 Windows 手势可能已更改；原始备份已保留，请到 Windows 触摸板设置检查。",new AggregateException(failure,rollback));}
        throw new InvalidOperationException("操作未完成，已恢复本次操作前的 Windows 手势设置。",failure);
      }
    }
  }
}

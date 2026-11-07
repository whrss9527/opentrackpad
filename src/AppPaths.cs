using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using System.Threading;
using System.Security.Cryptography;

namespace OpenTrackpadShared {
  public static class AppPaths {
    public static string ProgramDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }
    public static bool IsIsolated { get { return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENTRACKPAD_DATA_DIR")); } }
    public static string DataDirectory {
      get {
        string selected=Environment.GetEnvironmentVariable("OPENTRACKPAD_DATA_DIR");
        return Path.GetFullPath(string.IsNullOrWhiteSpace(selected)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenTrackpad"):selected);
      }
    }
    public static string StateDirectory { get { return Path.Combine(DataDirectory,"state"); } }
    public static string LogDirectory { get { return Path.Combine(DataDirectory,"logs"); } }
    public static string StateFile(string name) {
      if(string.IsNullOrEmpty(name) || name!=Path.GetFileName(name) || name=="." || name=="..")throw new ArgumentException("Expected a state filename", "name");
      return Path.Combine(StateDirectory,name);
    }
    public static string InstanceName(string suffix) {
      return "Local\\OpenTrackpad."+suffix+(IsIsolated?".profile."+ProfileId(DataDirectory):string.Empty);
    }
    internal static string ProfileId(string path) {
      ulong hash=14695981039346656037UL;
      foreach(char value in Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()) {
        hash^=(byte)value;hash=unchecked(hash*1099511628211UL);
        hash^=(byte)(value>>8);hash=unchecked(hash*1099511628211UL);
      }
      return hash.ToString("x16");
    }
    static readonly string[] LegacyFiles={"gesture-options.json","window-placement.json","gesture-configuration.json","gestures-before.json","gesture-effective-before.json","pointer-speed-before.json"};
    public static int MigrateLegacyState() {
      if(IsIsolated)return 0;
      var candidates=new List<string>();
      string explicitRoot=Environment.GetEnvironmentVariable("OPENTRACKPAD_LEGACY_DIR");
      if(!string.IsNullOrWhiteSpace(explicitRoot))candidates.Add(explicitRoot);
      candidates.Add(ProgramDirectory);
      try {
        using(RegistryKey run=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
          string command=run==null?null:run.GetValue("OpenTrackpadThreeFingerDrag") as string;
          if(command!=null && command.StartsWith("\"")) {
            int end=command.IndexOf('"',1);
            if(end>1 && command.Substring(end+1).Trim()=="--background") {
              string binary=command.Substring(1,end-1);
              string file=Path.GetFileName(binary);
              if(string.Equals(file,"ThreeFingerDrag.exe",StringComparison.OrdinalIgnoreCase) || string.Equals(file,"OpenTrackpad.exe",StringComparison.OrdinalIgnoreCase))candidates.Add(Path.GetDirectoryName(binary));
            }
          }
        }
      }catch { }
      int copied=0;
      foreach(string candidate in candidates) {
        try { copied+=ImportLegacyState(candidate,DataDirectory); } catch { }
      }
      return copied;
    }
    internal static int ImportLegacyState(string legacyRoot,string dataRoot) {
      string source=Path.GetFullPath(Path.Combine(legacyRoot,"state"));
      string destination=Path.GetFullPath(Path.Combine(dataRoot,"state"));
      if(string.Equals(source.TrimEnd('\\'),destination.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase) || !Directory.Exists(source))return 0;
      if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)return 0;
      int copied=0;
      foreach(string name in LegacyFiles) {
        string original=Path.Combine(source,name),target=Path.Combine(destination,name);
        if(File.Exists(target) || File.Exists(AtomicJson.RecoveryPath(target)))continue;
        string actual=File.Exists(original)?original:AtomicJson.RecoveryPath(original);
        if(!File.Exists(actual) || (File.GetAttributes(actual)&FileAttributes.ReparsePoint)!=0)continue;
        JsonRead data=AtomicJson.Read(original,null);
        if(data.Value==null)continue;
        bool imported=AtomicJson.WithLock(target,delegate {
          if(File.Exists(target) || File.Exists(AtomicJson.RecoveryPath(target)))return false;
          AtomicJson.Write(target,data.Value);return true;
        });
        if(imported)copied++;
      }
      // These records depend on the original backup directory. Archive the
      // metadata separately rather than pretending the new package can roll back.
      string retained=Path.Combine(destination,"legacy-driver-recovery.json");
      if(!File.Exists(retained) && !File.Exists(AtomicJson.RecoveryPath(retained))) {
        var records=new Dictionary<string,object>();
        foreach(string name in new[]{"baseline.json","installation-result.json"}) {
          string original=Path.Combine(source,name);
          string actual=File.Exists(original)?original:AtomicJson.RecoveryPath(original);
          if(!File.Exists(actual) || (File.GetAttributes(actual)&FileAttributes.ReparsePoint)!=0)continue;
          JsonRead data=AtomicJson.Read(original,null);if(data.Value!=null)records[name]=data.Value;
        }
        if(records.Count>0) {
          bool imported=AtomicJson.WithLock(retained,delegate {
            if(File.Exists(retained) || File.Exists(AtomicJson.RecoveryPath(retained)))return false;
            AtomicJson.Write(retained,new {legacyDirectory=Path.GetFullPath(legacyRoot),capturedAt=DateTime.UtcNow.ToString("o"),metadata=records,backupsCopied=false,rollbackAvailableInNewPackage=false});return true;
          });
          if(imported)copied++;
        }
      }
      return copied;
    }
  }

  public sealed class JsonRead {
    public object Value;
    public bool Found,Recovered;
    public bool Unreadable { get { return Found && Value==null; } }
  }

  public static class AtomicJson {
    const long MaximumBytes=2*1024*1024;
    public static string RecoveryPath(string path) { return Path.ChangeExtension(path,"recovery.json"); }
    public static T WithLock<T>(string path,Func<T> operation) {
      string normalized=Path.GetFullPath(path).ToUpperInvariant();string name;
      using(var hash=SHA256.Create())name="Global\\OpenTrackpad.Json."+BitConverter.ToString(hash.ComputeHash(Encoding.Unicode.GetBytes(normalized))).Replace("-","");
      using(var mutex=new Mutex(false,name)) {
        bool acquired=false;
        try {
          try{acquired=mutex.WaitOne(1000);}catch(AbandonedMutexException){acquired=true;}
          if(!acquired)throw new IOException("Settings are busy; retry after the other operation completes");
          return operation();
        }finally {if(acquired)mutex.ReleaseMutex();}
      }
    }
    public static JsonRead Read(string path,Func<object,bool> validator) {
      var result=new JsonRead { Found=File.Exists(path) || File.Exists(RecoveryPath(path)) };
      try {return WithLock(path,delegate {
        foreach(string candidate in new[]{path,RecoveryPath(path)}) {
          try {
            if(!File.Exists(candidate) || new FileInfo(candidate).Length>MaximumBytes)continue;
            object value=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(candidate,new UTF8Encoding(false,true)));
            if(value==null || !(value is Dictionary<string,object> || value is object[]) || validator!=null && !validator(value))continue;
            result.Value=value;result.Recovered=candidate!=path;return result;
          }catch { }
        }
        return result;
      });}catch {result.Found=true;}
      return result;
    }
    public static void Write(string path,object value) {
      string json=new JavaScriptSerializer().Serialize(value);
      if(Encoding.UTF8.GetByteCount(json)>MaximumBytes)throw new InvalidDataException("Settings exceed the supported size");
      WithLock(path,delegate {
        string full=Path.GetFullPath(path),recovery=RecoveryPath(full);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        string primaryTemp=Stage(full,json),recoveryTemp=null,retained=recovery+"."+Guid.NewGuid().ToString("N")+".rollback";
        bool mirrorCommitted=false,hadMirror=File.Exists(recovery);
        try {
          recoveryTemp=Stage(recovery,json);
          if(hadMirror)File.Replace(recoveryTemp,recovery,retained);else File.Move(recoveryTemp,recovery);
          mirrorCommitted=true;
          if(File.Exists(full))File.Replace(primaryTemp,full,null);else File.Move(primaryTemp,full);
          DeleteTemporary(retained);
        }catch(Exception failure) {
          if(mirrorCommitted) {
            try {
              if(hadMirror)File.Replace(retained,recovery,null);else File.Delete(recovery);
            }catch(Exception rollback) {throw new IOException("Settings commit failed and the recovery rollback needs attention; the retained .rollback file was preserved",new AggregateException(failure,rollback));}
          }
          throw;
        }finally {DeleteTemporary(primaryTemp);if(recoveryTemp!=null)DeleteTemporary(recoveryTemp);}
        return true;
      });
    }
    public static void PreserveDamaged(string path) {
      WithLock(path,delegate {
        if(!File.Exists(path))return false;
        string retained=Path.ChangeExtension(path,"damaged-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".json");
        File.Copy(path,retained,false);return true;
      });
    }
    static string Stage(string path,string text) {
      string full=Path.GetFullPath(path);
      string temporary=full+"."+Guid.NewGuid().ToString("N")+".tmp";
      try {
        using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
          byte[] bytes=new UTF8Encoding(false).GetBytes(text);stream.Write(bytes,0,bytes.Length);stream.Flush(true);
        }
        return temporary;
      }catch {DeleteTemporary(temporary);throw;}
    }
    static void DeleteTemporary(string path) {try{if(File.Exists(path))File.Delete(path);}catch{}}
  }
}

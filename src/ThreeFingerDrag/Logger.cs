using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenTrackpadShared;

namespace ThreeFingerDrag {
  sealed class Logger : IDisposable {
    readonly object gate = new object();
    readonly Queue<string> lines = new Queue<string>();
    readonly string directoryPath;
    readonly string filePath;
    const int MaxLines = 300;
    const long MaxFileBytes = 512 * 1024;
    public bool Verbose;
    public bool SuppressDisk;
    public event Action<string> LineAdded;

    public Logger() {
      directoryPath = AppPaths.LogDirectory;
      filePath = Path.Combine(directoryPath, "ThreeFingerDrag.log");
    }

    public void Info(string message) { Add("INFO", message, false); }
    public void Error(string message) { Add("ERROR", message, true); }
    public void Diagnostic(string message) {
      if (Verbose) Add("DIAG", message, true);
    }

    void Add(string level, string message, bool writeFile) {
      string line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + level + "] " + message;
      lock (gate) {
        lines.Enqueue(line);
        while (lines.Count > MaxLines) lines.Dequeue();
      }
      if (writeFile && !SuppressDisk) WriteFile(line);
      Action<string> handler = LineAdded;
      if(handler!=null)foreach(Action<string> subscriber in handler.GetInvocationList())try{subscriber(line);}catch{ /* A closing diagnostics view must not break input cleanup. */ }
    }

    void WriteFile(string line) {
      try {
        Directory.CreateDirectory(directoryPath);
        if (File.Exists(filePath) && new FileInfo(filePath).Length > MaxFileBytes) {
          File.WriteAllText(filePath, string.Empty, Encoding.UTF8);
        }
        File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
      } catch { }
    }

    public string Snapshot() {
      lock (gate) {
        return string.Join(Environment.NewLine, lines.ToArray());
      }
    }

    public void OpenDirectory() {
      Directory.CreateDirectory(directoryPath);
      System.Diagnostics.Process.Start(directoryPath);
    }

    public void Dispose() { LineAdded = null; }
  }
}

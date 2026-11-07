using System;
using System.Collections.Generic;
using System.IO;

namespace ThreeFingerDrag {
  sealed class CliOptions {
    public string[] Arguments;
    public bool Preview;
    public static bool TryParse(string[] raw,out CliOptions result) {
      result=null;if(raw==null)return false;
      var values=new List<string>();bool preview=false;
      foreach(string value in raw) {
        if(value=="--preview"){if(preview)return false;preview=true;}
        else values.Add(value);
      }
      string[] args=values.ToArray();
      if(args.Length==0){result=new CliOptions{Arguments=args,Preview=preview};return true;}
      string verb=args[0];bool valid=false,render=false;
      switch(verb) {
        case "--background":case "--relaunch":case "--shutdown":valid=args.Length==1;break;
        case "--render":case "--render-diagnostics":case "--render-appearance":case "--render-compact":render=true;goto case "--self-test-runtime";
        case "--self-test-runtime":case "--test-speed-response":case "--query-speed":case "--restore-speed":case "--test-speed":case "--test-speed-undo":case "--configure":case "--restore":case "--query-config":case "--self-test":case "--self-test-available":
          valid=args.Length==2 && ValidOutput(args[1]);break;
        case "--set-speed":
          int speed;valid=args.Length==3 && int.TryParse(args[1],out speed) && speed>=1 && speed<=20 && ValidOutput(args[2]);break;
      }
      if(!valid)return false;
      result=new CliOptions{Arguments=args,Preview=preview || render};return true;
    }
    static bool ValidOutput(string value) {
      if(string.IsNullOrWhiteSpace(value) || value.StartsWith("--",StringComparison.Ordinal))return false;
      try {return !string.IsNullOrEmpty(Path.GetFileName(Path.GetFullPath(value)));}catch{return false;}
    }
  }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using OpenTrackpadShared;

namespace OpenTrackpadUi {
  sealed class AppearanceSettings {
    public string Theme = "system";
    public string Font = "auto";
    public AppearanceSettings Copy() { return new AppearanceSettings { Theme = Theme, Font = Font }; }
  }

  sealed class FontChoice {
    public readonly string Key, Label, Family;
    public FontChoice(string key, string label, string family) { Key = key; Label = label; Family = family; }
  }

  static class UiPreferences {
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static readonly List<FontChoice> fonts = BuildFonts();
    public static string LoadWarning = "";
    static Dictionary<string,object> preserved = new Dictionary<string,object>();
    static AppearanceSettings current = Load();
    static bool dark = ResolveDark(current.Theme);
    public static AppearanceSettings Current { get { return current.Copy(); } }
    public static IList<FontChoice> Fonts { get { return fonts.AsReadOnly(); } }
    public static bool Dark { get { return dark; } }
    public static string FontFamily {
      get { foreach (var choice in fonts) if (choice.Key == current.Font) return choice.Family; return fonts[0].Family; }
    }

    static bool Available(string family) {
      try { using (var face = new FontFamily(family)) return face.IsStyleAvailable(FontStyle.Regular); }
      catch (ArgumentException) { return false; }
    }
    static string First(params string[] families) {
      foreach (string family in families) if (Available(family)) return family;
      return SystemFonts.MessageBoxFont.FontFamily.Name;
    }
    static List<FontChoice> BuildFonts() {
      string automatic = First("PingFang SC", "Noto Sans SC", "Microsoft YaHei UI", "Segoe UI");
      var list = new List<FontChoice>();
      list.Add(new FontChoice("auto", "自动（推荐）", automatic));
      list.Add(new FontChoice("system", "Windows 默认", First("Microsoft YaHei UI", "Segoe UI")));
      string[,] candidates = {
        { "pingfang", "苹方", "PingFang SC" }, { "noto-sans", "Noto Sans", "Noto Sans SC" },
        { "yahei", "微软雅黑", "Microsoft YaHei UI" }, { "jhenghei", "微软正黑体", "Microsoft JhengHei UI" },
        { "dengxian", "等线", "DengXian" }, { "noto-serif", "Noto Serif", "Noto Serif SC" }
      };
      for (int index = 0; index < candidates.GetLength(0); index++)
        if (Available(candidates[index, 2])) list.Add(new FontChoice(candidates[index, 0], candidates[index, 1], candidates[index, 2]));
      return list;
    }
    static AppearanceSettings Load() {
      var value = new AppearanceSettings();
      try {
        string path = AppPaths.StateFile("appearance.json");
        JsonRead result=AtomicJson.Read(path,delegate(object candidate){return candidate is Dictionary<string,object>;});
        var data=result.Value as Dictionary<string,object>;
        if(data==null){if(result.Unreadable)LoadWarning="外观设置暂时无法读取，已使用默认外观；原文件保留。";return value;}
        preserved=new Dictionary<string,object>(data);
        if(result.Recovered)LoadWarning="已使用外观恢复副本；保存时会修复主文件并保留原文件。";
        object theme, font;
        if (data != null && data.TryGetValue("theme", out theme) && (Convert.ToString(theme) == "light" || Convert.ToString(theme) == "dark")) value.Theme = Convert.ToString(theme);
        if (data != null && data.TryGetValue("font", out font)) {
          string key = Convert.ToString(font);
          foreach (var choice in fonts) if (choice.Key == key) value.Font = key;
        }
      } catch (Exception) { LoadWarning = "外观设置暂时无法读取，已使用默认外观；原文件保留。"; }
      return value;
    }
    static bool ResolveDark(string theme) {
      if (theme == "dark") return true;
      if (theme == "light") return false;
      try {
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
          return key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
      } catch { return false; }
    }
    public static void Preview(AppearanceSettings value) {
      current = value.Copy();
      dark = ResolveDark(current.Theme);
    }
    public static void Reload(){LoadWarning="";Preview(Load());}
    public static void RefreshSystemTheme() { dark = ResolveDark(current.Theme); }
    public static void Save(AppearanceSettings value) {
      string path = AppPaths.StateFile("appearance.json");
      JsonRead existing=AtomicJson.Read(path,delegate(object candidate){return candidate is Dictionary<string,object>;});
      var data=existing.Value as Dictionary<string,object>;
      var next=data==null?new Dictionary<string,object>(preserved):new Dictionary<string,object>(data);
      next["version"]=1;next["theme"]=value.Theme;next["font"]=value.Font;
      if(existing.Unreadable || existing.Recovered)AtomicJson.PreserveDamaged(path);
      AtomicJson.Write(path,next);
      preserved=next;
      Preview(value);
      LoadWarning="";
    }
  }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using ThreeFingerDrag;

// This entry point deliberately bypasses Program/Main and AppController.
// The selected suites use fake input sinks and never install hooks or write Windows settings.
static class CoreTests {
    [STAThread]
    static int Main(string[] args) {
        if (args.Length != 1) return 2;
        var json = new JavaScriptSerializer();
        try {
            Type diagnostics = typeof(GestureDiagnostics);
            var checks = (List<string>)diagnostics.GetField("checks", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            checks.Clear();
            foreach (string suite in new[] { "EngineTests", "ShortcutTests", "SpeedTests", "SpeedExperienceTests" }) {
                MethodInfo method = diagnostics.GetMethod(suite, BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null) throw new InvalidOperationException("Missing deterministic suite: " + suite);
                method.Invoke(null, null);
            }
            if (checks.Count < 40) throw new InvalidOperationException("The deterministic suite did not execute all expected checks.");
            File.WriteAllText(args[0], json.Serialize(new { success=true, checks=checks, hardwareRequired=false, windowsSettingsModified=false, globalInputInjected=false }));
            return 0;
        } catch (Exception error) {
            if (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            File.WriteAllText(args[0], json.Serialize(new { success=false, error=error.ToString() }));
            return 1;
        }
    }
}

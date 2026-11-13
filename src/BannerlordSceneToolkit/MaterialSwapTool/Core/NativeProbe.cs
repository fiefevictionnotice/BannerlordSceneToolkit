using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MaterialSwapTool.Core
{
    // CRASH LEDGER for engine calls that have not yet been proven safe in the editor (2026-11-12).
    //
    // Why this exists: the first live run of Scene Statistics took the editor down inside its
    // scan. A native crash is not a managed exception - try/catch never sees it, nothing is
    // logged, and the crash dump was cancelled, so there was no way to tell which of the half
    // dozen new engine calls did it.
    //
    // How it works: each group of unproven calls runs inside Run("name", ...). Before it starts,
    // "trying|name" is appended to a small file and flushed; when it returns, "ok|name" is
    // appended. If the editor dies in between, the file still ends on "trying" for that name.
    // The next time the toolkit loads it reads that as "this call crashed the editor", marks
    // the name "crashed", and Run() skips it from then on. So a bad call costs ONE crash, names
    // itself in the file, and the feature keeps working without it.
    //
    // The ledger is Documents\Mount and Blade II Bannerlord\MaterialSwapTool\native_probe_ledger.txt.
    // Deleting it (or the "crashed|..." line) lets a skipped call be tried again.
    public static class NativeProbe
    {
        private static readonly object Gate = new object();
        private static Dictionary<string, string> _state;

        public static string LedgerPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "native_probe_ledger.txt");

        private static void EnsureLoaded()
        {
            if (_state != null) return;
            _state = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(LedgerPath))
                {
                    foreach (var line in File.ReadAllLines(LedgerPath))
                    {
                        var parts = line.Split('|');
                        if (parts.Length < 2) continue;
                        _state[parts[1]] = parts[0];
                    }
                }
            }
            catch (Exception ex) { Log.Warn("[NativeProbe] could not read the ledger: " + ex.Message); }

            // Loaded once per editor process, before any probe of this session has run - so a
            // name still on "trying" was left there by a process that died inside it.
            foreach (var name in _state.Where(kv => kv.Value == "trying").Select(kv => kv.Key).ToList())
            {
                _state[name] = "crashed";
                Append("crashed", name);
                Log.Warn("[NativeProbe] '" + name + "' was in progress when the editor last died - it is now skipped. " +
                         "Delete " + LedgerPath + " to try it again.");
            }
        }

        private static void Append(string state, string name)
        {
            try
            {
                var dir = Path.GetDirectoryName(LedgerPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                // Open, write, flush to disk, close: the line must survive the process dying
                // on the very next instruction.
                using (var fs = new FileStream(LedgerPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var w = new StreamWriter(fs))
                {
                    w.WriteLine(state + "|" + name + "|" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    w.Flush();
                    fs.Flush(true);
                }
            }
            catch (Exception ex) { Log.Warn("[NativeProbe] could not write the ledger: " + ex.Message); }
        }

        public static bool IsSkipped(string name)
        {
            lock (Gate)
            {
                EnsureLoaded();
                return _state.TryGetValue(name, out var s) && s == "crashed";
            }
        }

        // Runs the action unless this name crashed the editor before. Returns false when skipped.
        // A managed exception is not a crash: it is recorded as survived and rethrown.
        public static bool Run(string name, Action action)
        {
            lock (Gate)
            {
                EnsureLoaded();
                if (_state.TryGetValue(name, out var s) && s == "crashed") return false;
                _state[name] = "trying";
                Append("trying", name);
            }
            try { action(); }
            finally
            {
                lock (Gate)
                {
                    _state[name] = "ok";
                    Append("ok", name);
                }
            }
            return true;
        }
    }
}

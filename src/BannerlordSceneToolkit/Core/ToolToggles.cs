using System;
using System.IO;

namespace BannerlordSceneToolkit
{
    // Hand-editable on/off switch per tool, read once at startup - so a specific tool's Harmony
    // patch (and therefore its hotkey and its Tick loop) can be left completely unregistered for
    // a whole session, without a rebuild. Built specifically to test the standing suspicion that
    // MaterialSwapTool's mere presence/activity alongside PrefabSwapperTool and PrefabCreatorTool
    // affects the long-standing native Qt5Core crash's frequency (see the crash investigation
    // memory) - merging all three into one assembly didn't actually test this, since all three
    // were already enabled together beforehand; this does. Setting a tool to false here is as
    // close to "not loaded" as is possible short of a separate build: its hotkey (F5/F6/F8) does
    // nothing, its own Tick() never runs, nothing about it is reachable from anywhere. Its static
    // classes still occupy memory (same assembly, can't avoid that without a real rebuild), but no
    // code in it ever executes.
    public class ToolToggles
    {
        public bool MaterialSwapTool = true;
        public bool PrefabSwapperTool = true;
        public bool PrefabCreatorTool = true;

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "BannerlordSceneToolkit", "tool_toggles.txt");

        public static ToolToggles Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    var defaults = new ToolToggles();
                    Save(defaults);
                    return defaults;
                }

                var result = new ToolToggles();
                foreach (var rawLine in File.ReadAllLines(ConfigPath))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var parts = line.Split('=');
                    if (parts.Length != 2) continue;
                    var key = parts[0].Trim();
                    if (!bool.TryParse(parts[1].Trim(), out var value)) continue;

                    if (string.Equals(key, "MaterialSwapTool", StringComparison.OrdinalIgnoreCase)) result.MaterialSwapTool = value;
                    else if (string.Equals(key, "PrefabSwapperTool", StringComparison.OrdinalIgnoreCase)) result.PrefabSwapperTool = value;
                    else if (string.Equals(key, "PrefabCreatorTool", StringComparison.OrdinalIgnoreCase)) result.PrefabCreatorTool = value;
                }
                return result;
            }
            catch (Exception ex)
            {
                Log.Error("ToolToggles.Load failed, defaulting to all tools enabled: " + ex.Message);
                return new ToolToggles();
            }
        }

        private static void Save(ToolToggles toggles)
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var lines = new[]
                {
                    "# BannerlordSceneToolkit per-tool enable/disable, read once at game startup.",
                    "# Set a tool to false to skip registering its Harmony patch entirely for this",
                    "# session - its hotkey (F5 Prefab Creator / F6 Prefab Swapper / F8 Material Swap)",
                    "# does nothing and its Tick loop never runs. Restart the game after editing for a",
                    "# change to take effect. Check BannerlordSceneToolkit\\tool.log on the next load",
                    "# to confirm which tools actually got patched.",
                    $"MaterialSwapTool={toggles.MaterialSwapTool}",
                    $"PrefabSwapperTool={toggles.PrefabSwapperTool}",
                    $"PrefabCreatorTool={toggles.PrefabCreatorTool}",
                };
                File.WriteAllLines(ConfigPath, lines);
            }
            catch (Exception ex)
            {
                Log.Error("ToolToggles.Save failed: " + ex.Message);
            }
        }
    }
}

using System;
using System.IO;
using Newtonsoft.Json;

namespace MaterialSwapTool.Core
{
    // ONE global UI scale for every toolkit panel (1.0 = the editor's normal size). Changing it on
    // any panel with Ctrl+Minus / Ctrl+Equals / Ctrl+0 changes the size EVERY panel opens at,
    // including windows popped out afterward - there is no longer a separate size per panel. This
    // is what "scale down 20% and the next window is also 20% down" needs: a new panel reads this
    // one value on open (see PanelScale.Apply).
    //
    // Stored as a single number in Documents\...\MaterialSwapTool\PanelScales.json, clamped to
    // [Min, Max] on both read and write so a hand-edited or stale file can never make panels
    // unusably tiny or so large a Close button leaves the screen. The panelKey parameters are
    // ignored - kept only so the 28 call sites did not have to change when this went from
    // per-panel to global.
    public static class PanelScaleStore
    {
        public const float Min = 0.5f;
        public const float Max = 1.5f;

        // Scale panels open at until the user changes it. 0.8 = two Ctrl+Minus steps below the
        // engine size; requested because panels open too large on some screens.
        public const float DefaultScale = 0.8f;

        private static float? _scale;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "PanelScales.json");

        public static float Clamp(float v) => v < Min ? Min : (v > Max ? Max : v);

        private static float Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    // New format is a bare number. An older per-panel file (a JSON object) throws
                    // here and falls through to the default - the next save rewrites it as a number.
                    var s = JsonConvert.DeserializeObject<float>(File.ReadAllText(FilePath));
                    if (s > 0f) return Clamp(s);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("PanelScaleStore: could not read a global scale (using default): " + ex.Message);
            }
            return DefaultScale;
        }

        public static float Get(string panelKey = null)
        {
            if (_scale == null) _scale = Load();
            return _scale.Value;
        }

        public static void Set(string panelKey, float scale)
        {
            float v = Clamp(scale);
            _scale = v;
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(v));
            }
            catch (Exception ex)
            {
                Log.Warn("PanelScaleStore: failed to save global scale: " + ex.Message);
            }
        }
    }
}

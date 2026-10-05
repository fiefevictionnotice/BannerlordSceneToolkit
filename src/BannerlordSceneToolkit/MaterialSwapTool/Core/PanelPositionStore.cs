using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace MaterialSwapTool.Core
{
    // Last-dragged position per panel, keyed by a short panel name (e.g. "MaterialSwapPanel").
    // Same Documents-folder convention as MaterialSwapPreset's PersonalPresetsDir. Loaded lazily,
    // written on every drag release - not batched, since drags are an infrequent user action, not
    // a per-tick one.
    public static class PanelPositionStore
    {
        private class Entry
        {
            public float X;
            public float Y;
        }

        private static Dictionary<string, Entry> _positions;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "PanelPositions.json");

        private static Dictionary<string, Entry> Positions
        {
            get
            {
                if (_positions != null) return _positions;
                _positions = new Dictionary<string, Entry>();
                try
                {
                    if (File.Exists(FilePath))
                    {
                        var loaded = JsonConvert.DeserializeObject<Dictionary<string, Entry>>(File.ReadAllText(FilePath));
                        if (loaded != null) _positions = loaded;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("PanelPositionStore: failed to load saved positions: " + ex.Message);
                }
                return _positions;
            }
        }

        public static bool TryGet(string panelKey, out float x, out float y)
        {
            if (Positions.TryGetValue(panelKey, out var entry))
            {
                x = entry.X;
                y = entry.Y;
                return true;
            }
            x = 0f;
            y = 0f;
            return false;
        }

        public static void Save(string panelKey, float x, float y)
        {
            Positions[panelKey] = new Entry { X = x, Y = y };
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(Positions, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.Warn("PanelPositionStore: failed to save position for '" + panelKey + "': " + ex.Message);
            }
        }
    }
}

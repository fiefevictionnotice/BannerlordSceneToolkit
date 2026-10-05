using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MaterialSwapTool.Core
{
    // Vanilla baseline material per (MetaMesh name, Mesh name), from mesh_slot_map.csv
    // (H:/AssetsToAnalyze), trimmed to just the 3 columns needed. Verified live (2026-08-16)
    // against european_city_house_d - every one of its 19 mesh slots, full-detail and lod5 tiers
    // alike, matched this data exactly by name. That's what makes Revert to Normal trustworthy:
    // the lookup key is the same (MetaMesh name, Mesh name) pair the live engine already uses,
    // not a positional index that would need re-verifying per prefab.
    public static class MeshDefaults
    {
        private static Dictionary<(string MetaMesh, string Mesh), string> _defaults;

        private static string ModuleDir => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "..", ".."));

        private static string ReferenceFilePath => Path.Combine(ModuleDir, "ReferenceData", "mesh_slot_defaults.txt");

        private static Dictionary<(string, string), string> Defaults
        {
            get
            {
                if (_defaults != null) return _defaults;
                _defaults = new Dictionary<(string, string), string>();
                try
                {
                    var path = ReferenceFilePath;
                    if (File.Exists(path))
                    {
                        foreach (var line in File.ReadLines(path))
                        {
                            var parts = line.Split('|');
                            if (parts.Length != 3) continue;
                            _defaults[(parts[0].Trim().ToLowerInvariant(), parts[1].Trim().ToLowerInvariant())] = parts[2].Trim();
                        }
                    }
                    else
                    {
                        Log.Warn($"MeshDefaults: reference file not found at {path} - Revert to Normal will find nothing.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("MeshDefaults: failed to load reference file: " + ex);
                }
                return _defaults;
            }
        }

        public static bool TryGetDefault(string metaMeshName, string meshName, out string defaultMaterial) =>
            Defaults.TryGetValue(((metaMeshName ?? "").ToLowerInvariant(), (meshName ?? "").ToLowerInvariant()), out defaultMaterial);
    }
}

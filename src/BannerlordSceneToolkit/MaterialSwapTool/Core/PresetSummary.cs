using System;
using System.Collections.Generic;

namespace MaterialSwapTool.Core
{
    public class PresetSummary
    {
        public string Name { get; set; }
        public DateTime ModifiedUtc { get; set; }
        public bool IsDeletable { get; set; }
        // True for the fixed set shipped with the module (Presets\BuiltIn, read-only, never
        // deletable). False for the user's own Documents\...\Presets folder.
        public bool IsBuiltIn { get; set; }
        public List<MaterialSwapRule> Rules { get; set; } = new List<MaterialSwapRule>();
        public List<string> Tags { get; set; } = new List<string>();
    }
}

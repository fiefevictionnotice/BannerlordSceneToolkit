namespace MaterialSwapTool.Core
{
    // A single "from material -> to material" substitution, matched by material Name.
    // ColorFactor is optional (empty = unset) - a hex color string applied to Mesh.Color on any
    // mesh the rule touches, on top of the material swap. RRGGBB or RRGGBBAA, matching the same
    // hex-color convention used throughout this engine's own UI brushes.
    public class MaterialSwapRule
    {
        public string FromMaterial { get; set; }
        public string ToMaterial { get; set; }
        public string ColorFactor { get; set; } = "";

        public MaterialSwapRule() { }

        public MaterialSwapRule(string from, string to, string colorFactor = "")
        {
            FromMaterial = from;
            ToMaterial = to;
            ColorFactor = colorFactor ?? "";
        }
    }
}

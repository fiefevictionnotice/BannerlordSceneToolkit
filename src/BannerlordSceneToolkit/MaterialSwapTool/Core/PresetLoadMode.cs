namespace MaterialSwapTool.Core
{
    public enum PresetLoadMode
    {
        Overwrite,
        Add,

        // Only fills in ToMaterial (and ColorFactor, if yours is blank too) for rules that
        // already exist in your current list with an empty ToMaterial - never adds new rows,
        // never touches a row that already has a target filled in. Built for finishing off
        // "Get Input Rules from Selection" output using an existing preset as the answer key.
        Merge,
    }
}

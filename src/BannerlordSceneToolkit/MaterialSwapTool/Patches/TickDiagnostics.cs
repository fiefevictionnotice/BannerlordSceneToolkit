namespace MaterialSwapTool.Patches
{
    // Throwaway instrumentation to empirically determine which MBEditor method actually ticks
    // during plain editing - MBEditor.TickEditMode was assumed to, based on decompiled source
    // reasoning, but a live heap-dump check proved its patch never fires. Inspect these counters
    // (and the *EditModeOn variants) the same way via `dumpclass` rather than guessing again.
    public static class TickDiagnostics
    {
        public static long TickEditModeHits;
        public static long TickEditModeHitsWhileEditModeOn;
        public static long TickScenePresentationHits;
        public static long TickScenePresentationHitsWhileEditModeOn;
    }
}

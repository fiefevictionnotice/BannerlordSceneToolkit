using System;
using System.Collections.Generic;
using System.Linq;
using MaterialSwapTool.Backup;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MaterialSwapTool.Core
{
    // Notices sustained stutter and offers to switch off the mod's own costly features.
    //
    // DELIBERATELY MODEST ABOUT WHAT IT KNOWS. It measures frame time, which rises for reasons
    // that have nothing to do with this mod - a big scene loading, the editor rebuilding navmesh,
    // something else entirely. So it never claims the mod is at fault. It says stutter is
    // happening, names what this mod runs continuously, and offers to turn that off so you can
    // tell the difference. Guessing at causation from a frame-time graph would be dishonest.
    //
    // WHAT COSTS ANYTHING HERE, measured rather than assumed:
    //   * ManipulationWatcher - polls the selection's frames ~16x a second. The only thing in the
    //     mod that runs continuously regardless of what you are doing. Cost is proportional to how
    //     much is SELECTED (capped at 64), not to scene size, since it asks the editor for its
    //     selection rather than scanning the scene.
    //   * The last-operation readout - one text widget, refreshed on the same tick.
    //   * Backup timer - a few file stats a second, and a copy every few minutes.
    // Everything else in this toolkit runs on a button press.
    public static class PerformanceWatchdog
    {
        // A frame this slow is a visible hitch, not just a busy frame.
        private const float StutterFrameSeconds = 0.25f;

        // How many hitches inside the window before it is worth mentioning.
        private const int StutterThreshold = 6;
        private const float WindowSeconds = 60f;

        // Rate limits, as specified: never more than once in 5 minutes, and never more than twice
        // in any 15. A warning about stutter that itself becomes a nuisance is self-defeating.
        private const float MinSecondsBetweenPrompts = 300f;
        private const int MaxPromptsPerQuarterHour = 2;
        private const float QuarterHourSeconds = 900f;

        private static readonly List<float> RecentStutters = new List<float>();
        private static readonly List<float> RecentPrompts = new List<float>();

        private static float _clock;
        private static float _lastPromptAt = float.MinValue;
        private static bool _promptOpen;

        public static void Tick(float dt)
        {
            _clock += dt;

            // Ignore absurd frames: alt-tabbing, a scene load or a breakpoint produce multi-second
            // dt values that say nothing about steady-state performance.
            if (dt >= StutterFrameSeconds && dt < 3f) RecentStutters.Add(_clock);

            RecentStutters.RemoveAll(t => _clock - t > WindowSeconds);
            RecentPrompts.RemoveAll(t => _clock - t > QuarterHourSeconds);

            if (_promptOpen) return;
            if (!PerformanceSettings.Current.WarningsEnabled) return;
            if (RecentStutters.Count < StutterThreshold) return;
            if (_clock - _lastPromptAt < MinSecondsBetweenPrompts) return;
            if (RecentPrompts.Count >= MaxPromptsPerQuarterHour) return;

            Prompt();
        }

        private static void Prompt()
        {
            _promptOpen = true;
            _lastPromptAt = _clock;
            RecentPrompts.Add(_clock);

            var count = RecentStutters.Count;
            RecentStutters.Clear();

            Log.Warn($"[Performance] {count} slow frame(s) in the last minute - offering to disable continuous features.");

            var body =
                $"The editor has had {count} noticeably slow frames in the last minute.\n\n" +
                "This may have nothing to do with this mod - a large scene, a navmesh rebuild or " +
                "anything else can do it. But the toolkit does run two things continuously, and " +
                "turning them off is the quickest way to rule it out:\n\n" +
                "  - drag detection (numeric transform + repeat last), polled ~16x a second\n" +
                "  - the last-operation readout\n\n" +
                "Everything else in the toolkit only runs when you press something, and backups are " +
                "unaffected either way.\n\n" +
                "Turn those off for now? You can switch them back on any time in F9 - Shortcuts.";

            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Editor is stuttering",
                    body,
                    isAffirmativeOptionShown: true,
                    isNegativeOptionShown: true,
                    affirmativeText: "Turn them off",
                    negativeText: "Keep them on",
                    affirmativeAction: () =>
                    {
                        _promptOpen = false;
                        var s = ShortcutSettings.Current;
                        s.NumericTransformEnabled = false;
                        s.Save();
                        Log.Info("[Performance] continuous features disabled by the user.");
                        try { MBEditor.AddEditorWarning("Drag detection and the last-operation readout are off. Re-enable in F9 - Shortcuts."); } catch { }
                    },
                    negativeAction: () =>
                    {
                        _promptOpen = false;
                        Log.Info("[Performance] user kept continuous features on.");
                        OfferPermanentDismissal();
                    }));
            }
            catch (Exception ex)
            {
                _promptOpen = false;
                Log.Warn("[Performance] could not show the prompt: " + ex.Message);
            }
        }

        // Asked only after declining once, so the "stop asking" option appears when it is actually
        // wanted rather than as a third button nobody reads the first time.
        private static void OfferPermanentDismissal()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Stop checking?",
                    "Would you like to stop these performance warnings entirely? Nothing changes about " +
                    "what the toolkit does - only whether it offers this again. You can turn the checks " +
                    "back on in F9 - Performance.",
                    isAffirmativeOptionShown: true,
                    isNegativeOptionShown: true,
                    affirmativeText: "Stop asking",
                    negativeText: "Keep checking",
                    affirmativeAction: () =>
                    {
                        var p = PerformanceSettings.Current;
                        p.WarningsEnabled = false;
                        p.Save();
                        Log.Info("[Performance] warnings permanently dismissed.");
                    },
                    negativeAction: null));
            }
            catch { }
        }

        // For the F9 readout, so the state is inspectable rather than mysterious.
        public static string DescribeState()
        {
            var enabled = PerformanceSettings.Current.WarningsEnabled;
            if (!enabled) return "Performance warnings: OFF";
            return $"Performance warnings: ON - {RecentStutters.Count} slow frame(s) in the last minute " +
                   $"(warns at {StutterThreshold}).";
        }
    }
}

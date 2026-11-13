using System;
using TaleWorlds.Library;

namespace BannerlordSceneToolkit
{
    // "You have unsaved changes" for the row editors (Interior Whitelist, Category Editor,
    // Culture Editor), added 2026-11-12. Those panels only write on Save, and Close / Esc used to
    // discard edits without a word - on the tutorial video that cost a scene its interiors
    // (docs/VIDEO-VS-DOCS-REVIEW.md, section B).
    //
    // Two buttons is all an inquiry has, so the question is "discard?" and the safe answer keeps
    // the panel open: whoever wanted to save presses Save. A "save and close" button was left
    // out on purpose - a save that fails would then also close the panel.
    //
    // Only the user's own Close button and Esc go through here. Forced closes (parent panel
    // closing, scene switch) still close at once; asking a question during a teardown is worse
    // than losing an edit.
    public static class UnsavedGuard
    {
        private static bool _asking;

        public static void CloseOrAsk(string panelName, bool hasUnsavedChanges, Action close)
        {
            if (close == null) return;
            if (!hasUnsavedChanges) { close(); return; }
            if (_asking) return;   // Esc pressed again while the question is already up

            _asking = true;
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Unsaved changes",
                    panelName + " has changes that have not been saved. Closing now discards them.",
                    isAffirmativeOptionShown: true,
                    isNegativeOptionShown: true,
                    affirmativeText: "Discard changes",
                    negativeText: "Keep editing",
                    affirmativeAction: () => { _asking = false; close(); },
                    negativeAction: () => { _asking = false; }));
            }
            catch (Exception ex)
            {
                // Never trap the user in a panel because the question could not be shown.
                _asking = false;
                MaterialSwapTool.Log.Warn("[UnsavedGuard] could not show the inquiry, closing: " + ex.Message);
                close();
            }
        }
    }
}

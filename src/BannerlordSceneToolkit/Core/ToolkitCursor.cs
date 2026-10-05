using System;
using System.Collections.Generic;
using HarmonyLib;
using MaterialSwapTool.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace BannerlordSceneToolkit
{
    // Keeps the editor's own Windows cursor while toolkit panels are open.
    //
    // WHY THE CURSOR CHANGED. Every toolkit panel is a GauntletLayer, and each one called
    // InputRestrictions.SetInputRestrictions() with its default isMouseVisible=true.
    // ScreenManager.UpdateMouseVisibility walks every active layer each frame and, the moment
    // one asks for a visible mouse, calls the engine's SetMouseVisible(true). In the game that
    // means "show the cursor"; in the editor it means the engine swaps in its own hardware cursor
    // (Data\cursors\mb_cursor.cur - the oversized Gauntlet one) over the editor's normal Windows
    // arrow. The flag does nothing else: hit-testing, focus, click and wheel routing all come
    // from the InputUsageMask, which is left exactly as it was (ScreenManager.EarlyUpdate never
    // reads MouseVisibility; checked against the decompiled 1.2 ScreenSystem).
    //
    // So every toolkit layer now opens with isMouseVisible=false. That is simply the state the
    // editor is in before any panel opens - the same state it returns to when the last panel
    // closes, which already happens today without the cursor vanishing - so the Windows cursor
    // stays put and no engine cursor is ever requested.
    //
    // SECOND HALF, THE HARMONY PREFIX. ScreenManager also calls
    // MouseManager.ActivateMouseCursor(layer.ActiveCursor) whenever the mouse is over a layer
    // (GauntletLayer rewrites ActiveCursor from its UIContext every frame, so setting the layer's
    // cursor to CursorType.System would not stick). Whether the native side swaps the hardware
    // cursor on that call even while the mouse is "hidden" cannot be seen from managed code, so
    // while native-cursor mode is on the call is skipped whenever the engine cursor is hidden.
    // The only moment that call does anything wanted is when the engine cursor is actually
    // showing - and in native mode it never is. Editor-only: in the game client the prefix
    // always lets the call through.
    //
    // SWITCHABLE (F9 -> Shortcuts -> "Native cursor") because this is unverified against every
    // editor interaction. If the Windows cursor ever vanishes with a panel open, flipping the
    // switch restores the old engine-cursor behaviour for every open panel on the spot - no
    // rebuild, no reopen.
    public static class ToolkitCursor
    {
        public static bool NativeCursor => ShortcutSettings.Current.NativeCursorEnabled;

        // Layers this class has configured, so a toggle can reach the ones still open. Weak so a
        // layer torn down by a scene switch (which bypasses our Close paths) does not stay alive
        // through this list.
        private static readonly List<WeakReference<ScreenLayer>> _applied = new List<WeakReference<ScreenLayer>>();

        // Replaces the bare SetInputRestrictions() every toolkit layer used to call on open.
        // Same InputUsageMask.All as before; only the mouse-visibility flag follows the setting.
        public static void ApplyInputRestrictions(ScreenLayer layer)
        {
            if (layer == null) return;
            layer.InputRestrictions.SetInputRestrictions(!NativeCursor, InputUsageMask.All);
            lock (_applied)
            {
                Prune();
                _applied.Add(new WeakReference<ScreenLayer>(layer));
            }
        }

        // Pushes the current setting onto every toolkit layer that is open right now, so the F9
        // switch takes effect without closing and reopening panels. ScreenManager re-reads the
        // flag every frame, so this is all it takes. Returns how many open layers were touched.
        public static int ReapplyToOpenLayers()
        {
            int touched = 0;
            bool visible = !NativeCursor;
            lock (_applied)
            {
                Prune();
                foreach (var wr in _applied)
                {
                    if (!wr.TryGetTarget(out var layer)) continue;
                    try
                    {
                        if (!IsOpen(layer)) continue;
                        layer.InputRestrictions.SetMouseVisibility(visible);
                        touched++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("ToolkitCursor: could not re-apply to a layer: " + ex.Message);
                    }
                }
            }
            Log.Info($"ToolkitCursor: native cursor {(NativeCursor ? "ON" : "OFF")}, re-applied to {touched} open panel(s).");
            return touched;
        }

        private static bool IsOpen(ScreenLayer layer)
        {
            var layers = ScreenManager.SortedLayers;
            return layers != null && layers.Contains(layer);
        }

        // Drops dead references and layers that are no longer on screen. A closed layer's
        // InputRestrictions were Reset by its own Close path, and a scene switch discards the
        // layer object entirely, so there is nothing to keep for either.
        private static void Prune()
        {
            _applied.RemoveAll(wr =>
            {
                if (!wr.TryGetTarget(out var layer)) return true;
                try { return !IsOpen(layer); }
                catch { return true; }
            });
        }

        // ---- Harmony: MouseManager.ActivateMouseCursor ----

        public static void TryApplyPatch(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(MouseManager), nameof(MouseManager.ActivateMouseCursor));
                if (target == null)
                {
                    Log.Warn("[ToolkitCursor] MouseManager.ActivateMouseCursor not found - the native-cursor " +
                             "switch still works, but hovering a panel may briefly show the engine cursor.");
                    return;
                }
                var prefix = new HarmonyMethod(typeof(ToolkitCursor)
                    .GetMethod(nameof(ActivateMouseCursorPrefix), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
                harmony.Patch(target, prefix: prefix);
                Log.Info("[ToolkitCursor] ActivateMouseCursor prefix applied.");
            }
            catch (Exception ex)
            {
                Log.Warn("[ToolkitCursor] could not patch ActivateMouseCursor (non-fatal): " + ex.Message);
            }
        }

        // Skip the hardware-cursor swap entirely in the editor while native mode is on.
        // Returning false suppresses the original call; true lets it run untouched.
        //
        // 2026-10-04, first live test: the visibility-gated version changed nothing - the cursor
        // still swapped on the first panel open and the toggle made no difference. The editor
        // most likely reports its mouse as always visible, so the gate never closed and every
        // hover-time ActivateMouseCursor(Default) went through. So: in the editor, native mode
        // skips the call unconditionally (only ScreenManager ever makes it from managed code;
        // the editor's own Qt UI does not go through here). The first few calls per session are
        // logged with the cursor type and the visibility flag, so if the cursor STILL swaps
        // with every call suppressed, tool.log proves the swap is native and the remaining
        // route is a smaller cursor file (MouseManager.SetMouseCursor / Data\cursors).
        private static int _logged;

        private static bool ActivateMouseCursorPrefix(CursorType mouseId)
        {
            try
            {
                if (!NativeCursor) return true;
                if (!MBEditor.EditModeEnabled) return true;        // game client: never interfere
                if (_logged < 5)
                {
                    _logged++;
                    bool vis = false;
                    try { vis = ScreenManager.GetMouseVisibility(); } catch { }
                    Log.Info($"[ToolkitCursor] ActivateMouseCursor({mouseId}) call #{_logged}: engineMouseVisible={vis} -> SKIPPED (native cursor mode)");
                }
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}

using System;
using System.IO;
using TaleWorlds.Engine;

namespace BannerlordSceneToolkit
{
    // PRELOADS loose sprite-sheet PNGs into the engine's texture registry at startup, so the
    // editor's "RGL CONTENT WARNING - Unable to find texture: ui_..." popups never fire.
    //
    // WHY (2026-08-23, CCModule_SP): a module can reference sprite sheets in its SpriteData
    // that live only in its AssetPackages tpacs - the game CLIENT resolves those, but the
    // EDITOR does not mount that module's tpacs, and the editor's texture resolver also
    // ignores every loose-file location that was tried (GUI\SpriteSheets in category
    // subfolders, flat, and the Assets import folder - all placed, all ignored). The one
    // door that provably reaches the engine's texture registry from OUR side is
    // Texture.LoadTextureFromPath, called from managed code at submodule load - which runs
    // BEFORE the editor screen (and its AlwaysLoad sprite categories) initializes.
    //
    // Scans every module's GUI\SpriteSheets for PNGs and preloads them by filename (the
    // texture's resource name). Only community modules ship loose sheets, so the scan is
    // cheap; native modules contribute nothing. Cost is VRAM for the decoded sheets - paid
    // only when such files exist, i.e. exactly when the popups would have fired instead.
    public static class TextureRescue
    {
        public static void Run()
        {
            int loaded = 0, failed = 0;
            try
            {
                // bin\Win64_Shipping_wEditor -> two levels up -> Modules
                var modulesRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Modules"));
                if (!Directory.Exists(modulesRoot))
                {
                    Log.Warn($"[TextureRescue] Modules root not found at '{modulesRoot}' - skipped.");
                    return;
                }

                foreach (var moduleDir in Directory.GetDirectories(modulesRoot))
                {
                    var sheets = System.IO.Path.Combine(moduleDir, "GUI", "SpriteSheets");
                    if (!Directory.Exists(sheets)) continue;

                    foreach (var png in Directory.EnumerateFiles(sheets, "*.png", SearchOption.AllDirectories))
                    {
                        try
                        {
                            Texture.LoadTextureFromPath(System.IO.Path.GetFileName(png), System.IO.Path.GetDirectoryName(png));
                            loaded++;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            Log.Warn($"[TextureRescue] '{System.IO.Path.GetFileNameWithoutExtension(png)}': {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[TextureRescue] scan failed: " + ex.Message);
            }

            if (loaded > 0 || failed > 0)
                Log.Info($"[TextureRescue] preloaded {loaded} loose sprite sheet(s), {failed} failed.");
        }
    }
}

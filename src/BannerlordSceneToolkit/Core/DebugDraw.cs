using System;
using System.Reflection;
using TaleWorlds.Library;

namespace BannerlordSceneToolkit
{
    // Draws the engine's own debug shapes - currently just a sphere, for the grow-selection
    // radius. Nothing is created in the scene: these are per-frame debug primitives, so there is
    // no entity to undo, nothing that can be saved into the .xscene by accident, and no cleanup
    // to get wrong if the panel is closed abruptly.
    //
    // WHY REFLECTION RATHER THAN A DIRECT CALL. Both MBDebug.RenderDebugSphere and
    // Debug.RenderDebugSphere carry [Conditional("_RGL_KEEP_ASSERTS")]. A Conditional attribute
    // is evaluated where the CALL is compiled, not where the method is defined - so a direct call
    // from this assembly would be silently deleted by the C# compiler unless the whole project
    // defined that symbol. Defining it globally would also switch on every other conditional
    // TaleWorlds call this codebase makes, which is a much wider change than "draw one sphere".
    // Invoking through reflection sidesteps the attribute completely: the method body is present
    // in the shipped assembly either way.
    //
    // UNVERIFIED IN THE EDITOR BUILD, and worth saying plainly: whether the engine's debug
    // renderer actually draws anything in Win64_Shipping_wEditor has not been confirmed. The
    // binding either resolves or it does not, and that is logged once - so if no sphere appears,
    // the log distinguishes "we never found the method" from "we called it and the renderer
    // ignored us".
    internal static class DebugDraw
    {
        private static bool _resolved;
        private static MethodInfo _renderSphere;

        // ARGB, matching ColorHex's packing. A translucent-looking cyan reads clearly against
        // both terrain and stone without being mistaken for a selection highlight.
        public const uint RadiusColor = 0x6633CCFFu;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            foreach (var candidate in new[]
                     {
                         "TaleWorlds.Engine.MBDebug, TaleWorlds.Engine",
                         "TaleWorlds.Library.Debug, TaleWorlds.Library",
                     })
            {
                try
                {
                    var type = Type.GetType(candidate);
                    var method = type?.GetMethod("RenderDebugSphere", BindingFlags.Public | BindingFlags.Static);
                    if (method == null) continue;
                    _renderSphere = method;
                    Log.Info($"[DebugDraw] bound {type.FullName}.RenderDebugSphere for the grow-radius sphere.");
                    return;
                }
                catch (Exception ex) { Log.Warn($"[DebugDraw] could not bind via '{candidate}': {ex.Message}"); }
            }

            Log.Warn("[DebugDraw] no RenderDebugSphere could be bound - the radius sphere will not draw.");
        }

        // Call once per frame while the shape should be visible; debug primitives last a single
        // frame unless given a lifetime, which is what makes this self-cleaning.
        public static void Sphere(Vec3 center, float radius, uint color = RadiusColor)
        {
            Resolve();
            if (_renderSphere == null || radius <= 0.0001f) return;

            try
            {
                // (position, radius, color, depthCheck, time). depthCheck false so the sphere is
                // visible through geometry - the point is to see the reach of a selection that is
                // usually surrounded by the very things it might pull in.
                _renderSphere.Invoke(null, new object[] { center, radius, color, false, 0f });
            }
            catch (Exception ex)
            {
                Log.Warn("[DebugDraw] RenderDebugSphere threw, disabling: " + ex.Message);
                _renderSphere = null;
            }
        }
    }
}

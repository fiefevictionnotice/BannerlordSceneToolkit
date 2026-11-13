using System;
using System.Diagnostics;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace BannerlordSceneToolkit
{
    // Hover tooltips for every toolkit panel (2026-11-12).
    //
    // HOW A HINT IS DECLARED: in the panel XML, put a hidden text child with Id="Hint" inside the
    // widget the hint belongs to (normally a ButtonWidget, or the wrapper Widget of a text field):
    //
    //     <TextWidget Id="Hint" IsVisible="false" DoNotAcceptEvents="true" Text="What this does." />
    //
    // Nothing else is needed - no VM property, no command, no per-panel code. That is deliberate:
    // there are about forty view models here and a hint mechanism that needed a method on each of
    // them would never be kept up.
    //
    // HOW IT IS SHOWN: once per frame this looks at every GauntletLayer on the top screen, asks
    // its EventManager which widget the mouse is over, and walks up a few parents looking for a
    // "Hint" child. After a short dwell the text is shown in ONE shared layer of its own.
    //
    // THAT LAYER IS NOT A FOCUS LAYER and every node in it is DoNotAcceptEvents, for the same
    // reason as LastOperationHudLayer: it is drawn over the viewport and must never take a click
    // or a keystroke (see Core/PanelFocus.cs for what a stray focus grab costs).
    //
    // Why not Gauntlet's own HintWidget/HintViewModel: those rely on the game's global information
    // layer, which is not known to exist in the editor screen, and they need a HintViewModel
    // property per button.
    public static class ToolkitHints
    {
        public const string HintChildId = "Hint";

        private const string MovieName = "ToolkitHintPanel";
        private const float DwellSeconds = 0.45f;
        private const int MaxParentWalk = 4;

        private static GauntletLayer _layer;
        private static ToolkitHintVM _vm;
        private static Widget _box;
        private static ScreenBase _screenItWasAddedTo;

        private static Widget _hoverWidget;       // widget whose hint is pending or showing
        private static Widget _suppressedWidget;  // clicked: stay quiet until the mouse moves on
        private static float _hoverSeconds;
        private static bool _showing;
        private static bool _brokenThisSession;   // box laid out absurdly large: stay off (see PlaceBox)
        private static float _anchorX, _anchorY;  // mouse position when the hint appeared, pixels

        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static long _lastTickMs = -1000;

        private static bool Enabled
        {
            get
            {
                try { return MaterialSwapTool.Core.ShortcutSettings.Current.HintsEnabled; }
                catch { return true; }
            }
        }

        // Called from all three editor tick patches (so hints keep working whichever tools are
        // toggled off); the stopwatch guard makes the second and third call in a frame free.
        public static void Tick()
        {
            long now = Clock.ElapsedMilliseconds;
            long elapsed = now - _lastTickMs;
            if (elapsed < 4) return;
            _lastTickMs = now;
            float dt = elapsed > 100 ? 0.1f : elapsed / 1000f;

            try { TickInner(dt); }
            catch (Exception ex)
            {
                // A scene switch tears the screen stack down under these statics. Drop everything
                // and start clean rather than throw into the editor tick.
                MaterialSwapTool.Log.Warn("[Hints] tick threw (stale layer after a scene switch?): " + ex.Message);
                _layer = null; _vm = null; _box = null; _screenItWasAddedTo = null;
                _hoverWidget = null; _suppressedWidget = null; _showing = false;
            }
        }

        private static void TickInner(float dt)
        {
            if (_brokenThisSession) return;
            if (!Enabled) { Hide(); CloseLayer(); return; }

            var screen = ScreenManager.TopScreen;
            if (screen == null) { Hide(); return; }
            if (_layer != null && !ReferenceEquals(_screenItWasAddedTo, screen)) CloseLayer();

            Widget hovered = null;
            string text = null;
            var layers = screen.Layers;
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                var gl = layers[i] as GauntletLayer;
                if (gl == null || ReferenceEquals(gl, _layer)) continue;
                Widget hw;
                try { hw = gl.UIContext?.EventManager?.HoveredWidget; }
                catch { continue; }
                if (hw == null) continue;
                text = FindHint(hw);
                if (text != null) { hovered = hw; break; }
            }

            if (text == null)
            {
                _hoverWidget = null; _suppressedWidget = null; _hoverSeconds = 0f;
                Hide();
                return;
            }

            bool mouseDown = Input.IsKeyDown(InputKey.LeftMouseButton) || Input.IsKeyDown(InputKey.RightMouseButton);
            if (mouseDown)
            {
                // Clicking a button means "I know what this is": hide, and do not pop back up
                // while the mouse just rests there afterwards.
                _suppressedWidget = hovered;
                _hoverWidget = hovered;
                _hoverSeconds = 0f;
                Hide();
                return;
            }

            if (!ReferenceEquals(hovered, _hoverWidget))
            {
                _hoverWidget = hovered;
                _suppressedWidget = null;
                _hoverSeconds = 0f;
                Hide();
                return;
            }
            if (ReferenceEquals(hovered, _suppressedWidget)) return;

            _hoverSeconds += dt;
            if (_hoverSeconds < DwellSeconds) return;

            if (!_showing)
            {
                if (!EnsureLayer(screen)) return;
                // Match the shared panel scale (it can change while the layer stays alive).
                try
                {
                    float g = MaterialSwapTool.Core.PanelScaleStore.Get();
                    if (Math.Abs(_layer.UIContext.ScaleModifier - g) > 0.0001f) _layer.UIContext.ScaleModifier = g;
                }
                catch { }
                var mouse = hovered.EventManager.MousePosition;
                _anchorX = mouse.X;
                _anchorY = mouse.Y;
                _vm.Set(text, true);
                _showing = true;
                PlaceBox(first: true);
            }
            else
            {
                PlaceBox(first: false);
            }
        }

        private static string FindHint(Widget w)
        {
            for (int depth = 0; w != null && depth < MaxParentWalk; depth++, w = w.ParentWidget)
            {
                var children = w.Children;
                for (int i = 0; i < children.Count; i++)
                {
                    var c = children[i];
                    if (c.Id == HintChildId && c is TextWidget tw)
                    {
                        var t = tw.Text;
                        return string.IsNullOrWhiteSpace(t) ? null : t;
                    }
                }
            }
            return null;
        }

        // Keeps the box on screen (flips left / up near the right and bottom edges) and corrects
        // its position from where the engine actually put it. The correction loop is why this
        // does not need to know exactly how PositionXOffset relates to pixels at the current
        // panel scale: it converges on the pixel target either way.
        private static void PlaceBox(bool first)
        {
            if (_box == null || _layer == null) return;
            var ctx = _layer.UIContext;
            float inv = ctx.InverseScale;
            if (inv <= 0f || float.IsNaN(inv) || float.IsInfinity(inv)) inv = 1f;

            float screenW = TaleWorlds.Engine.Screen.RealScreenResolutionWidth;
            float screenH = TaleWorlds.Engine.Screen.RealScreenResolutionHeight;

            var size = _box.Size;
            float w = size.X > 1f ? size.X : 0f;
            float h = size.Y > 1f ? size.Y : 0f;

            // SAFETY NET (2026-11-12): the first live build laid the box out at full-screen size
            // and every tooltip blanked the editor. The layout is fixed, but a tooltip that big
            // is never right - if it happens again, switch hints off for the session instead of
            // covering the screen.
            if (w > screenW * 0.6f || h > screenH * 0.6f)
            {
                MaterialSwapTool.Log.Warn($"[Hints] tooltip box measured {w:0}x{h:0} on a {screenW:0}x{screenH:0} screen - hints disabled for this session.");
                _brokenThisSession = true;
                Hide();
                CloseLayer();
                return;
            }

            float tx = _anchorX + 18f, ty = _anchorY + 26f;
            if (w > 0f && tx + w > screenW - 4f) tx = _anchorX - w - 10f;
            if (h > 0f && ty + h > screenH - 4f) ty = _anchorY - h - 10f;
            if (tx < 4f) tx = 4f;
            if (ty < 4f) ty = 4f;

            if (first || w <= 0f)
            {
                _box.PositionXOffset = tx * inv;
                _box.PositionYOffset = ty * inv;
                return;
            }

            var actual = _box.GlobalPosition;
            float ex = tx - actual.X, ey = ty - actual.Y;
            if (Math.Abs(ex) > 1f) _box.PositionXOffset += ex * inv;
            if (Math.Abs(ey) > 1f) _box.PositionYOffset += ey * inv;
        }

        private static bool EnsureLayer(ScreenBase screen)
        {
            if (_layer != null) return true;
            // Same rule as the last-operation HUD: never add a layer while no scene is open
            // (the editor screen may be mid-teardown).
            if (!MaterialSwapTool.Core.EntitySelector.HasOpenScene) return false;
            try
            {
                _vm = new ToolkitHintVM();
                _layer = new GauntletLayer(MovieName, 4990);
                var movie = _layer.LoadMovie(MovieName, _vm);
                _box = movie.Movie.RootWidget.FindChild("HintBox", true);
                screen.AddLayer(_layer);
                _screenItWasAddedTo = screen;
                try { _layer.UIContext.ScaleModifier = MaterialSwapTool.Core.PanelScaleStore.Get(); } catch { }
                if (_box == null)
                {
                    MaterialSwapTool.Log.Warn("[Hints] HintBox widget not found in " + MovieName + " - hints disabled this session.");
                    CloseLayer();
                    return false;
                }
                MaterialSwapTool.Log.Info("[Hints] tooltip layer opened.");
                return true;
            }
            catch (Exception ex)
            {
                MaterialSwapTool.Log.Error("[Hints] could not open the tooltip layer: " + ex);
                CloseLayer();
                return false;
            }
        }

        private static void Hide()
        {
            if (!_showing) return;
            _showing = false;
            try { _vm?.Set("", false); } catch { }
        }

        private static void CloseLayer()
        {
            if (_layer == null) return;
            var owner = _screenItWasAddedTo ?? ScreenManager.TopScreen;
            try { owner?.RemoveLayer(_layer); }
            catch (Exception ex) { MaterialSwapTool.Log.Warn("[Hints] teardown issue: " + ex.Message); }
            _layer = null; _vm = null; _box = null; _screenItWasAddedTo = null; _showing = false;
        }
    }

    public class ToolkitHintVM : ViewModel
    {
        private string _text = "";
        private bool _visible;

        public void Set(string text, bool visible)
        {
            text = text ?? "";
            if (text != _text) { _text = text; OnPropertyChanged(nameof(Text)); }
            if (visible != _visible) { _visible = visible; OnPropertyChanged(nameof(IsVisible)); }
        }

        [DataSourceProperty]
        public bool IsVisible => _visible;

        [DataSourceProperty]
        public string Text => _text;
    }
}

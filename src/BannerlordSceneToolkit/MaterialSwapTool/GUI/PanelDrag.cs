using MaterialSwapTool.Core;
using TaleWorlds.Engine;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;

namespace MaterialSwapTool.GUI
{
    // Shared drag-to-move + position-persistence for every flyout panel. Wire a title-row widget's
    // Command.MouseDown (NOT Command.MousePressed - Widget only ever fires the string "MouseDown"
    // from OnMousePressed, confirmed by decompiling TaleWorlds.GauntletUI.BaseTypes.Widget; the
    // original drag scaffolding used "MousePressed", which matches no fired event and silently
    // never triggered) to a VM method that calls BeginDrag(), call ApplyInitialPosition() once
    // right after the movie loads, and call Tick() every frame the panel is open.
    public class PanelDrag
    {
        private readonly string _key;
        private readonly float _defaultX;
        private readonly float _defaultY;
        private bool _isDragging;

        // Not a pixel-perfect "keep the whole panel on screen" clamp (that would need the panel's
        // own current size, which isn't reliably measured yet the moment ApplyInitialPosition runs
        // right after the movie loads) - just a floor under how far off-center a saved offset can
        // push the panel, using the actual screen resolution. Confirmed live: a panel resized
        // significantly (Documentation grew from 682x720 to 760x1056 this session) combined with a
        // saved offset from when it was smaller pushed it far enough off-screen that even its Close
        // button became unreachable, with no way to drag it back since the drag handle was also
        // off-screen. This keeps at least a solid margin of any panel within the visible area
        // regardless of how its saved offset and current size interact.
        private const float EdgeMargin = 150f;

        public PanelDrag(string key, float defaultX = 0f, float defaultY = 0f)
        {
            _key = key;
            _defaultX = defaultX;
            _defaultY = defaultY;
        }

        private static float ClampOffset(float offset, float screenExtentHalf)
        {
            var limit = screenExtentHalf - EdgeMargin;
            if (limit <= 0f) return 0f;
            if (offset > limit) return limit;
            if (offset < -limit) return -limit;
            return offset;
        }

        public void ApplyInitialPosition(Widget root)
        {
            if (root == null) return;
            float x, y;
            if (PanelPositionStore.TryGet(_key, out x, out y))
            {
                // Fall through to the clamp below either way - a stale saved position is exactly
                // the case this is meant to catch.
            }
            else
            {
                x = _defaultX;
                y = _defaultY;
            }

            root.PositionXOffset = ClampOffset(x, Screen.RealScreenResolutionWidth / 2f);
            root.PositionYOffset = ClampOffset(y, Screen.RealScreenResolutionHeight / 2f);

            // A recentre requested while this panel was closed must not move it on open.
            _recenterGen = BannerlordSceneToolkit.PanelRecenter.Generation;
        }

        public void BeginDrag() => _isDragging = true;

        // Last PanelRecenter generation this panel honoured (see Core/PanelRecenter).
        private int _recenterGen = BannerlordSceneToolkit.PanelRecenter.Generation;

        public void Tick(Widget root)
        {
            if (root != null && _recenterGen != BannerlordSceneToolkit.PanelRecenter.Generation)
            {
                _recenterGen = BannerlordSceneToolkit.PanelRecenter.Generation;
                _isDragging = false;
                root.PositionXOffset = 0f;
                root.PositionYOffset = 0f;
                PanelPositionStore.Save(_key, 0f, 0f);
            }

            if (!_isDragging || root == null) return;
            root.PositionXOffset += Input.MouseMoveX;
            root.PositionYOffset += Input.MouseMoveY;
            if (!Input.IsKeyDown(InputKey.LeftMouseButton))
            {
                _isDragging = false;
                root.PositionXOffset = ClampOffset(root.PositionXOffset, Screen.RealScreenResolutionWidth / 2f);
                root.PositionYOffset = ClampOffset(root.PositionYOffset, Screen.RealScreenResolutionHeight / 2f);
                PanelPositionStore.Save(_key, root.PositionXOffset, root.PositionYOffset);
            }
        }
    }
}

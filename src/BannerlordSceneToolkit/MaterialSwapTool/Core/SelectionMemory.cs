using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;

namespace MaterialSwapTool.Core
{
    // Remembers what was selected a moment ago.
    //
    // WHY THIS IS NECESSARY. Pressing a key in this editor frequently CLEARS the selection - the
    // editor binds the number row and various letters to its own things, and it processes the key
    // before our Postfix runs. So any feature that reads the selection at the instant its hotkey
    // fires reads an empty one. That is why Ctrl+Shift+T reported "nothing selected" and Shift+R
    // had nothing to repeat, while typing during an operation worked fine - that path used a
    // snapshot captured earlier rather than a live read.
    //
    // Fed by ManipulationWatcher's polling, which is already running continuously, so this costs
    // nothing extra. Readers ask for the live selection first and fall back to this only when the
    // live read comes back empty AND the memory is recent - a genuinely empty selection that has
    // been empty for a while still reads as empty, which is what you want.
    public static class SelectionMemory
    {
        private const float MaxAgeSeconds = 2.0f;

        private static readonly List<GameEntity> Remembered = new List<GameEntity>();
        private static float _age = float.MaxValue;

        public static void Update(IEnumerable<GameEntity> selection, float dt)
        {
            _age += dt;

            var live = selection?.Where(e => e != null && e.Pointer != UIntPtr.Zero).ToList();
            if (live == null || live.Count == 0) return;

            Remembered.Clear();
            Remembered.AddRange(live);
            _age = 0f;
        }

        public static bool HasRecent => Remembered.Count > 0 && _age <= MaxAgeSeconds;

        public static List<GameEntity> Recent =>
            Remembered.Where(e => e != null && e.Pointer != UIntPtr.Zero).ToList();

        public static void Clear()
        {
            Remembered.Clear();
            _age = float.MaxValue;
        }

        // What every hotkey-driven feature should call. Live read wins; the remembered selection
        // only fills in when the key press itself has just wiped it.
        public static List<GameEntity> GetSelection()
        {
            var live = EntitySelector.GetLiveManualSelection();
            if (live.Count > 0) return live;

            if (HasRecent)
            {
                var recalled = Recent;
                if (recalled.Count > 0)
                {
                    Log.Info($"[SelectionMemory] live selection was empty; using {recalled.Count} remembered from {_age:0.00}s ago.");
                    return recalled;
                }
            }
            return live;
        }
    }
}

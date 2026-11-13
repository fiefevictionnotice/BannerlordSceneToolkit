using System;
using System.Collections.Generic;
using System.Globalization;
using MaterialSwapTool.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // One line of the Check result.
    public class MeshBlendRowVM : ViewModel
    {
        private string _text;
        private string _rowColor;

        public const string OkColor = "#1f4a2a99";
        public const string FailColor = "#8a3a1a99";
        public const string UnknownColor = "#6b5a1a99";
        public const string InfoColor = "#00000055";
        public const string SummaryColor = "#234a6bCC";

        public MeshBlendRowVM(MeshBlend.Line line)
        {
            _text = line.Text;
            switch (line.Verdict)
            {
                case MeshBlend.Verdict.Ok: _rowColor = OkColor; break;
                case MeshBlend.Verdict.Fail: _rowColor = FailColor; break;
                case MeshBlend.Verdict.Unknown: _rowColor = UnknownColor; break;
                case MeshBlend.Verdict.Summary: _rowColor = SummaryColor; break;
                default: _rowColor = InfoColor; break;
            }
        }

        [DataSourceProperty]
        public string Text
        {
            get => _text;
            set { if (value != _text) { _text = value; OnPropertyChangedWithValue(value, nameof(Text)); } }
        }

        [DataSourceProperty]
        public string RowColor
        {
            get => _rowColor;
            set { if (value != _rowColor) { _rowColor = value; OnPropertyChangedWithValue(value, nameof(RowColor)); } }
        }
    }

    // F8 -> Mesh Blend. Diagnoses why terrain mesh blend is not showing on the selected entity
    // and writes the per-mesh tuning values (see Core/MeshBlend.cs for the rules and where they
    // were read from).
    //
    // Everything runs on a button press. The -/+ steppers only change the text in a field;
    // nothing reaches the scene until Apply to Selection.
    //
    // FIELD CONVENTION: an EMPTY field means "keep whatever each mesh has now". That is how a
    // value the user is not tuning is preserved per mesh instead of being overwritten with the
    // first mesh's value.
    public class MeshBlendVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;

        private MBBindingList<MeshBlendRowVM> _rows = new MBBindingList<MeshBlendRowVM>();
        private string _statusText = "Select an entity in the editor, then press Check. Empty tuning fields keep the current value.";
        private string _blendHeight = "", _softness = "", _noiseAmount = "", _noiseScale = "";
        private string _heightmapStart = "", _heightmapPower = "", _normalBend = "";
        private bool _isAdvancedVisible;
        private string _advancedButtonText = "Advanced: OFF";

        public MeshBlendVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
        }

        [DataSourceProperty]
        public MBBindingList<MeshBlendRowVM> Rows
        {
            get => _rows;
            set { if (value != _rows) { _rows = value; OnPropertyChangedWithValue(value, nameof(Rows)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        [DataSourceProperty]
        public string BlendHeight
        {
            get => _blendHeight;
            set { if (value != _blendHeight) { _blendHeight = value; OnPropertyChangedWithValue(value, nameof(BlendHeight)); } }
        }

        [DataSourceProperty]
        public string Softness
        {
            get => _softness;
            set { if (value != _softness) { _softness = value; OnPropertyChangedWithValue(value, nameof(Softness)); } }
        }

        [DataSourceProperty]
        public string NoiseAmount
        {
            get => _noiseAmount;
            set { if (value != _noiseAmount) { _noiseAmount = value; OnPropertyChangedWithValue(value, nameof(NoiseAmount)); } }
        }

        [DataSourceProperty]
        public string NoiseScale
        {
            get => _noiseScale;
            set { if (value != _noiseScale) { _noiseScale = value; OnPropertyChangedWithValue(value, nameof(NoiseScale)); } }
        }

        [DataSourceProperty]
        public string HeightmapStart
        {
            get => _heightmapStart;
            set { if (value != _heightmapStart) { _heightmapStart = value; OnPropertyChangedWithValue(value, nameof(HeightmapStart)); } }
        }

        [DataSourceProperty]
        public string HeightmapPower
        {
            get => _heightmapPower;
            set { if (value != _heightmapPower) { _heightmapPower = value; OnPropertyChangedWithValue(value, nameof(HeightmapPower)); } }
        }

        [DataSourceProperty]
        public string NormalBend
        {
            get => _normalBend;
            set { if (value != _normalBend) { _normalBend = value; OnPropertyChangedWithValue(value, nameof(NormalBend)); } }
        }

        [DataSourceProperty]
        public bool IsAdvancedVisible
        {
            get => _isAdvancedVisible;
            set { if (value != _isAdvancedVisible) { _isAdvancedVisible = value; OnPropertyChangedWithValue(value, nameof(IsAdvancedVisible)); } }
        }

        [DataSourceProperty]
        public string AdvancedButtonText
        {
            get => _advancedButtonText;
            set { if (value != _advancedButtonText) { _advancedButtonText = value; OnPropertyChangedWithValue(value, nameof(AdvancedButtonText)); } }
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();

        // ---------------------------------------------------------------- field definitions

        // Limits come from the shader math (terrain_mesh_blend_functions.rsh:26-33, 106-109):
        // the smooth and heightmap ratios are divided by (1 - ratio), the normal bend ratio is a
        // divisor itself, and a blend height of 0 leaves nothing to blend. Out-of-range input is
        // refused with a message, never clamped.
        private class Field
        {
            public string Label;
            public float Default, Min, Max, Step;
            public Func<MeshBlendVM, string> Get;
            public Action<MeshBlendVM, string> Set;
        }

        private static readonly Field HeightField = new Field { Label = "Blend height", Default = 1.0f, Min = 0.05f, Max = 20f, Step = 0.1f, Get = v => v.BlendHeight, Set = (v, s) => v.BlendHeight = s };
        private static readonly Field SoftnessField = new Field { Label = "Softness", Default = 0f, Min = -0.5f, Max = 0.49f, Step = 0.05f, Get = v => v.Softness, Set = (v, s) => v.Softness = s };
        private static readonly Field NoiseAmountField = new Field { Label = "Noise amount", Default = 0f, Min = -0.5f, Max = 10f, Step = 0.1f, Get = v => v.NoiseAmount, Set = (v, s) => v.NoiseAmount = s };
        private static readonly Field NoiseScaleField = new Field { Label = "Noise scale", Default = 0f, Min = -15f, Max = 200f, Step = 1f, Get = v => v.NoiseScale, Set = (v, s) => v.NoiseScale = s };
        private static readonly Field HeightmapStartField = new Field { Label = "Heightmap start", Default = 0f, Min = -0.5f, Max = 0.49f, Step = 0.05f, Get = v => v.HeightmapStart, Set = (v, s) => v.HeightmapStart = s };
        private static readonly Field HeightmapPowerField = new Field { Label = "Heightmap power", Default = 0f, Min = -1f, Max = 10f, Step = 0.1f, Get = v => v.HeightmapPower, Set = (v, s) => v.HeightmapPower = s };
        private static readonly Field NormalBendField = new Field { Label = "Normal bend", Default = 0f, Min = -0.19f, Max = 0.8f, Step = 0.05f, Get = v => v.NormalBend, Set = (v, s) => v.NormalBend = s };

        private static string Fmt(float v) => (Math.Round(v, 3)).ToString("0.###", CultureInfo.InvariantCulture);

        // Empty -> null (keep). Anything else must parse and be in range, or the whole action
        // is refused with the reason.
        private bool TryField(Field field, out float? value, out string problem)
        {
            value = null;
            problem = null;
            var text = field.Get(this);
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!MeshBlend.TryParseNumber(text, out var parsed, out var why))
            {
                problem = field.Label + ": " + why + ". Use a number such as " + Fmt(field.Default) + " (a comma or a dot as decimal separator), or leave it empty to keep the current value.";
                return false;
            }
            if (parsed < field.Min || parsed > field.Max)
            {
                problem = $"{field.Label}: {Fmt(parsed)} is outside {Fmt(field.Min)} to {Fmt(field.Max)}. Nothing was changed.";
                return false;
            }
            value = parsed;
            return true;
        }

        private void Step(Field field, int direction)
        {
            try
            {
                var text = field.Get(this);
                float current;
                if (string.IsNullOrWhiteSpace(text)) current = field.Default;
                else if (!MeshBlend.TryParseNumber(text, out current, out var why))
                {
                    StatusText = field.Label + ": " + why + ". Fix or empty the field before using - and +.";
                    return;
                }
                var next = (float)Math.Round(current + direction * field.Step, 3);
                if (next < field.Min) next = field.Min;
                if (next > field.Max) next = field.Max;
                field.Set(this, Fmt(next));
                StatusText = $"{field.Label} set to {Fmt(next)} in the panel (allowed {Fmt(field.Min)} to {Fmt(field.Max)}). Press Apply to Selection to write it.";
            }
            catch (Exception ex)
            {
                StatusText = "Stepper failed: " + ex.Message;
                Log.Error("MeshBlend stepper failed: " + ex);
            }
        }

        public void ExecuteHeightDown() => Step(HeightField, -1);
        public void ExecuteHeightUp() => Step(HeightField, 1);
        public void ExecuteSoftnessDown() => Step(SoftnessField, -1);
        public void ExecuteSoftnessUp() => Step(SoftnessField, 1);
        public void ExecuteNoiseAmountDown() => Step(NoiseAmountField, -1);
        public void ExecuteNoiseAmountUp() => Step(NoiseAmountField, 1);
        public void ExecuteNoiseScaleDown() => Step(NoiseScaleField, -1);
        public void ExecuteNoiseScaleUp() => Step(NoiseScaleField, 1);
        public void ExecuteHeightmapStartDown() => Step(HeightmapStartField, -1);
        public void ExecuteHeightmapStartUp() => Step(HeightmapStartField, 1);
        public void ExecuteHeightmapPowerDown() => Step(HeightmapPowerField, -1);
        public void ExecuteHeightmapPowerUp() => Step(HeightmapPowerField, 1);
        public void ExecuteNormalBendDown() => Step(NormalBendField, -1);
        public void ExecuteNormalBendUp() => Step(NormalBendField, 1);

        public void ExecuteToggleAdvanced()
        {
            IsAdvancedVisible = !IsAdvancedVisible;
            AdvancedButtonText = IsAdvancedVisible ? "Advanced: ON" : "Advanced: OFF";
            StatusText = IsAdvancedVisible
                ? "Advanced values shown. They are applied only while this is ON, and only the fields that are not empty."
                : "Advanced values hidden. Apply to Selection leaves them alone while this is OFF.";
        }

        // ---------------------------------------------------------------- actions

        private bool TryGetSelection(out List<GameEntity> selection)
        {
            selection = null;
            if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return false; }
            selection = SelectionMemory.GetSelection();
            if (selection == null || selection.Count == 0)
            {
                StatusText = "Nothing is selected. Select one or more entities in the editor first.";
                return false;
            }
            return true;
        }

        public void ExecuteCheck()
        {
            try
            {
                if (!TryGetSelection(out var selection)) return;
                var lines = MeshBlend.Check(selection);
                Rows.Clear();
                int fail = 0, unknown = 0;
                foreach (var line in lines)
                {
                    Rows.Add(new MeshBlendRowVM(line));
                    if (line.Verdict == MeshBlend.Verdict.Fail) fail++;
                    if (line.Verdict == MeshBlend.Verdict.Unknown) unknown++;
                    Log.Info("[MeshBlend] " + line.Verdict + ": " + line.Text);
                }
                StatusText = lines.Count > 0 ? lines[lines.Count - 1].Text + $" ({fail} FAIL, {unknown} UNKNOWN.)" : "Check produced no lines.";
            }
            catch (Exception ex)
            {
                StatusText = "Check failed: " + ex.Message;
                Log.Error("MeshBlend check failed: " + ex);
            }
        }

        public void ExecuteRecalculate()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
                StatusText = MeshBlend.Recalculate();
            }
            catch (Exception ex)
            {
                StatusText = "Recalculate failed: " + ex.Message;
                Log.Error("MeshBlend recalculate failed: " + ex);
            }
        }

        public void ExecuteReadFromSelection()
        {
            try
            {
                if (!TryGetSelection(out var selection)) return;
                if (!MeshBlend.TryRead(selection, out var first, out var second, out var message))
                {
                    StatusText = message;
                    return;
                }
                BlendHeight = Fmt(1.0f + first.x);
                Softness = Fmt(first.y);
                NoiseAmount = Fmt(first.z);
                NoiseScale = Fmt(first.w);
                HeightmapStart = Fmt(second.x);
                HeightmapPower = Fmt(second.y);
                NormalBend = Fmt(second.z);
                StatusText = message;
            }
            catch (Exception ex)
            {
                StatusText = "Read failed: " + ex.Message;
                Log.Error("MeshBlend read failed: " + ex);
            }
        }

        public void ExecuteApply()
        {
            try
            {
                if (!TryGetSelection(out var selection)) return;

                var values = new MeshBlend.Values();
                if (!TryField(HeightField, out var height, out var problem)) { StatusText = problem; return; }
                if (!TryField(SoftnessField, out values.Y, out problem)) { StatusText = problem; return; }
                if (!TryField(NoiseAmountField, out values.Z, out problem)) { StatusText = problem; return; }
                if (!TryField(NoiseScaleField, out values.W, out problem)) { StatusText = problem; return; }
                if (height.HasValue) values.X = height.Value - 1.0f;

                if (IsAdvancedVisible)
                {
                    if (!TryField(HeightmapStartField, out values.X2, out problem)) { StatusText = problem; return; }
                    if (!TryField(HeightmapPowerField, out values.Y2, out problem)) { StatusText = problem; return; }
                    if (!TryField(NormalBendField, out values.Z2, out problem)) { StatusText = problem; return; }
                }

                StatusText = MeshBlend.Apply(selection, values, "Apply");
            }
            catch (Exception ex)
            {
                StatusText = "Apply failed: " + ex.Message;
                Log.Error("MeshBlend apply failed: " + ex);
            }
        }

        public void ExecuteResetToDefault()
        {
            try
            {
                if (!TryGetSelection(out var selection)) return;
                // Zeros on everything the blend reads. The fourth component of the second
                // argument is not a blend value and is kept.
                var values = new MeshBlend.Values { X = 0f, Y = 0f, Z = 0f, W = 0f, X2 = 0f, Y2 = 0f, Z2 = 0f };
                var result = MeshBlend.Apply(selection, values, "Reset to default");
                if (result.StartsWith("Reset to default: changed", StringComparison.Ordinal))
                {
                    BlendHeight = Fmt(1.0f);
                    Softness = NoiseAmount = NoiseScale = HeightmapStart = HeightmapPower = NormalBend = Fmt(0f);
                }
                StatusText = result;
            }
            catch (Exception ex)
            {
                StatusText = "Reset failed: " + ex.Message;
                Log.Error("MeshBlend reset failed: " + ex);
            }
        }

        public void ExecuteUndo()
        {
            try { StatusText = MeshBlend.UndoLast(); }
            catch (Exception ex)
            {
                StatusText = "Undo failed: " + ex.Message;
                Log.Error("MeshBlend undo failed: " + ex);
            }
        }
    }
}

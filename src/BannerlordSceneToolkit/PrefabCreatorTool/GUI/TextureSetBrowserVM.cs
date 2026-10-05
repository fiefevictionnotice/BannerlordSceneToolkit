using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PrefabCreatorTool.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace PrefabCreatorTool.GUI
{
    // Browse/create/edit ColorPresets ("texture sets") - the piece that was missing entirely
    // before: presets could only ever be auto-derived from a detected variant's diff, never
    // authored or edited by hand, and there was no way to see what was actually in one beyond
    // clicking it and hoping. Styled after MaterialSwapTool's PresetBrowserPanel (card list,
    // expandable detail) per explicit request to match that look.
    public class TextureSetBrowserVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;

        private string _searchTerm = "";
        private MBBindingList<TextureSetRowVM> _rows;
        private string _statusText = "";
        private string _colorWordsInput = string.Join(", ", ColorFamilyImporter.DefaultColorWords);

        // --- Create/Edit working area ---
        private string _editNameInput = "";
        private string _editBasePrefabInput = "";
        private MBBindingList<TextureSetOverrideRowVM> _editOverrides;
        private string _editStatus = "Type a name + base prefab, then add overrides (capture from a selected entity, or type them manually), then Save.";

        public TextureSetBrowserVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _rows = new MBBindingList<TextureSetRowVM>();
            _editOverrides = new MBBindingList<TextureSetOverrideRowVM>();
            Refresh();
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();

        [DataSourceProperty]
        public string SearchTerm
        {
            get => _searchTerm;
            set { if (value != _searchTerm) { _searchTerm = value; OnPropertyChangedWithValue(value, nameof(SearchTerm)); Refresh(); } }
        }

        [DataSourceProperty]
        public MBBindingList<TextureSetRowVM> Rows
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
        public string EditNameInput
        {
            get => _editNameInput;
            set { if (value != _editNameInput) { _editNameInput = value; OnPropertyChangedWithValue(value, nameof(EditNameInput)); } }
        }

        [DataSourceProperty]
        public string EditBasePrefabInput
        {
            get => _editBasePrefabInput;
            set { if (value != _editBasePrefabInput) { _editBasePrefabInput = value; OnPropertyChangedWithValue(value, nameof(EditBasePrefabInput)); } }
        }

        [DataSourceProperty]
        public MBBindingList<TextureSetOverrideRowVM> EditOverrides
        {
            get => _editOverrides;
            set { if (value != _editOverrides) { _editOverrides = value; OnPropertyChangedWithValue(value, nameof(EditOverrides)); } }
        }

        [DataSourceProperty]
        public string EditStatus
        {
            get => _editStatus;
            set { if (value != _editStatus) { _editStatus = value; OnPropertyChangedWithValue(value, nameof(EditStatus)); } }
        }

        [DataSourceProperty]
        public string ColorWordsInput
        {
            get => _colorWordsInput;
            set { if (value != _colorWordsInput) { _colorWordsInput = value; OnPropertyChangedWithValue(value, nameof(ColorWordsInput)); } }
        }

        public void ExecuteRefresh() => Refresh();

        // Scans the live scene for entities tagged "<family>_<colorword>" (the convention already
        // seen in real scenes, e.g. "fief_wine_shelf_redbrown" on the greeble pieces of one colored
        // shelf assembly) and saves one ColorPreset per (family, colorword) found - see
        // ColorFamilyImporter for the matching/aggregation logic. Lets any future tagged furniture
        // family get texture-set presets without hand-authoring them one override at a time.
        public void ExecuteImportColorFamilies()
        {
            if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

            var words = (ColorWordsInput ?? "").Split(',').Select(w => w.Trim()).Where(w => w.Length > 0).ToList();
            var result = ColorFamilyImporter.Import(EntitySelector.CollectAll(), words.Count > 0 ? words : null);

            Refresh();
            StatusText = result.PresetsSaved == 0
                ? "No tagged color families found - " + (result.Skipped.FirstOrDefault() ?? "nothing matched.")
                : $"Imported {result.PresetsSaved} texture set(s): {string.Join(", ", result.SavedNames)}";
        }

        private void Refresh()
        {
            Rows.Clear();
            var term = (SearchTerm ?? "").Trim();
            foreach (var name in ColorPresetStore.ListNames())
            {
                ColorPreset preset;
                try { preset = ColorPresetStore.Load(name); }
                catch { continue; }
                if (term.Length > 0 &&
                    preset.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0 &&
                    preset.BasePrefabName.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Rows.Add(new TextureSetRowVM(preset, RunApply, RunDelete, RunEdit));
            }
        }

        private void RunApply(TextureSetRowVM row)
        {
            if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
            var selection = EntitySelector.GetManualSelection();
            if (selection.Count == 0) { StatusText = "Nothing selected - select the entity/entities to apply it to first."; return; }

            int applied = 0;
            foreach (var e in selection) applied += ColorPresetApplier.Apply(e, row.Preset);
            StatusText = $"Applied '{row.Name}' to {selection.Count} entit{(selection.Count == 1 ? "y" : "ies")} - {applied} override(s) matched.";
        }

        private void RunDelete(TextureSetRowVM row)
        {
            ColorPresetStore.Delete(row.Name);
            Rows.Remove(row);
            StatusText = $"Deleted '{row.Name}'.";
        }

        private void RunEdit(TextureSetRowVM row)
        {
            EditNameInput = row.Name;
            EditBasePrefabInput = row.BasePrefabName;
            EditOverrides.Clear();
            foreach (var kv in row.Preset.Overrides)
                EditOverrides.Add(MakeOverrideRow(kv.Key, kv.Value.Material, kv.Value.Color));
            EditStatus = $"Editing '{row.Name}' - change anything below, then Save (overwrites this same preset).";
        }

        public void ExecuteFillBaseFromSelection()
        {
            if (!EntitySelector.HasOpenScene) { EditStatus = "No scene is currently open."; return; }
            var selected = EntitySelector.GetManualSelection();
            if (selected.Count == 0) { EditStatus = "Nothing selected."; return; }
            EditBasePrefabInput = selected[0].Name ?? "";
            EditStatus = $"Base prefab filled from selection: '{EditBasePrefabInput}'";
        }

        // Reads whatever's selected (the base itself, or one of its children/parts) and adds/
        // updates an override entry for it - part key is the entity's own stripped name (matches
        // ColorPresetApplier's child-lookup resolution), material/color come from its first mesh.
        public void ExecuteCaptureOverrideFromSelection()
        {
            if (!EntitySelector.HasOpenScene) { EditStatus = "No scene is currently open."; return; }
            var selected = EntitySelector.GetManualSelection();
            if (selected.Count == 0) { EditStatus = "Nothing selected - select the part to capture first."; return; }

            var entity = selected[0];
            var (material, color) = GetFirstMeshInfo(entity);
            if (material == null && color == null) { EditStatus = $"'{entity.Name}' has no readable mesh material/color."; return; }

            var partKey = StripDuplicateSuffix(entity.Name ?? "");
            var existing = EditOverrides.FirstOrDefault(o => string.Equals(o.PartKey, partKey, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Material = material ?? existing.Material;
                existing.Color = color ?? existing.Color;
            }
            else
            {
                EditOverrides.Add(MakeOverrideRow(partKey, material, color));
            }
            EditStatus = $"Captured '{partKey}': material='{material}', color='{color}'.";
        }

        public void ExecuteAddManualOverride()
        {
            EditOverrides.Add(MakeOverrideRow("part_name", "", ""));
            EditStatus = "Added a blank override row - type the part key (mesh slot or child entity name), material, and/or color hex.";
        }

        public void ExecuteSaveTextureSet()
        {
            if (string.IsNullOrWhiteSpace(EditNameInput)) { EditStatus = "Enter a name first."; return; }
            if (string.IsNullOrWhiteSpace(EditBasePrefabInput)) { EditStatus = "Enter (or fill from selection) the base prefab name first."; return; }
            if (EditOverrides.Count == 0) { EditStatus = "Add at least one override first (capture from selection, or add manually)."; return; }

            var preset = new ColorPreset { Name = EditNameInput.Trim(), BasePrefabName = EditBasePrefabInput.Trim() };
            foreach (var row in EditOverrides)
            {
                if (string.IsNullOrWhiteSpace(row.PartKey)) continue;
                preset.Overrides[row.PartKey.Trim()] = new ColorPresetOverride
                {
                    Material = string.IsNullOrWhiteSpace(row.Material) ? null : row.Material.Trim(),
                    Color = string.IsNullOrWhiteSpace(row.Color) ? null : row.Color.Trim(),
                };
            }

            ColorPresetStore.Save(preset);
            EditStatus = $"Saved '{preset.Name}' ({preset.Overrides.Count} override(s)).";
            Refresh();
        }

        public void ExecuteClearEditor()
        {
            EditNameInput = "";
            EditBasePrefabInput = "";
            EditOverrides.Clear();
            EditStatus = "Cleared - type a name + base prefab, then add overrides, then Save.";
        }

        private TextureSetOverrideRowVM MakeOverrideRow(string partKey, string material, string color) =>
            new TextureSetOverrideRowVM(partKey, material, color, RemoveOverrideRow);

        private void RemoveOverrideRow(TextureSetOverrideRowVM row) => EditOverrides.Remove(row);

        private static string StripDuplicateSuffix(string name) => Regex.Replace(name ?? "", @"\.\d+$", "");

        private static (string material, string color) GetFirstMeshInfo(GameEntity entity)
        {
            for (int m = 0; m < entity.MultiMeshComponentCount; m++)
            {
                var meta = entity.GetMetaMesh(m);
                if (meta == null || !meta.IsValid) continue;
                for (int i = 0; i < meta.MeshCount; i++)
                {
                    var mesh = meta.GetMeshAtIndex(i);
                    if (mesh == null) continue;
                    var material = mesh.GetMaterial()?.Name;
                    var color = ColorHex.ToHex(mesh.Color);
                    if (material != null || color != null) return (material, color);
                }
            }
            return (null, null);
        }
    }
}

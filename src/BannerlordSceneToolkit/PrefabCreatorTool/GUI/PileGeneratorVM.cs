using System;
using System.Collections.Generic;
using System.Linq;
using PrefabCreatorTool.Backup;
using PrefabCreatorTool.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace PrefabCreatorTool.GUI
{
    // Pile Generator - whitelist a set of prefabs (with count, optional texture set, optional
    // snap-to-surface) and scatter them into one debris pile in a single Generate. Entries are
    // authored top-to-bottom the way the pile should look (first row = top layer, last row = base
    // layer); PileGenerator.Generate is what actually reverses that into placement order. Recipes
    // (the whole entry list + scatter radius) can be saved/loaded by name so a "generic rubble
    // pile" definition doesn't need retyping in every scene.
    public class PileGeneratorVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;

        private MBBindingList<PileEntryRowVM> _entries;
        private string _scatterRadiusInput = "1.5";
        private string _anchorNameInput = "Pile";
        private string _recipeNameInput = "";
        private string _generateStatus = "Add entries below (top row = top layer, bottom row = base layer), select ONE placed entity as the pile's center, then Generate.";
        private bool _isAreaMode;
        private string _placementModeLabel = "Mode: Single Pile (point + radius)";

        private string _recipeSearchTerm = "";
        private MBBindingList<PileRecipeRowVM> _recipeRows;
        private string _recipeStatus = "";

        public PileGeneratorVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _entries = new MBBindingList<PileEntryRowVM>();
            _recipeRows = new MBBindingList<PileRecipeRowVM>();
            RefreshRecipes();
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();

        [DataSourceProperty]
        public MBBindingList<PileEntryRowVM> Entries
        {
            get => _entries;
            set { if (value != _entries) { _entries = value; OnPropertyChangedWithValue(value, nameof(Entries)); } }
        }

        [DataSourceProperty]
        public string ScatterRadiusInput
        {
            get => _scatterRadiusInput;
            set { if (value != _scatterRadiusInput) { _scatterRadiusInput = value; OnPropertyChangedWithValue(value, nameof(ScatterRadiusInput)); } }
        }

        [DataSourceProperty]
        public string AnchorNameInput
        {
            get => _anchorNameInput;
            set { if (value != _anchorNameInput) { _anchorNameInput = value; OnPropertyChangedWithValue(value, nameof(AnchorNameInput)); } }
        }

        [DataSourceProperty]
        public string RecipeNameInput
        {
            get => _recipeNameInput;
            set { if (value != _recipeNameInput) { _recipeNameInput = value; OnPropertyChangedWithValue(value, nameof(RecipeNameInput)); } }
        }

        [DataSourceProperty]
        public string GenerateStatus
        {
            get => _generateStatus;
            set { if (value != _generateStatus) { _generateStatus = value; OnPropertyChangedWithValue(value, nameof(GenerateStatus)); } }
        }

        [DataSourceProperty]
        public string PlacementModeLabel
        {
            get => _placementModeLabel;
            set { if (value != _placementModeLabel) { _placementModeLabel = value; OnPropertyChangedWithValue(value, nameof(PlacementModeLabel)); } }
        }

        [DataSourceProperty]
        public string RecipeSearchTerm
        {
            get => _recipeSearchTerm;
            set { if (value != _recipeSearchTerm) { _recipeSearchTerm = value; OnPropertyChangedWithValue(value, nameof(RecipeSearchTerm)); RefreshRecipes(); } }
        }

        [DataSourceProperty]
        public MBBindingList<PileRecipeRowVM> RecipeRows
        {
            get => _recipeRows;
            set { if (value != _recipeRows) { _recipeRows = value; OnPropertyChangedWithValue(value, nameof(RecipeRows)); } }
        }

        [DataSourceProperty]
        public string RecipeStatus
        {
            get => _recipeStatus;
            set { if (value != _recipeStatus) { _recipeStatus = value; OnPropertyChangedWithValue(value, nameof(RecipeStatus)); } }
        }

        // Single Pile: one reference entity + radius, jittered around a point - the original mode.
        // Scatter Area: the combined top-down footprint of everything selected (built for floors -
        // select the floor tile(s), items spread across the whole thing, not just one corner).
        public void ExecuteTogglePlacementMode()
        {
            _isAreaMode = !_isAreaMode;
            PlacementModeLabel = _isAreaMode ? "Mode: Scatter Area (selected footprint)" : "Mode: Single Pile (point + radius)";
            GenerateStatus = _isAreaMode
                ? "Select the floor entity/entities (one or more) whose combined top-down footprint should be covered, then Generate."
                : "Select ONE placed entity as the pile's center, then Generate.";
        }

        public void ExecuteAddEntry() => Entries.Add(MakeRow(new PileEntry { PrefabName = "", Count = 5, SnapToSurface = true }));

        private PileEntryRowVM MakeRow(PileEntry entry) =>
            new PileEntryRowVM(entry.PrefabName, entry.Count, entry.PresetName, entry.SnapToSurface, OnMoveUp, OnMoveDown, OnRemoveRow, OnCyclePreset)
            { DeletePhysics = entry.DeletePhysics };

        private void OnMoveUp(PileEntryRowVM row)
        {
            var idx = Entries.IndexOf(row);
            if (idx <= 0) return;
            Entries.RemoveAt(idx);
            Entries.Insert(idx - 1, row);
        }

        private void OnMoveDown(PileEntryRowVM row)
        {
            var idx = Entries.IndexOf(row);
            if (idx < 0 || idx >= Entries.Count - 1) return;
            Entries.RemoveAt(idx);
            Entries.Insert(idx + 1, row);
        }

        private void OnRemoveRow(PileEntryRowVM row) => Entries.Remove(row);

        // The Texture cell on each row cycles saved texture sets; this opens the browser that
        // CREATES them, which was previously only reachable from the Prefab Creator panel
        // (2026-08-23, "the texture overrides menu is impossible to reach from the pile
        // generator menu").
        public void ExecuteOpenTextureBrowser() => TextureSetBrowserLayer.Open();

        // The persistence half of Delete Physics (see SavedScenePhysicsStripper for the whole
        // story): save the scene, click this, reload - the runtime strip covers the session,
        // this covers every load after.
        public void ExecuteStripSavedPhysics() => GenerateStatus = SavedScenePhysicsStripper.StripCurrentScene();

        private void OnCyclePreset(PileEntryRowVM row)
        {
            var options = ColorPresetStore.ListForBasePrefab(row.PrefabName).Select(p => p.Name).ToList();
            if (options.Count == 0) { GenerateStatus = $"No saved texture sets for '{row.PrefabName}' yet - blank means it keeps its default look."; return; }

            var currentIdx = options.FindIndex(n => string.Equals(n, row.PresetName, StringComparison.OrdinalIgnoreCase));
            // One step past the last real option cycles back to "None" - otherwise there'd be no
            // way to clear a texture set once one's been picked, short of removing and re-adding
            // the whole row.
            row.PresetName = currentIdx + 1 >= options.Count ? null : options[currentIdx + 1];
        }

        public void ExecuteSaveRecipe()
        {
            if (string.IsNullOrWhiteSpace(RecipeNameInput)) { RecipeStatus = "Type a recipe name first."; return; }
            if (Entries.Count == 0) { RecipeStatus = "Add at least one entry first."; return; }
            if (!float.TryParse(ScatterRadiusInput, out var radius)) radius = 1.5f;

            // PlacementTag rides along with the recipe - a pile that is meant to be findable by
            // tag should stay that way when the recipe is shared, not need retyping on the far end.
            var recipe = new PileRecipe
            {
                Name = RecipeNameInput.Trim(),
                ScatterRadius = radius,
                Entries = ToEntries(),
                PlacementTag = PlacementTagInput?.Trim() ?? "",
            };
            PileRecipeStore.Save(recipe);
            RecipeStatus = $"Saved recipe '{recipe.Name}' ({recipe.Entries.Count} entry(ies)).";
            RefreshRecipes();
        }

        public void ExecuteRefreshRecipes() => RefreshRecipes();

        // --- Recipe import / export (PrefabCreatorTool\Exports\PileRecipes) ---
        public void ExecuteExportRecipes()
        {
            try
            {
                var (exported, dir) = PileRecipeStore.Export();
                RecipeStatus = exported == 0 ? "No saved recipes to export." : $"Exported {exported} recipe(s) to {dir}";
            }
            catch (Exception ex)
            {
                RecipeStatus = "Recipe export failed: " + ex.Message;
                Log.Error("ExportRecipes failed: " + ex);
            }
        }

        public void ExecuteImportRecipes()
        {
            try
            {
                var (imported, skipped, problems) = PileRecipeStore.Import(overwriteExisting: false);
                foreach (var p in problems) Log.Warn("[PileRecipeIO] " + p);
                var note = problems.Count == 0 ? "" : "  Issues: " + string.Join("; ", problems.Take(3))
                           + (problems.Count > 3 ? $" (+{problems.Count - 3} more, see tool.log)" : "");
                RecipeStatus = $"Imported {imported} recipe(s), skipped {skipped}." + note;
                ExecuteRefreshRecipes();
            }
            catch (Exception ex)
            {
                RecipeStatus = "Recipe import failed: " + ex.Message;
                Log.Error("ImportRecipes failed: " + ex);
            }
        }

        public void ExecuteOpenRecipeFolder()
        {
            try { PileRecipeStore.OpenExportFolder(); RecipeStatus = "Opened " + PileRecipeStore.ExportDir; }
            catch (Exception ex) { RecipeStatus = "Couldn't open the folder: " + ex.Message; }
        }



        private void RefreshRecipes()
        {
            RecipeRows.Clear();
            var term = (RecipeSearchTerm ?? "").Trim();
            foreach (var name in PileRecipeStore.ListNames())
            {
                PileRecipe recipe;
                try { recipe = PileRecipeStore.Load(name); }
                catch { continue; }
                if (term.Length > 0 && recipe.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) continue;

                RecipeRows.Add(new PileRecipeRowVM(recipe, RunLoadRecipe, RunDeleteRecipe));
            }
            RecipeStatus = RecipeRows.Count == 0 ? "No saved recipes yet." : $"{RecipeRows.Count} recipe(s).";
        }

        private void RunLoadRecipe(PileRecipeRowVM row)
        {
            Entries.Clear();
            foreach (var entry in row.Recipe.Entries) Entries.Add(MakeRow(entry));
            ScatterRadiusInput = row.Recipe.ScatterRadius.ToString("F2");
            RecipeNameInput = row.Recipe.Name;
            PlacementTagInput = row.Recipe.PlacementTag ?? "";
            RecipeStatus = $"Loaded recipe '{row.Recipe.Name}' ({row.Recipe.Entries.Count} entry(ies)).";
        }

        private void RunDeleteRecipe(PileRecipeRowVM row)
        {
            PileRecipeStore.Delete(row.Recipe.Name);
            RecipeRows.Remove(row);
            RecipeStatus = $"Deleted recipe '{row.Recipe.Name}'.";
        }

        private List<PileEntry> ToEntries() => Entries.Select(r => new PileEntry
        {
            PrefabName = r.PrefabName?.Trim() ?? "",
            Count = r.ParsedCount,
            PresetName = r.PresetName,
            SnapToSurface = r.SnapToSurface,
            DeletePhysics = r.DeletePhysics,
        }).Where(e => !string.IsNullOrWhiteSpace(e.PrefabName)).ToList();

        // Optional tag written onto every piece as it is placed. Set BEFORE generating - it is
        // applied at instantiation, not retro-fitted, so changing it afterwards does not relabel
        // an existing pile.
        [DataSourceProperty]
        public string PlacementTagInput
        {
            get => _placementTagInput;
            set { if (value != _placementTagInput) { _placementTagInput = value; OnPropertyChangedWithValue(value, nameof(PlacementTagInput)); } }
        }
        private string _placementTagInput = "";

        // The pieces from the most recent Generate, so Re-Settle has something to act on when
        // nothing is selected. Cleared implicitly by generating again.
        private List<GameEntity> _lastPlaced = new List<GameEntity>();

        // RANDOMIZE ROTATION (v0.7). Per-axis caps in degrees; each targeted entity draws its
        // own angles from [-cap, +cap] - see RandomizeRotation for the world-axis convention.
        // Z defaults to 180 (yaw is the one you nearly always want randomized), X/Y to 0 so
        // upright things stay upright unless leaning is asked for.
        [DataSourceProperty]
        public string RandomXInput
        {
            get => _randomXInput;
            set { if (value != _randomXInput) { _randomXInput = value; OnPropertyChangedWithValue(value, nameof(RandomXInput)); } }
        }
        private string _randomXInput = "0";

        [DataSourceProperty]
        public string RandomYInput
        {
            get => _randomYInput;
            set { if (value != _randomYInput) { _randomYInput = value; OnPropertyChangedWithValue(value, nameof(RandomYInput)); } }
        }
        private string _randomYInput = "0";

        [DataSourceProperty]
        public string RandomZInput
        {
            get => _randomZInput;
            set { if (value != _randomZInput) { _randomZInput = value; OnPropertyChangedWithValue(value, nameof(RandomZInput)); } }
        }
        private string _randomZInput = "180";

        // Same target resolution as Re-Settle, for the same reasons: the current selection when
        // there is one (anchors expand to their children - randomizing an anchor would spin the
        // whole pile as one lump, when the point is per-piece variation), otherwise the pieces
        // from the last Generate, so "generate, then randomize" needs no reselecting.
        public void ExecuteRandomizeRotation()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { GenerateStatus = "No scene is currently open."; return; }

                float maxX = ParseDegrees(RandomXInput), maxY = ParseDegrees(RandomYInput), maxZ = ParseDegrees(RandomZInput);
                if (maxX <= 0f && maxY <= 0f && maxZ <= 0f)
                {
                    GenerateStatus = "All three axis caps are 0 - nothing to randomize. Type max degrees per axis (e.g. Z 180).";
                    return;
                }

                var selection = EntitySelector.GetManualSelection();
                List<GameEntity> targets;
                string source;
                if (selection.Count > 0)
                {
                    targets = selection.SelectMany(ExpandForSettle).Distinct().ToList();
                    source = $"{selection.Count} selected";
                }
                else if (_lastPlaced.Count > 0)
                {
                    targets = _lastPlaced.Where(e => e != null && e.Pointer != UIntPtr.Zero).ToList();
                    source = "the last generated pile";
                }
                else
                {
                    GenerateStatus = "Select what to randomize, or generate a pile first.";
                    return;
                }

                if (targets.Count == 0) { GenerateStatus = "Nothing left to randomize - those entities are gone."; return; }

                BackupManager.BackupNow("before-apply");
                PrefabSwapperTool.Core.ManipulationWatcher.SuppressSelfEdit();

                // Rotation only, so the captured frames are a complete undo.
                BannerlordSceneToolkit.EditUndo.CaptureFrames($"Randomize rotation ({targets.Count})", targets);

                var result = Core.RandomizeRotation.Apply(targets, maxX, maxY, maxZ);

                var msg = $"Randomized rotation on {result.Rotated} piece(s) from {source} " +
                          $"(caps X {maxX:0.#} / Y {maxY:0.#} / Z {maxZ:0.#} deg). Undo is available.";
                if (result.Failed > 0) msg += $" {result.Failed} failed (see tool.log).";
                GenerateStatus = msg;
                Log.Info($"[Randomize] {result.Rotated} rotated, {result.Failed} failed, caps=({maxX},{maxY},{maxZ})");
            }
            catch (Exception ex)
            {
                GenerateStatus = "Randomize failed: " + ex.Message;
                Log.Error("PileGeneratorVM.ExecuteRandomizeRotation failed: " + ex);
            }
        }

        // Blank or unparseable reads as 0 (that axis untouched); caps clamp to [0, 360] - a cap
        // past 360 adds nothing a 360 cap doesn't already cover.
        private static float ParseDegrees(string text)
        {
            if (!float.TryParse((text ?? "").Trim(), out var v)) return 0f;
            if (v < 0f) v = -v;   // "-30" almost certainly meant "within 30 degrees either way"
            return Math.Min(v, 360f);
        }

        // RE-SETTLE / FLATTEN.
        //
        // Acts on the current editor selection when there is one, otherwise on the pieces from the
        // last Generate. Selection wins because that is the case where you have moved something
        // and want it re-seated; falling back to the last pile covers "generate, then flatten"
        // without making you reselect what the tool just made.
        // Shared with every other tool - see EditUndo. Generating and re-settling are both
        // undoable; nothing here deletes, so nothing here is unrecoverable by it.
        public void ExecuteUndo()
        {
            var (ok, message) = BannerlordSceneToolkit.EditUndo.UndoLast();
            GenerateStatus = message;
            if (!ok) Log.Info("[EditUndo] " + message);
        }

        public void ExecuteResettle()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { GenerateStatus = "No scene is currently open."; return; }

                var selection = EntitySelector.GetManualSelection();
                List<GameEntity> targets;
                string source;

                if (selection.Count > 0)
                {
                    // Settling an anchor would move the whole pile as one lump; its children are
                    // what should each find their own ground.
                    targets = selection.SelectMany(ExpandForSettle).Distinct().ToList();
                    source = $"{selection.Count} selected";
                }
                else if (_lastPlaced.Count > 0)
                {
                    targets = _lastPlaced.Where(e => e != null && e.Pointer != UIntPtr.Zero).ToList();
                    source = "the last generated pile";
                }
                else
                {
                    GenerateStatus = "Select what to settle, or generate a pile first.";
                    return;
                }

                if (targets.Count == 0) { GenerateStatus = "Nothing left to settle - those entities are gone."; return; }

                BackupManager.BackupNow("before-apply");
                PrefabSwapperTool.Core.ManipulationWatcher.SuppressSelfEdit();

                // Frame-only, so the captured frames are a complete undo.
                BannerlordSceneToolkit.EditUndo.CaptureFrames($"Re-settle ({targets.Count})", targets);

                var result = PileGenerator.SettleDown(EntitySelector.CurrentScene, targets);

                var msg = $"Re-settled {result.Moved} piece(s) from {source}.";
                if (result.Missed > 0) msg += $" {result.Missed} had nothing beneath them and were left where they were.";
                if (result.Skipped > 0) msg += $" {result.Skipped} skipped.";
                GenerateStatus = msg;
            }
            catch (Exception ex)
            {
                GenerateStatus = "Re-settle failed: " + ex.Message;
                Log.Error("PileGeneratorVM.ExecuteResettle failed: " + ex);
            }
        }

        // A selected pile anchor holds no geometry of its own - settle its children instead, or
        // the whole pile drops as a rigid block and keeps its internal gaps.
        private static IEnumerable<GameEntity> ExpandForSettle(GameEntity entity)
        {
            if (entity == null || entity.Pointer == UIntPtr.Zero) yield break;
            List<GameEntity> children = null;
            try { children = entity.GetChildren().ToList(); } catch { }
            if (children != null && children.Count > 0)
            {
                foreach (var child in children) yield return child;
                yield break;
            }
            yield return entity;
        }

        public void ExecuteGenerate()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { GenerateStatus = "No scene is currently open."; return; }
                var selection = EntitySelector.GetManualSelection();

                var entries = ToEntries();
                if (entries.Count == 0) { GenerateStatus = "No valid entries - add at least one row with a prefab name typed in."; return; }

                var anchorName = string.IsNullOrWhiteSpace(AnchorNameInput) ? "Pile" : AnchorNameInput.Trim();
                var totalRequested = entries.Sum(e => e.Count);

                PileGenerator.GenerateResult result;
                if (_isAreaMode)
                {
                    if (selection.Count == 0) { GenerateStatus = "Select the floor entity/entities whose footprint should be covered first."; return; }
                    BackupManager.BackupNow("before-apply");
                    result = PileGenerator.GenerateInArea(EntitySelector.CurrentScene, selection, entries, anchorName, PlacementTagInput);
                }
                else
                {
                    if (selection.Count != 1) { GenerateStatus = $"Select exactly ONE placed entity as the pile's center (selected: {selection.Count})."; return; }
                    if (!float.TryParse(ScatterRadiusInput, out var radius) || radius <= 0f) { GenerateStatus = "Scatter Radius must be a positive number."; return; }

                    BackupManager.BackupNow("before-apply");
                    var referenceFrame = selection[0].GetGlobalFrame();
                    result = PileGenerator.Generate(EntitySelector.CurrentScene, referenceFrame, entries, radius, anchorName, PlacementTagInput);
                }

                if (!result.Success) { GenerateStatus = "Failed: " + result.Error; return; }

                var msg = $"Created '{result.AnchorEntity.Name}', placed {result.Placed.Count} of {totalRequested} requested piece(s).";
                if (result.TextureFailed.Count > 0) msg += $" {result.TextureFailed.Count} placed but their texture set failed to apply.";
                if (result.Failed.Count > 0) msg += $" {result.Failed.Count} failed: {string.Join("; ", result.Failed.Take(3))}{(result.Failed.Count > 3 ? "; ..." : "")}";
                if (result.Tagged > 0) msg += $" Tagged {result.Tagged} with '{PlacementTagInput.Trim()}'.";
                if (result.PhysicsCleared > 0) msg += $" Cleared physics on {result.PhysicsCleared} body/bodies.";

                _lastPlaced = result.Placed.ToList();

                // Anchor included: undo has to remove it too, or an empty anchor is left behind
                // looking like a real object (see EditUndo).
                BannerlordSceneToolkit.EditUndo.CaptureCreated($"Generate pile ({result.Placed.Count} piece(s))",
                    result.Placed.Concat(new[] { result.AnchorEntity }));

                // Same reasoning as the swapper: the pieces you just made become the selection,
                // so Re-Settle or a numeric nudge can follow immediately.
                MaterialSwapTool.Core.LiveSceneChecks.SetEditorSelection(result.Placed);
                GenerateStatus = msg;
            }
            catch (Exception ex)
            {
                GenerateStatus = "Generate failed: " + ex.Message;
                Log.Error("PileGeneratorVM.ExecuteGenerate failed: " + ex);
            }
        }
    }
}

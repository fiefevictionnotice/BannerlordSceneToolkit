using System;
using System.Collections.Generic;
using System.Linq;
using PrefabCreatorTool.Backup;
using PrefabCreatorTool.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace PrefabCreatorTool.GUI
{
    public class PrefabCreatorVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;

        // --- Prefab Swapper, hosted here since v0.7 -----------------------------------------
        //
        // F6 had drifted into being two unrelated panels sharing a hotkey: a swapper at the top
        // and, below it, everything to do with distribution. Swapping belongs with prefab
        // authoring - it is the same job as building and re-originating a prefab, just applied to
        // one already placed - so it moved here and F6 is now purely Distribution.
        //
        // FORWARDED, NOT REIMPLEMENTED. The swap orchestration (child re-parenting, mesh-override
        // capture, the not-a-real-prefab refusal, history logging) is several hundred lines that
        // took real crashes to get right; copying it here to serve a different panel would be the
        // worst possible reason to fork it. This holds a private PrefabSwapperVM and mirrors just
        // the handful of properties and commands the panel binds to. Its close/drag actions are
        // no-ops: this instance never owns a window of its own.
        private readonly PrefabSwapperTool.GUI.PrefabSwapperVM _swap =
            new PrefabSwapperTool.GUI.PrefabSwapperVM(() => { }, () => { });

        [DataSourceProperty]
        public string OldPrefabName
        {
            get => _swap.OldPrefabName;
            set { if (value != _swap.OldPrefabName) { _swap.OldPrefabName = value; OnPropertyChangedWithValue(value, nameof(OldPrefabName)); } }
        }

        [DataSourceProperty]
        public string NewPrefabName
        {
            get => _swap.NewPrefabName;
            set { if (value != _swap.NewPrefabName) { _swap.NewPrefabName = value; OnPropertyChangedWithValue(value, nameof(NewPrefabName)); } }
        }

        [DataSourceProperty]
        public string SwapStatusText => _swap.StatusText;

        [DataSourceProperty]
        public string SwapSelectionInfoText => _swap.SelectionInfoText;

        [DataSourceProperty]
        public string LiveReferenceLabel => _swap.LiveReferenceLabel;

        // The inner VM raises its own property-changed notifications against ITSELF, and nothing
        // is bound to that instance - so after any forwarded command the panel has to be told
        // which of the mirrored properties may have moved.
        private void RefreshSwapBindings()
        {
            OnPropertyChanged(nameof(OldPrefabName));
            OnPropertyChanged(nameof(NewPrefabName));
            OnPropertyChanged(nameof(SwapStatusText));
            OnPropertyChanged(nameof(SwapSelectionInfoText));
            OnPropertyChanged(nameof(LiveReferenceLabel));
            OnPropertyChanged(nameof(SwapModeLabel));
            OnPropertyChanged(nameof(SwapModeColor));
            OnPropertyChanged(nameof(SwapScaleLabel));
            OnPropertyChanged(nameof(SwapScaleColor));
        }

        [DataSourceProperty]
        public string SwapModeLabel => _swap.SwapModeLabel;

        [DataSourceProperty]
        public string SwapModeColor => _swap.SwapModeColor;

        public void ExecuteToggleSwapMode() { _swap.ExecuteToggleSwapMode(); RefreshSwapBindings(); }

        [DataSourceProperty]
        public string SwapScaleLabel => _swap.SwapScaleLabel;

        [DataSourceProperty]
        public string SwapScaleColor => _swap.SwapScaleColor;

        public void ExecuteToggleSwapScale() { _swap.ExecuteToggleSwapScale(); RefreshSwapBindings(); }

        // Extra Z rotation (degrees) and uniform scale multiplier for swapped-in entities -
        // proxied through to PrefabSwapperVM, which writes LivePrefabSwapper's statics, so
        // regular swaps, live-reference swaps, and swap-set Apply all honour them.
        [DataSourceProperty]
        public string SwapZRotInput
        {
            get => _swap.SwapZRotInput;
            set { if (value != _swap.SwapZRotInput) { _swap.SwapZRotInput = value; OnPropertyChangedWithValue(value, nameof(SwapZRotInput)); } }
        }

        [DataSourceProperty]
        public string SwapScaleMultInput
        {
            get => _swap.SwapScaleMultInput;
            set { if (value != _swap.SwapScaleMultInput) { _swap.SwapScaleMultInput = value; OnPropertyChangedWithValue(value, nameof(SwapScaleMultInput)); } }
        }

        public void ExecuteRepairFlaggedCopies() { _swap.ExecuteRepairFlaggedCopies(); RefreshSwapBindings(); }

        public void ExecuteSwapFillFromSelection() { _swap.ExecuteFillFromSelection(); RefreshSwapBindings(); }
        public void ExecuteSwapFillNewFromSelection() { _swap.ExecuteFillNewFromSelection(); RefreshSwapBindings(); }
        public void ExecuteSwapSelected() { _swap.ExecuteSwapSelected(); RefreshSwapBindings(); }
        public void ExecuteSwapAllMatching() { _swap.ExecuteSwapAllMatching(); RefreshSwapBindings(); }
        public void ExecuteSetLiveReference() { _swap.ExecuteSetLiveReference(); RefreshSwapBindings(); }
        public void ExecuteSwapSelectedToLiveReference() { _swap.ExecuteSwapSelectedToLiveReference(); RefreshSwapBindings(); }
        public void ExecuteUndoLastSwap() { _swap.ExecuteUndoLastSwap(); RefreshSwapBindings(); }
        public void ExecuteOpenSwapHistory() { _swap.ExecuteOpenHistory(); }
        public void ExecuteOpenSwapSets() { _swap.ExecuteOpenSwapSets(); }

        // --- Naming prefix (applies to every generated name across all modes) ---
        private string _namingPrefixInput = PrefabNaming.Prefix;

        // --- Mode 1: New Prefab ---
        private string _newPrefabBaseName = "";
        private string _newPrefabStatus = "Select entities in the editor, type a base name, then Create Anchor.";

        // --- Match Secondary Origin to Primary ---
        private GameEntity _primaryAnchorEntity;
        private string _primaryAnchorLabel = "Primary: none set";

        // --- Mode 2: Identify Variations (both detectors share these scan settings) ---
        private string _requiredTagInput = "fief_furniture";
        private string _clusterRadiusInput = "8";
        private string _positionToleranceInput = "2";
        private string _rotationToleranceInput = "5";
        // Was hardcoded (scale) or nonexistent (composition similarity) - both are real detector
        // parameters now, exposed the same way position/rotation tolerance already are.
        private string _scaleToleranceInput = "0.15";
        private string _minCompositionSimilarityInput = "0.75";
        private bool _fuzzyMatching = true;
        private string _fuzzyLabel = "Fuzzy Matching: On";
        private string _namingModeLabel = "Variant Naming: " + PrefabNaming.NamingMode;
        private string _detectStatus = "";
        private MBBindingList<VariantGroupRowVM> _detectedGroups;
        private MBBindingList<InstanceGroupRowVM> _detectedInstanceGroups;

        // --- Color Presets ---
        private string _presetNameInput = "";
        private string _presetStatus = "";
        private MBBindingList<CulturePickItemVMLite> _savedPresets;

        public PrefabCreatorVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _detectedGroups = new MBBindingList<VariantGroupRowVM>();
            _detectedInstanceGroups = new MBBindingList<InstanceGroupRowVM>();
            _savedPresets = new MBBindingList<CulturePickItemVMLite>();
            RefreshSavedPresets();
        }

        // --- Origin to Anchor -----------------------------------------------------------------
        //
        // Moves a prefab's ORIGIN to a point you pick, without moving the prefab itself. A prefab
        // whose origin sits somewhere unhelpful misbehaves in every tool that reasons about
        // position - mirror reflects the origin, distribute spaces from it, rotate turns about it -
        // so a bad origin makes those look broken when the maths was right.
        //
        // Workflow mirrors the Mirror tool's custom point deliberately: select the thing that marks
        // where the origin should go, click Fill From Selection, then select the prefab and click
        // Origin to Anchor.
        private string _originAnchorInput = "";
        private string _originStatus = "Fill an anchor point, select the prefab, then Origin to Anchor.";

        [DataSourceProperty]
        public string OriginAnchorInput
        {
            get => _originAnchorInput;
            set { if (value != _originAnchorInput) { _originAnchorInput = value; OnPropertyChangedWithValue(value, nameof(OriginAnchorInput)); } }
        }

        [DataSourceProperty]
        public string OriginStatus
        {
            get => _originStatus;
            set { if (value != _originStatus) { _originStatus = value; OnPropertyChangedWithValue(value, nameof(OriginStatus)); } }
        }

        public void ExecuteFillOriginAnchorFromSelection()
        {
            var selection = MaterialSwapTool.Core.SelectionMemory.GetSelection();
            if (selection.Count != 1)
            {
                OriginStatus = $"Select exactly ONE entity to read the anchor point from (selected: {selection.Count}).";
                return;
            }

            var p = selection[0].GetGlobalFrame().origin;
            OriginAnchorInput = $"{p.x:0.###}, {p.y:0.###}, {p.z:0.###}";
            OriginStatus = $"Anchor point set to '{selection[0].Name}' at {OriginAnchorInput}. Now select the prefab.";
        }

        public void ExecuteOriginToAnchor() => RunOriginToAnchor(force: false);

        private void RunOriginToAnchor(bool force)
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { OriginStatus = "No scene is currently open."; return; }

                if (!TryParseVec3(OriginAnchorInput, out var anchor))
                {
                    OriginStatus = "Anchor point must be 'x, y, z' - use Fill From Selection.";
                    return;
                }

                var selection = MaterialSwapTool.Core.SelectionMemory.GetSelection();
                if (selection.Count != 1)
                {
                    OriginStatus = $"Select exactly ONE prefab whose origin should move (selected: {selection.Count}).";
                    return;
                }

                BackupManager.BackupNow("before-apply");

                var result = OriginToAnchor.MoveOriginTo(selection[0], anchor, force);

                if (result.NeedsConfirmation)
                {
                    // Not an empty - moving it moves a real object, so it asks first. Children are
                    // held in place either way; the question is only about the parent itself.
                    var target = selection[0];
                    InformationManager.ShowInquiry(new InquiryData(
                        "Move a non-empty entity?",
                        result.ConfirmationPrompt,
                        isAffirmativeOptionShown: true,
                        isNegativeOptionShown: true,
                        affirmativeText: "Move it",
                        negativeText: "Cancel",
                        affirmativeAction: () =>
                        {
                            var forced = OriginToAnchor.MoveOriginTo(target, anchor, force: true);
                            OriginStatus = forced.Message;
                        },
                        negativeAction: () => { OriginStatus = "Cancelled - nothing moved."; }));
                    OriginStatus = "Waiting for confirmation...";
                    return;
                }

                OriginStatus = result.Message;
            }
            catch (Exception ex)
            {
                OriginStatus = "Origin to Anchor failed: " + ex.Message;
                Log.Error("OriginToAnchor failed: " + ex);
            }
        }

        // --- Auto Origin (v0.7) ------------------------------------------------------------
        //
        // Two cycle buttons rather than seven preset buttons, matching the Local/World +
        // component pair the Prefab Swapper already uses for its axes: one picks the AXIS, one
        // picks WHICH END of it, and a label spells out the preset those two make so nobody has
        // to hold the naming rule in their head. Every selected prefab is measured and moved on
        // its own box - see OriginPresets.
        private OriginPresets.Axis _originAxis = OriginPresets.Axis.Z;
        private OriginPresets.Side _originSide = OriginPresets.Side.Min;

        [DataSourceProperty]
        public string OriginAxisLabel => $"Axis: {_originAxis}";

        [DataSourceProperty]
        public string OriginSideLabel => $"Side: {_originSide}";

        [DataSourceProperty]
        public string OriginPresetLabel => "= " + OriginPresets.FriendlyName(_originAxis, _originSide);

        public void ExecuteCycleOriginAxis()
        {
            _originAxis = _originAxis == OriginPresets.Axis.X ? OriginPresets.Axis.Y
                        : _originAxis == OriginPresets.Axis.Y ? OriginPresets.Axis.Z
                        : OriginPresets.Axis.X;
            OnPropertyChanged(nameof(OriginAxisLabel));
            OnPropertyChanged(nameof(OriginPresetLabel));
        }

        public void ExecuteCycleOriginSide()
        {
            _originSide = _originSide == OriginPresets.Side.Min ? OriginPresets.Side.Center
                        : _originSide == OriginPresets.Side.Center ? OriginPresets.Side.Max
                        : OriginPresets.Side.Min;
            OnPropertyChanged(nameof(OriginSideLabel));
            OnPropertyChanged(nameof(OriginPresetLabel));
        }

        public void ExecuteApplyOriginPreset()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { OriginStatus = "No scene is currently open."; return; }

                var selection = MaterialSwapTool.Core.SelectionMemory.GetSelection();
                if (selection.Count == 0)
                {
                    OriginStatus = "Select the prefab(s) whose origins should move. Each one is measured " +
                                   "and moved on its own - press Ctrl+Shift+P first if you clicked a child part.";
                    return;
                }

                BackupManager.BackupNow("before-apply");
                PrefabSwapperTool.Core.ManipulationWatcher.SuppressSelfEdit();

                // Roots AND their direct children: the children are written back to the frames
                // they already had, but capturing them makes undo an exact inverse.
                var undoTargets = OriginPresets.CollectForUndo(selection);
                BannerlordSceneToolkit.EditUndo.CaptureFrames(
                    $"Origin -> {OriginPresets.FriendlyName(_originAxis, _originSide)} ({selection.Count})", undoTargets);

                var result = OriginPresets.ApplyToEach(selection, _originAxis, _originSide);

                // Nothing moved - drop the undo step rather than leave a no-op that would eat
                // the next Undo press.
                if (result.Moved == 0) BannerlordSceneToolkit.EditUndo.DiscardLast();

                var preset = OriginPresets.FriendlyName(_originAxis, _originSide);
                var msg = result.Moved > 0
                    ? $"Moved {result.Moved} origin(s) to {preset}. The prefabs themselves did not move. Undo is available."
                    : $"Nothing moved to {preset}.";

                // Meshed entities are the common "why did nothing happen" case, so they are named
                // rather than just counted - see the class comment in OriginPresets for why they
                // cannot be re-origined at all.
                if (result.SkippedMeshed.Count > 0)
                {
                    msg += $" Skipped {result.SkippedMeshed.Count} with geometry of their own (" +
                           string.Join(", ", result.SkippedMeshed.Take(2)) +
                           (result.SkippedMeshed.Count > 2 ? ", ..." : "") +
                           ") - moving those would move the object, not just its origin.";
                }
                if (result.Failed > 0)
                    msg += $" {result.Failed} failed: {string.Join("; ", result.Problems.Take(2))}";

                OriginStatus = msg;
            }
            catch (Exception ex)
            {
                OriginStatus = "Auto origin failed: " + ex.Message;
                Log.Error("ApplyOriginPreset failed: " + ex);
            }
        }

        // Same format the Mirror tool's custom point accepts.
        private static bool TryParseVec3(string text, out Vec3 result)
        {
            result = default(Vec3);
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return false;

            if (!float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)) return false;
            if (!float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)) return false;
            if (!float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z)) return false;

            result = new Vec3(x, y, z, 0f);
            return true;
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();

        // RETIRED FROM THE UI (still compile, just unreachable - no button in
        // PrefabCreatorPanel.xml calls either anymore). Texture Sets and Pairing Browser (and
        // Family Browser, only ever reachable from Pairing Browser's own button) pulled after
        // repeated instability in this area, including a confirmed live crash (ArgumentException in
        // ComboMemberRowVM's Auto-Place notification, since fixed) plus general fragility. Left in
        // place rather than deleted so a future revival has something to start from.
        public void ExecuteOpenTextureSetBrowser() => TextureSetBrowserLayer.Toggle();
        public void ExecuteOpenPairingBrowser() => PairingBrowserLayer.Toggle();

        public void ExecuteOpenPileGenerator() => PileGeneratorLayer.Toggle();

        // Applies (and persists) on every keystroke, same as any other two-way-bound text field in
        // this tool - it's just a string, nothing to validate or fail on, and every naming call
        // site reads PrefabNaming.Prefix fresh each time it's used.
        [DataSourceProperty]
        public string NamingPrefixInput
        {
            get => _namingPrefixInput;
            set
            {
                if (value == _namingPrefixInput) return;
                _namingPrefixInput = value;
                OnPropertyChangedWithValue(value, nameof(NamingPrefixInput));
                PrefabNaming.Prefix = value;
            }
        }

        [DataSourceProperty]
        public string NewPrefabBaseName
        {
            get => _newPrefabBaseName;
            set { if (value != _newPrefabBaseName) { _newPrefabBaseName = value; OnPropertyChangedWithValue(value, nameof(NewPrefabBaseName)); } }
        }

        [DataSourceProperty]
        public string NewPrefabStatus
        {
            get => _newPrefabStatus;
            set { if (value != _newPrefabStatus) { _newPrefabStatus = value; OnPropertyChangedWithValue(value, nameof(NewPrefabStatus)); } }
        }

        [DataSourceProperty]
        public string PrimaryAnchorLabel
        {
            get => _primaryAnchorLabel;
            set { if (value != _primaryAnchorLabel) { _primaryAnchorLabel = value; OnPropertyChangedWithValue(value, nameof(PrimaryAnchorLabel)); } }
        }

        [DataSourceProperty]
        public string RequiredTagInput
        {
            get => _requiredTagInput;
            set { if (value != _requiredTagInput) { _requiredTagInput = value; OnPropertyChangedWithValue(value, nameof(RequiredTagInput)); } }
        }

        [DataSourceProperty]
        public string ClusterRadiusInput
        {
            get => _clusterRadiusInput;
            set { if (value != _clusterRadiusInput) { _clusterRadiusInput = value; OnPropertyChangedWithValue(value, nameof(ClusterRadiusInput)); } }
        }

        [DataSourceProperty]
        public string PositionToleranceInput
        {
            get => _positionToleranceInput;
            set { if (value != _positionToleranceInput) { _positionToleranceInput = value; OnPropertyChangedWithValue(value, nameof(PositionToleranceInput)); } }
        }

        [DataSourceProperty]
        public string RotationToleranceInput
        {
            get => _rotationToleranceInput;
            set { if (value != _rotationToleranceInput) { _rotationToleranceInput = value; OnPropertyChangedWithValue(value, nameof(RotationToleranceInput)); } }
        }

        [DataSourceProperty]
        public string ScaleToleranceInput
        {
            get => _scaleToleranceInput;
            set { if (value != _scaleToleranceInput) { _scaleToleranceInput = value; OnPropertyChangedWithValue(value, nameof(ScaleToleranceInput)); } }
        }

        [DataSourceProperty]
        public string MinCompositionSimilarityInput
        {
            get => _minCompositionSimilarityInput;
            set { if (value != _minCompositionSimilarityInput) { _minCompositionSimilarityInput = value; OnPropertyChangedWithValue(value, nameof(MinCompositionSimilarityInput)); } }
        }

        [DataSourceProperty]
        public string FuzzyLabel
        {
            get => _fuzzyLabel;
            set { if (value != _fuzzyLabel) { _fuzzyLabel = value; OnPropertyChangedWithValue(value, nameof(FuzzyLabel)); } }
        }

        [DataSourceProperty]
        public string DetectStatus
        {
            get => _detectStatus;
            set { if (value != _detectStatus) { _detectStatus = value; OnPropertyChangedWithValue(value, nameof(DetectStatus)); } }
        }

        [DataSourceProperty]
        public MBBindingList<VariantGroupRowVM> DetectedGroups
        {
            get => _detectedGroups;
            set { if (value != _detectedGroups) { _detectedGroups = value; OnPropertyChangedWithValue(value, nameof(DetectedGroups)); } }
        }

        [DataSourceProperty]
        public MBBindingList<InstanceGroupRowVM> DetectedInstanceGroups
        {
            get => _detectedInstanceGroups;
            set { if (value != _detectedInstanceGroups) { _detectedInstanceGroups = value; OnPropertyChangedWithValue(value, nameof(DetectedInstanceGroups)); } }
        }

        [DataSourceProperty]
        public string PresetNameInput
        {
            get => _presetNameInput;
            set { if (value != _presetNameInput) { _presetNameInput = value; OnPropertyChangedWithValue(value, nameof(PresetNameInput)); } }
        }

        [DataSourceProperty]
        public string PresetStatus
        {
            get => _presetStatus;
            set { if (value != _presetStatus) { _presetStatus = value; OnPropertyChangedWithValue(value, nameof(PresetStatus)); } }
        }

        [DataSourceProperty]
        public MBBindingList<CulturePickItemVMLite> SavedPresets
        {
            get => _savedPresets;
            set { if (value != _savedPresets) { _savedPresets = value; OnPropertyChangedWithValue(value, nameof(SavedPresets)); } }
        }

        // RETIRED FROM THE UI (still compiles, just unreachable - Identify Variations' buttons and
        // both results lists were removed from PrefabCreatorPanel.xml, didn't work right at all per
        // live testing). ExecuteToggleFuzzy/ExecuteCycleNamingMode/ExecuteScanForVariants/
        // ExecuteScanForInstanceVariants and the underlying detectors are left in place rather than
        // deleted, so a future revival has something to start from.
        public void ExecuteToggleFuzzy()
        {
            _fuzzyMatching = !_fuzzyMatching;
            FuzzyLabel = _fuzzyMatching ? "Fuzzy Matching: On" : "Fuzzy Matching: Off (exact)";
        }

        [DataSourceProperty]
        public string NamingModeLabel
        {
            get => _namingModeLabel;
            set { if (value != _namingModeLabel) { _namingModeLabel = value; OnPropertyChangedWithValue(value, nameof(NamingModeLabel)); } }
        }

        // Cycles which naming scheme a detected variant's tag comes from - Letters (_B, _C, _D...),
        // Numbers (_Variant2, _Variant3...), or ColorOrMaterial (tries the variant's own material
        // name first, e.g. "_Redbrown", falling back to a letter only if nothing usable can be
        // inferred from it).
        public void ExecuteCycleNamingMode()
        {
            PrefabNaming.NamingMode = PrefabNaming.NamingMode switch
            {
                PrefabNaming.VariantNamingMode.ColorOrMaterial => PrefabNaming.VariantNamingMode.Letters,
                PrefabNaming.VariantNamingMode.Letters => PrefabNaming.VariantNamingMode.Numbers,
                _ => PrefabNaming.VariantNamingMode.ColorOrMaterial,
            };
            NamingModeLabel = "Variant Naming: " + PrefabNaming.NamingMode;
        }

        // ---- Mode 1: New Prefab ----
        public void ExecuteCreateAnchor()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { NewPrefabStatus = "No scene is currently open."; return; }
                if (string.IsNullOrWhiteSpace(NewPrefabBaseName)) { NewPrefabStatus = "Enter a base name first."; return; }

                var selection = EntitySelector.GetManualSelection();
                if (selection.Count == 0) { NewPrefabStatus = "Nothing selected - select the entities that make up this prefab first."; return; }

                BackupManager.BackupNow("before-apply");
                var result = PrefabCreatorEngine.CreateNewPrefabAnchor(EntitySelector.CurrentScene, selection, NewPrefabBaseName);

                NewPrefabStatus = result.Success
                    ? $"Created '{result.AnchorEntity.Name}' at the selection's bottom-center pivot. Tagged {result.TaggedCount} entit{(result.TaggedCount == 1 ? "y" : "ies")} with '{PrefabCreatorEngine.ReselectTagPrefix}{PrefabNaming.SanitizeBaseName(NewPrefabBaseName)}' - search that tag in the editor to re-select everything, then use the editor's own Create Prefab command."
                    : "Failed: " + result.Error;
            }
            catch (Exception ex)
            {
                NewPrefabStatus = "Failed: " + ex.Message;
                Log.Error("CreateAnchor failed: " + ex);
            }
        }

        // ---- Match Secondary Origin to Primary ----
        // Captures whatever's currently selected (must be exactly one entity - the primary's own
        // empty/anchor) to be used as the target origin below.
        public void ExecuteSetPrimaryAnchor()
        {
            if (!EntitySelector.HasOpenScene) { NewPrefabStatus = "No scene is currently open."; return; }

            var selection = EntitySelector.GetManualSelection();
            if (selection.Count != 1)
            {
                NewPrefabStatus = $"Select exactly one entity to use as the primary - {selection.Count} selected.";
                return;
            }

            _primaryAnchorEntity = selection[0];
            PrimaryAnchorLabel = $"Primary: {_primaryAnchorEntity.Name}";
            NewPrefabStatus = $"Primary set to '{_primaryAnchorEntity.Name}'. Select the secondary's empty/anchor and click Match Secondary Origin to Primary.";
        }

        // Moves the currently-selected (secondary) anchor's own origin to match the primary's,
        // without moving the secondary's visible content - see PivotAligner for how.
        public void ExecuteMatchSecondaryOrigin()
        {
            if (!EntitySelector.HasOpenScene) { NewPrefabStatus = "No scene is currently open."; return; }
            if (_primaryAnchorEntity == null || !EntitySelector.IsValidEntity(_primaryAnchorEntity))
            {
                NewPrefabStatus = "Set a primary anchor first.";
                return;
            }

            var selection = EntitySelector.GetManualSelection();
            if (selection.Count != 1)
            {
                NewPrefabStatus = $"Select exactly one entity to use as the secondary - {selection.Count} selected.";
                return;
            }

            var secondary = selection[0];
            if (secondary.Pointer == _primaryAnchorEntity.Pointer)
            {
                NewPrefabStatus = "Selection is the primary anchor itself - select the secondary's anchor instead.";
                return;
            }

            try
            {
                BackupManager.BackupNow("before-apply");
                var result = PivotAligner.MatchSecondaryOriginToPrimary(_primaryAnchorEntity, secondary);
                NewPrefabStatus = result.Success
                    ? $"Moved '{secondary.Name}''s origin to match '{_primaryAnchorEntity.Name}''s. {result.ChildrenRepositioned} child entit(y/ies) held their world position."
                      + (string.IsNullOrEmpty(result.Warning) ? "" : "  NOTE: " + result.Warning)
                    : "Failed: " + result.Error;
            }
            catch (Exception ex)
            {
                NewPrefabStatus = "Failed: " + ex.Message;
                Log.Error("MatchSecondaryOrigin failed: " + ex);
            }
        }

        // ---- Mode 2a: Identify Variations (multi-entity clusters) ----
        public void ExecuteScanForVariants()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { DetectStatus = "No scene is currently open."; return; }

                var options = new DetectorOptions { FuzzyMatching = _fuzzyMatching };
                if (double.TryParse(ClusterRadiusInput, out var radius)) options.ClusterRadius = radius;
                if (double.TryParse(PositionToleranceInput, out var posT)) options.PositionToleranceFuzzy = posT;
                if (double.TryParse(RotationToleranceInput, out var rotT)) options.RotationToleranceDegreesFuzzy = rotT;
                if (double.TryParse(ScaleToleranceInput, out var scaleT)) options.ScaleToleranceFuzzy = scaleT;
                if (double.TryParse(MinCompositionSimilarityInput, out var simT)) options.MinCompositionSimilarity = simT;

                var all = FilteredEntities();
                var groups = VariantDetector.DetectVariantGroups(all, options);

                DetectedGroups.Clear();
                int idx = 1;
                foreach (var group in groups)
                {
                    DetectedGroups.Add(new VariantGroupRowVM(group, "Group" + idx, RunApplyVariantGroup, RunSaveClusterPreset));
                    idx++;
                }

                DetectStatus = groups.Count == 0
                    ? "No cluster variant groups found - try widening Cluster Radius or the fuzzy tolerances."
                    : $"Found {groups.Count} cluster variant group(s) below.";
            }
            catch (Exception ex)
            {
                DetectStatus = "Scan failed: " + ex.Message;
                Log.Error("ScanForVariants failed: " + ex);
            }
        }

        // ---- Mode 2b: Identify Variations (same-prefab-instance color/material diff) ----
        public void ExecuteScanForInstanceVariants()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { DetectStatus = "No scene is currently open."; return; }

                var all = FilteredEntities();
                var groups = PrefabInstanceColorDetector.Detect(all, null); // already tag-filtered by FilteredEntities

                DetectedInstanceGroups.Clear();
                foreach (var group in groups)
                    DetectedInstanceGroups.Add(new InstanceGroupRowVM(group, group.Anchor.Name, RunRenameInstanceGroup, RunSaveInstancePreset));

                DetectStatus = groups.Count == 0
                    ? "No same-prefab color/material variants found (checks entities sharing the same name/prefab for differing mesh colors or materials)."
                    : $"Found {groups.Count} same-prefab variant group(s) below.";
            }
            catch (Exception ex)
            {
                DetectStatus = "Scan failed: " + ex.Message;
                Log.Error("ScanForInstanceVariants failed: " + ex);
            }
        }

        private List<GameEntity> FilteredEntities()
        {
            var all = EntitySelector.CollectAll();
            var tag = (RequiredTagInput ?? "").Trim();
            return tag.Length == 0 ? all : all.Where(e => e.HasTag(tag)).ToList();
        }

        private void RunApplyVariantGroup(VariantGroupRowVM row)
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { DetectStatus = "No scene is currently open."; return; }
                if (string.IsNullOrWhiteSpace(row.BaseNameInput)) { DetectStatus = "Enter a base name for that group first."; return; }

                BackupManager.BackupNow("before-apply");
                var result = PrefabCreatorEngine.ApplyVariantGroup(EntitySelector.CurrentScene, row.Group, row.BaseNameInput);

                var msg = $"Created '{result.AnchorEntity?.Name}' + {result.VariantAnchorEntities.Count} variant anchor(s): " +
                          string.Join(", ", result.VariantAnchorEntities.Select(a => a.Name));
                if (result.Warnings.Count > 0) msg += $" ({result.Warnings.Count} warning(s), see log)";
                foreach (var w in result.Warnings) Log.Warn(w);
                DetectStatus = msg;

                DetectedGroups.Remove(row);
            }
            catch (Exception ex)
            {
                DetectStatus = "Apply failed: " + ex.Message;
                Log.Error("ApplyVariantGroup failed: " + ex);
            }
        }

        private void RunSaveClusterPreset(VariantGroupRowVM row)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(row.BaseNameInput)) { DetectStatus = "Enter a base name for that group first (used as the preset name)."; return; }
                int saved = 0;
                foreach (var (cluster, parts) in row.Group.Variants)
                {
                    var material = parts.FirstOrDefault(p => p.MaterialDiffers)?.VariantMaterial;
                    var tag = PrefabNaming.NextVariantTag(saved + 2, material);
                    var presetName = PrefabNaming.VariantName(row.BaseNameInput, tag);
                    var preset = ColorPresetApplier.FromClusterDiff(presetName, PrefabNaming.AnchorName(row.BaseNameInput), parts);
                    ColorPresetStore.Save(preset);
                    saved++;
                }
                RefreshSavedPresets();
                DetectStatus = $"Saved {saved} preset(s) from that group's variant(s).";
            }
            catch (Exception ex)
            {
                DetectStatus = "Save preset failed: " + ex.Message;
                Log.Error("RunSaveClusterPreset failed: " + ex);
            }
        }

        private void RunRenameInstanceGroup(InstanceGroupRowVM row)
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { DetectStatus = "No scene is currently open."; return; }
                if (string.IsNullOrWhiteSpace(row.BaseNameInput)) { DetectStatus = "Enter a base name for that group first."; return; }

                BackupManager.BackupNow("before-apply");
                row.Group.Anchor.Name = PrefabNaming.AnchorName(row.BaseNameInput);
                row.Group.Anchor.AddTag(PrefabCreatorEngine.ReselectTagPrefix + PrefabNaming.SanitizeBaseName(row.BaseNameInput));

                var names = new List<string> { row.Group.Anchor.Name };
                int i = 2;
                foreach (var (entity, diffs) in row.Group.Variants)
                {
                    var material = diffs.FirstOrDefault(d => d.MaterialDiffers)?.VariantMaterial;
                    var tag = PrefabNaming.NextVariantTag(i, material);
                    entity.Name = PrefabNaming.VariantName(row.BaseNameInput, tag);
                    entity.AddTag(PrefabCreatorEngine.ReselectTagPrefix + PrefabNaming.SanitizeBaseName(row.BaseNameInput));
                    names.Add(entity.Name);
                    i++;
                }

                DetectStatus = "Renamed: " + string.Join(", ", names);
                DetectedInstanceGroups.Remove(row);
            }
            catch (Exception ex)
            {
                DetectStatus = "Rename failed: " + ex.Message;
                Log.Error("RunRenameInstanceGroup failed: " + ex);
            }
        }

        private void RunSaveInstancePreset(InstanceGroupRowVM row)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(row.BaseNameInput)) { DetectStatus = "Enter a base name for that group first."; return; }
                int saved = 0;
                foreach (var (entity, diffs) in row.Group.Variants)
                {
                    var material = diffs.FirstOrDefault(d => d.MaterialDiffers)?.VariantMaterial;
                    var tag = PrefabNaming.NextVariantTag(saved + 2, material);
                    var presetName = PrefabNaming.VariantName(row.BaseNameInput, tag);
                    var preset = ColorPresetApplier.FromInstanceDiff(presetName, PrefabNaming.AnchorName(row.BaseNameInput), diffs);
                    ColorPresetStore.Save(preset);
                    saved++;
                }
                RefreshSavedPresets();
                DetectStatus = $"Saved {saved} preset(s) from that group's variant(s).";
            }
            catch (Exception ex)
            {
                DetectStatus = "Save preset failed: " + ex.Message;
                Log.Error("RunSaveInstancePreset failed: " + ex);
            }
        }

        // ---- Color Presets: apply a saved preset to the current selection ----
        private void RefreshSavedPresets()
        {
            SavedPresets.Clear();
            foreach (var name in ColorPresetStore.ListNames())
                SavedPresets.Add(new CulturePickItemVMLite(name, ApplyPresetByName));
        }

        private void ApplyPresetByName(string name)
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { PresetStatus = "No scene is currently open."; return; }
                var selection = EntitySelector.GetManualSelection();
                if (selection.Count == 0) { PresetStatus = "Nothing selected."; return; }

                var preset = ColorPresetStore.Load(name);
                BackupManager.BackupNow("before-apply");

                int totalApplied = 0;
                foreach (var e in selection)
                    totalApplied += ColorPresetApplier.Apply(e, preset);

                PresetStatus = $"Applied '{name}' to {selection.Count} entit{(selection.Count == 1 ? "y" : "ies")} - {totalApplied} override(s) matched.";
            }
            catch (Exception ex)
            {
                PresetStatus = "Apply preset failed: " + ex.Message;
                Log.Error("ApplyPresetByName failed: " + ex);
            }
        }

        // RETIRED FROM THE UI (still compiles, just unreachable - the Color Presets section and its
        // ExecuteChoose-bound saved-presets list were removed from PrefabCreatorPanel.xml along
        // with Texture Sets/Pairing Browser). Left in place rather than deleted.
        public void ExecuteDeletePreset()
        {
            if (string.IsNullOrWhiteSpace(PresetNameInput)) { PresetStatus = "Enter (or click) a preset name first."; return; }
            ColorPresetStore.Delete(PresetNameInput);
            RefreshSavedPresets();
            PresetStatus = $"Deleted preset '{PresetNameInput}'.";
        }
    }
}

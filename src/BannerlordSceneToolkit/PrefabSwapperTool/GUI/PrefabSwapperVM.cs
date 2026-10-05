using System;
using System.Collections.Generic;
using System.Linq;
using PrefabSwapperTool.Backup;
using PrefabSwapperTool.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace PrefabSwapperTool.GUI
{
    // F6 Prefab Swapper - the general-purpose version of BrokenPrefabFixer's curated fix and the
    // physics-unfucker tool it was modeled on: swap ANY entity/prefab for ANY other prefab,
    // preserving position/rotation, not limited to a pre-curated broken/fixed mapping. Built on the
    // same LivePrefabSwapper engine that now also powers BrokenPrefabFixer and LOD Substitution's
    // apply action - one swap mechanism, three surfaces.
    //
    // Prefab replacement (unlike material recoloring) is a hard-to-reverse operation - the old
    // entity is actually deleted, not just edited - so every swap is logged to PrefabSwapLogger
    // (its own persisted JSONL file, separate from ChangeLogger/Batch History - see that class for
    // why) instead of just an in-memory stack. Undo still prefers the in-memory GameEntity
    // reference when this session still has it (precise, no matching needed) but falls back to
    // name+position matching against the persisted log when it doesn't - e.g. after closing and
    // reopening this panel, or the game itself.
    public class PrefabSwapperVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private string _oldPrefabName = "";
        private string _newPrefabName = "";
        private string _statusText = "";
        private string _selectionInfoText = "No entity selected.";

        // "Swap to Live Reference" - skips the save-a-prefab-first requirement entirely, per
        // direct request ("can we skip that requirement to save - for the swapper?"). Captured the
        // same way OverrideFillEngine's reference entity is in MaterialSwapTool: select it, click
        // Set, it's held until replaced or the panel closes.
        private GameEntity _liveReferenceEntity;
        private string _liveReferenceLabel = "Live reference: none set";

        // --- Distribution (Path + Grid) ---
        private string _distributePrefabName = "";
        private string _distributeAnchorName = "";
        private bool _distributeRelativeSpacing = true;
        // Matches what ExecuteToggleDistributeSpacingMode sets - the box became a GAP on top of
        // the measured size when PrefabDistributor changed, and this initial label still claimed
        // it was ignored.
        private string _distributeSpacingModeLabel = "Spacing: Relative (measured size + the box as a gap; 0 = edge to edge)";
        private string _distributeAbsoluteSpacing = "3";
        private string _pathNameInput = "";
        private string _pathCountInput = "10";
        private string _pathRotationInput = "0";
        private bool _pathFillToLength = false;
        private string _pathCountModeLabel = "Count: Manual (typed below)";

        // Shared by both Path and Grid: where the wrapping anchor itself ends up, independent of
        // where the individual pieces are placed. ExactOriginPoint = the reference frame used to
        // place things (path start / selected entity) - what this tool always did. BottomCenter =
        // recomputed after the fact from the placed group's own combined footprint, matching what
        // you'd want right before saving the result as a reusable prefab via the editor's own tools.
        private PrefabDistributor.AnchorMode _distributeAnchorMode = PrefabDistributor.AnchorMode.ExactOriginPoint;
        private string _distributeAnchorModeLabel = "Anchor: Exact Origin Point";

        // Grid axes are fully independent - each gets its own direction, spacing mode, absolute
        // value, and gap. Direction is two simple cycle-buttons (Local/World, X/Y/Z) plus a
        // read-only combined-status label - reached this after both a single 6-way cycle-button
        // and a real floating-overlay DropdownWidget (which rendered off-screen in practice, see
        // git history) were tried first. Mode only has 2 choices so a cycling button is fine there
        // as a single button.
        private PrefabDistributor.DistributionAxis _gridAxis1Direction = PrefabDistributor.DistributionAxis.LocalX;
        private PrefabDistributor.DistributionAxis _gridAxis2Direction = PrefabDistributor.DistributionAxis.LocalZ;
        private bool _gridAxis1Relative = true;
        private bool _gridAxis2Relative = true;
        private string _gridAxis1Label = "Axis1: Local X (right)";
        private string _gridAxis2Label = "Axis2: Local Z (up)";
        private string _gridAxis1ModeLabel = "Axis1 Spacing: Relative";
        private string _gridAxis2ModeLabel = "Axis2 Spacing: Relative";
        private string _gridAxis1AbsoluteInput = "3";
        private string _gridAxis2AbsoluteInput = "3";
        private string _gridAxis1GapInput = "0";
        private string _gridAxis2GapInput = "0";
        private string _gridCountAxis1Input = "10";
        private string _gridCountAxis2Input = "1";
        private string _distributeStatus = "Relative spacing measures the FIRST placed instance's own size - sanity-check it before running a big batch.";

        // Which distribution mode's controls are on screen (Grid / Path / Onto Surface) - see the
        // mode region below. Grid is the default because it's the most-used of the three.
        private int _distributeMode = DistributeModeGrid;
        private string _distributeSummary = "";

        // --- Mirror (true reflection, not the 180-degree-rotation trick) ---
        private PrefabDistributor.DistributionAxis _mirrorAxis = PrefabDistributor.DistributionAxis.LocalX;
        private string _mirrorAxisLabel = "Mirror across: Local X (right)";
        // Rotate used to share Mirror's own axis field - confirmed live bug: Mirror's sensible
        // default (Local X, a horizontal axis - most mirroring is left-right/front-back) meant
        // Rotate 45/90 defaulted to tipping objects onto their side instead of spinning them like a
        // turntable, since that needs the vertical axis. Independent field, defaulting to Local Z.
        private PrefabDistributor.DistributionAxis _rotateAxis = PrefabDistributor.DistributionAxis.LocalZ;
        private string _rotateAxisLabel = "Rotate around: Local Z (up)";
        private string _mirrorAnchorName = "";
        private bool _mirrorUseCustomPoint;
        private string _mirrorPointModeLabel = "Mirror Point: Group Center (auto)";
        private string _mirrorCustomPointInput = "";
        private string _mirrorStatus = "Select the group to mirror (multi-select), then Mirror Selected Group. Plane passes through the group's own bottom-center pivot.";

        // --- Placement: Shrinkwrap (snap to surface) + Pile ---
        private RaycastPlacement.CastDirection _snapDirection = RaycastPlacement.CastDirection.StraightDown;
        private string _snapDirectionLabel = "Cast Direction: Straight Down";
        private string _snapMaxDistanceInput = "300";
        private string _snapStatus = "Select entities, then Snap to Surface (drops each onto whatever's below - terrain or another entity) or Snap Into Pile (scatters + drops, for rubble/debris).";
        private string _pileScatterRadiusInput = "1.5";

        public PrefabSwapperVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            RefreshDistributeSummary();
        }

        [DataSourceProperty]
        public string OldPrefabName
        {
            get => _oldPrefabName;
            set { if (value != _oldPrefabName) { _oldPrefabName = value; OnPropertyChangedWithValue(value, nameof(OldPrefabName)); } }
        }

        [DataSourceProperty]
        public string NewPrefabName
        {
            get => _newPrefabName;
            set { if (value != _newPrefabName) { _newPrefabName = value; OnPropertyChangedWithValue(value, nameof(NewPrefabName)); } }
        }

        [DataSourceProperty]
        public string LiveReferenceLabel
        {
            get => _liveReferenceLabel;
            set { if (value != _liveReferenceLabel) { _liveReferenceLabel = value; OnPropertyChangedWithValue(value, nameof(LiveReferenceLabel)); } }
        }

        [DataSourceProperty]
        public string StatusText
        {
            get => _statusText;
            set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, nameof(StatusText)); } }
        }

        [DataSourceProperty]
        public string SelectionInfoText
        {
            get => _selectionInfoText;
            set { if (value != _selectionInfoText) { _selectionInfoText = value; OnPropertyChangedWithValue(value, nameof(SelectionInfoText)); } }
        }

        [DataSourceProperty]
        public string DistributePrefabName
        {
            get => _distributePrefabName;
            set { if (value != _distributePrefabName) { _distributePrefabName = value; OnPropertyChangedWithValue(value, nameof(DistributePrefabName)); RefreshDistributeSummary(); } }
        }

        [DataSourceProperty]
        public string DistributeAnchorName
        {
            get => _distributeAnchorName;
            set { if (value != _distributeAnchorName) { _distributeAnchorName = value; OnPropertyChangedWithValue(value, nameof(DistributeAnchorName)); } }
        }

        [DataSourceProperty]
        public string DistributeSpacingModeLabel
        {
            get => _distributeSpacingModeLabel;
            set { if (value != _distributeSpacingModeLabel) { _distributeSpacingModeLabel = value; OnPropertyChangedWithValue(value, nameof(DistributeSpacingModeLabel)); } }
        }

        [DataSourceProperty]
        public string DistributeAnchorModeLabel
        {
            get => _distributeAnchorModeLabel;
            set { if (value != _distributeAnchorModeLabel) { _distributeAnchorModeLabel = value; OnPropertyChangedWithValue(value, nameof(DistributeAnchorModeLabel)); } }
        }

        [DataSourceProperty]
        public string DistributeAbsoluteSpacing
        {
            get => _distributeAbsoluteSpacing;
            set { if (value != _distributeAbsoluteSpacing) { _distributeAbsoluteSpacing = value; OnPropertyChangedWithValue(value, nameof(DistributeAbsoluteSpacing)); } }
        }

        [DataSourceProperty]
        public string PathNameInput
        {
            get => _pathNameInput;
            set { if (value != _pathNameInput) { _pathNameInput = value; OnPropertyChangedWithValue(value, nameof(PathNameInput)); RefreshDistributeSummary(); } }
        }

        [DataSourceProperty]
        public string PathCountInput
        {
            get => _pathCountInput;
            set { if (value != _pathCountInput) { _pathCountInput = value; OnPropertyChangedWithValue(value, nameof(PathCountInput)); RefreshDistributeSummary(); } }
        }

        [DataSourceProperty]
        public string PathRotationInput
        {
            get => _pathRotationInput;
            set { if (value != _pathRotationInput) { _pathRotationInput = value; OnPropertyChangedWithValue(value, nameof(PathRotationInput)); } }
        }

        [DataSourceProperty]
        public string PathCountModeLabel
        {
            get => _pathCountModeLabel;
            set { if (value != _pathCountModeLabel) { _pathCountModeLabel = value; OnPropertyChangedWithValue(value, nameof(PathCountModeLabel)); } }
        }

        [DataSourceProperty]
        public string GridCountAxis1Input
        {
            get => _gridCountAxis1Input;
            set { if (value != _gridCountAxis1Input) { _gridCountAxis1Input = value; OnPropertyChangedWithValue(value, nameof(GridCountAxis1Input)); RefreshDistributeSummary(); } }
        }

        [DataSourceProperty]
        public string GridCountAxis2Input
        {
            get => _gridCountAxis2Input;
            set { if (value != _gridCountAxis2Input) { _gridCountAxis2Input = value; OnPropertyChangedWithValue(value, nameof(GridCountAxis2Input)); RefreshDistributeSummary(); } }
        }

        [DataSourceProperty]
        public string GridAxis1Label
        {
            get => _gridAxis1Label;
            set { if (value != _gridAxis1Label) { _gridAxis1Label = value; OnPropertyChangedWithValue(value, nameof(GridAxis1Label)); } }
        }

        [DataSourceProperty]
        public string GridAxis2Label
        {
            get => _gridAxis2Label;
            set { if (value != _gridAxis2Label) { _gridAxis2Label = value; OnPropertyChangedWithValue(value, nameof(GridAxis2Label)); } }
        }

        [DataSourceProperty]
        public string MirrorAxisLabel
        {
            get => _mirrorAxisLabel;
            set { if (value != _mirrorAxisLabel) { _mirrorAxisLabel = value; OnPropertyChangedWithValue(value, nameof(MirrorAxisLabel)); } }
        }

        [DataSourceProperty]
        public string RotateAxisLabel
        {
            get => _rotateAxisLabel;
            set { if (value != _rotateAxisLabel) { _rotateAxisLabel = value; OnPropertyChangedWithValue(value, nameof(RotateAxisLabel)); } }
        }

        [DataSourceProperty]
        public string MirrorAnchorName
        {
            get => _mirrorAnchorName;
            set { if (value != _mirrorAnchorName) { _mirrorAnchorName = value; OnPropertyChangedWithValue(value, nameof(MirrorAnchorName)); } }
        }

        [DataSourceProperty]
        public string MirrorStatus
        {
            get => _mirrorStatus;
            set { if (value != _mirrorStatus) { _mirrorStatus = value; OnPropertyChangedWithValue(value, nameof(MirrorStatus)); } }
        }

        [DataSourceProperty]
        public string SnapDirectionLabel
        {
            get => _snapDirectionLabel;
            set { if (value != _snapDirectionLabel) { _snapDirectionLabel = value; OnPropertyChangedWithValue(value, nameof(SnapDirectionLabel)); } }
        }

        [DataSourceProperty]
        public string SnapMaxDistanceInput
        {
            get => _snapMaxDistanceInput;
            set { if (value != _snapMaxDistanceInput) { _snapMaxDistanceInput = value; OnPropertyChangedWithValue(value, nameof(SnapMaxDistanceInput)); } }
        }

        [DataSourceProperty]
        public string PileScatterRadiusInput
        {
            get => _pileScatterRadiusInput;
            set { if (value != _pileScatterRadiusInput) { _pileScatterRadiusInput = value; OnPropertyChangedWithValue(value, nameof(PileScatterRadiusInput)); } }
        }

        [DataSourceProperty]
        public string SnapStatus
        {
            get => _snapStatus;
            set { if (value != _snapStatus) { _snapStatus = value; OnPropertyChangedWithValue(value, nameof(SnapStatus)); } }
        }

        [DataSourceProperty]
        public string MirrorPointModeLabel
        {
            get => _mirrorPointModeLabel;
            set { if (value != _mirrorPointModeLabel) { _mirrorPointModeLabel = value; OnPropertyChangedWithValue(value, nameof(MirrorPointModeLabel)); } }
        }

        [DataSourceProperty]
        public string MirrorCustomPointInput
        {
            get => _mirrorCustomPointInput;
            set { if (value != _mirrorCustomPointInput) { _mirrorCustomPointInput = value; OnPropertyChangedWithValue(value, nameof(MirrorCustomPointInput)); } }
        }

        [DataSourceProperty]
        public string GridAxis1ModeLabel
        {
            get => _gridAxis1ModeLabel;
            set { if (value != _gridAxis1ModeLabel) { _gridAxis1ModeLabel = value; OnPropertyChangedWithValue(value, nameof(GridAxis1ModeLabel)); } }
        }

        [DataSourceProperty]
        public string GridAxis2ModeLabel
        {
            get => _gridAxis2ModeLabel;
            set { if (value != _gridAxis2ModeLabel) { _gridAxis2ModeLabel = value; OnPropertyChangedWithValue(value, nameof(GridAxis2ModeLabel)); } }
        }

        [DataSourceProperty]
        public string GridAxis1AbsoluteInput
        {
            get => _gridAxis1AbsoluteInput;
            set { if (value != _gridAxis1AbsoluteInput) { _gridAxis1AbsoluteInput = value; OnPropertyChangedWithValue(value, nameof(GridAxis1AbsoluteInput)); } }
        }

        [DataSourceProperty]
        public string GridAxis2AbsoluteInput
        {
            get => _gridAxis2AbsoluteInput;
            set { if (value != _gridAxis2AbsoluteInput) { _gridAxis2AbsoluteInput = value; OnPropertyChangedWithValue(value, nameof(GridAxis2AbsoluteInput)); } }
        }

        [DataSourceProperty]
        public string GridAxis1GapInput
        {
            get => _gridAxis1GapInput;
            set { if (value != _gridAxis1GapInput) { _gridAxis1GapInput = value; OnPropertyChangedWithValue(value, nameof(GridAxis1GapInput)); } }
        }

        [DataSourceProperty]
        public string GridAxis2GapInput
        {
            get => _gridAxis2GapInput;
            set { if (value != _gridAxis2GapInput) { _gridAxis2GapInput = value; OnPropertyChangedWithValue(value, nameof(GridAxis2GapInput)); } }
        }

        [DataSourceProperty]
        public string DistributeStatus
        {
            get => _distributeStatus;
            set { if (value != _distributeStatus) { _distributeStatus = value; OnPropertyChangedWithValue(value, nameof(DistributeStatus)); } }
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
        public void ExecuteOpenHistory() => PrefabHistoryLayer.Toggle();
        public void ExecuteOpenSwapSets() => SwapSetBrowserLayer.Toggle();

        // Reads whatever's currently selected in the native editor (click an entity, then click
        // this) and fills OldPrefabName from it - saves having to type/remember exact prefab names.
        public void ExecuteFillFromSelection()
        {
            if (!EntitySelector.HasOpenScene) { SelectionInfoText = "No scene is currently open."; return; }

            var selected = EntitySelector.GetTargets(SelectionMode.Manual);
            if (selected.Count == 0)
            {
                SelectionInfoText = "No entity selected - click one in the editor first.";
                return;
            }

            var first = selected[0];
            OldPrefabName = ResolveFillName(first, out var usedDisplayName);
            SelectionInfoText = (selected.Count == 1
                ? $"Selected: '{OldPrefabName}'"
                : $"Selected {selected.Count} entities - using first one ('{OldPrefabName}'). \"Swap Selected\" will still act on all {selected.Count}.")
                + (usedDisplayName ? " (display name - no prefab behind this entity)" : "");
        }

        // The REAL prefab name when the entity has one, the display name only as a fallback.
        // Display names drift from prefab names constantly - the editor appends _2/_3 to
        // duplicates, Material Swap's rename appends _mst, recolor naming appends the material -
        // and a swap box filled with a drifted display name swaps to (or matches) the wrong
        // thing. Confirmed report: Fill From Selection for the NEW name produced "the old prefab
        // name appended at the end" - a recolor-suffixed display name, not the prefab.
        private static string ResolveFillName(GameEntity entity, out bool usedDisplayName)
        {
            usedDisplayName = false;
            try
            {
                var prefabName = entity.GetPrefabName();
                if (!string.IsNullOrWhiteSpace(prefabName)) return prefabName.Trim();
            }
            catch { }
            usedDisplayName = true;
            return entity.Name ?? "";
        }

        // Same idea as ExecuteFillFromSelection but for NewPrefabName - useful when you've already
        // placed one instance of the target prefab in the scene and want to copy its exact name
        // rather than retype it.
        public void ExecuteFillNewFromSelection()
        {
            if (!EntitySelector.HasOpenScene) { SelectionInfoText = "No scene is currently open."; return; }

            var selected = EntitySelector.GetTargets(SelectionMode.Manual);
            if (selected.Count == 0)
            {
                SelectionInfoText = "No entity selected - click one in the editor first.";
                return;
            }

            var first = selected[0];
            NewPrefabName = ResolveFillName(first, out var usedDisplayName);
            SelectionInfoText = $"New prefab name filled from selection: '{NewPrefabName}'" +
                (usedDisplayName ? " (display name - no prefab behind this entity; Instantiate may not resolve it)" : "");
        }

        // Swaps every currently-selected entity to NewPrefabName, regardless of what it's
        // currently named - the direct "replace what I've clicked" action.
        public void ExecuteSwapSelected()
        {
            if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
            if (string.IsNullOrWhiteSpace(NewPrefabName)) { StatusText = "Enter a new prefab name first."; return; }

            var selected = EntitySelector.GetTargets(SelectionMode.Manual);
            if (selected.Count == 0) { StatusText = "Nothing selected - click an entity in the editor first."; return; }

            ConfirmAndSwap(selected, $"Swap {selected.Count} selected entit{(selected.Count == 1 ? "y" : "ies")} to '{NewPrefabName}'?");
        }

        // Swaps every entity in the WHOLE scene whose current name exactly matches OldPrefabName -
        // the "any prefab to any other prefab, scene-wide" action, same shape as BrokenPrefabFixer
        // but for an arbitrary user-specified pair instead of a curated list.
        public void ExecuteSwapAllMatching()
        {
            if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
            if (string.IsNullOrWhiteSpace(OldPrefabName)) { StatusText = "Enter the prefab name to replace first (or use Fill From Selection)."; return; }
            if (string.IsNullOrWhiteSpace(NewPrefabName)) { StatusText = "Enter a new prefab name first."; return; }

            var all = EntitySelector.GetTargets(SelectionMode.WholeScene);
            var matches = all.Where(e => string.Equals(e.Name, OldPrefabName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0) { StatusText = $"No entities named '{OldPrefabName}' found in this scene."; return; }

            ConfirmAndSwap(matches, $"Swap all {matches.Count} instance(s) of '{OldPrefabName}' to '{NewPrefabName}'?");
        }

        // Add vs Swap - see LivePrefabSwapper.AddModeKeepOriginals. Label kept SHORT so the
        // scale toggle fits beside it on one row (direct request); the toggle's status line
        // carries the full explanation.
        [DataSourceProperty]
        public string SwapModeLabel => LivePrefabSwapper.AddModeKeepOriginals
            ? "Mode: ADD (keep orig.)"
            : "Mode: SWAP (replace)";

        [DataSourceProperty]
        public string SwapModeColor => LivePrefabSwapper.AddModeKeepOriginals ? "#5a8f3cFF" : "#c9782fFF";

        // 1x vs Inherit scale - see LivePrefabSwapper.InheritSourceScale: the engine carries
        // scale in the frame's basis lengths, so the old "scale can't carry over" claim was
        // wrong - it silently ALWAYS carried. Now explicit. 1x (default) = the prefab's own
        // authored scale; Inherit = keep the old entity's scale, weird or not.
        [DataSourceProperty]
        public string SwapScaleLabel => LivePrefabSwapper.InheritSourceScale ? "Scale: Inherit" : "Scale: 1x";

        [DataSourceProperty]
        public string SwapScaleColor => LivePrefabSwapper.InheritSourceScale ? "#8a5fc9FF" : "#3ba1c9FF";

        // Scene-wide repair for copies contaminated before the recursive flag clear existed -
        // see PrefabDistributor.RepairFlaggedCopySubtrees for the signature that makes a full
        // sweep safe. Flags-only: nothing moves, nothing is created or deleted.
        public void ExecuteRepairFlaggedCopies()
        {
            if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
            try
            {
                var (subtrees, cleared) = PrefabDistributor.RepairFlaggedCopySubtrees(EntitySelector.CurrentScene);
                StatusText = cleared == 0
                    ? "No contaminated copies found - every clean-rooted subtree's children are already unflagged."
                    : $"Repaired {subtrees} cop(y/ies): cleared runtime flags on {cleared} child entit(y/ies). " +
                      "They are now clickable and will save. See tool.log [FlagRepair] for names.";
            }
            catch (Exception ex)
            {
                StatusText = "Repair failed: " + ex.Message;
                Log.Error("ExecuteRepairFlaggedCopies failed: " + ex);
            }
        }

        // Optional extra Z rotation (degrees) and uniform scale multiplier for every swapped-in
        // entity (2026-08-23 request) - written straight into LivePrefabSwapper's statics, so
        // regular swaps, live-reference swaps, and swap-set Apply all honour them; undo/redo
        // neutralize them for the duration.
        private string _swapZRotInput = "0";
        [DataSourceProperty]
        public string SwapZRotInput
        {
            get => _swapZRotInput;
            set
            {
                if (value == _swapZRotInput) return;
                _swapZRotInput = value;
                OnPropertyChangedWithValue(value, nameof(SwapZRotInput));
                LivePrefabSwapper.SwapZRotationDegrees =
                    float.TryParse(value, out var deg) ? deg : 0f;
            }
        }

        private string _swapScaleMultInput = "1";
        [DataSourceProperty]
        public string SwapScaleMultInput
        {
            get => _swapScaleMultInput;
            set
            {
                if (value == _swapScaleMultInput) return;
                _swapScaleMultInput = value;
                OnPropertyChangedWithValue(value, nameof(SwapScaleMultInput));
                LivePrefabSwapper.SwapScaleMultiplier =
                    float.TryParse(value, out var mult) && mult > 0.001f ? mult : 1f;
            }
        }

        public void ExecuteToggleSwapScale()
        {
            LivePrefabSwapper.InheritSourceScale = !LivePrefabSwapper.InheritSourceScale;
            OnPropertyChanged(nameof(SwapScaleLabel));
            OnPropertyChanged(nameof(SwapScaleColor));
            StatusText = LivePrefabSwapper.InheritSourceScale
                ? "Inherit Scale: the new entity keeps the OLD entity's scale (carried in the frame), squished or not."
                : "1x Scale: the new entity gets the prefab's own authored scale, regardless of how the old one was scaled.";
        }

        public void ExecuteToggleSwapMode()
        {
            LivePrefabSwapper.AddModeKeepOriginals = !LivePrefabSwapper.AddModeKeepOriginals;
            OnPropertyChanged(nameof(SwapModeLabel));
            OnPropertyChanged(nameof(SwapModeColor));
            StatusText = LivePrefabSwapper.AddModeKeepOriginals
                ? "ADD mode: the new prefab is placed at each target's exact spot and the original is left untouched - " +
                  "no \"break prefab?\" dialogs, no lost pieces. Delete the originals yourself when the result looks right."
                : "SWAP mode: originals are deleted and recreated as the new prefab. Removing pieces that live INSIDE " +
                  "a placed prefab instance will trip the editor's \"break prefab?\" dialog per piece.";
        }

        private void ConfirmAndSwap(List<GameEntity> targets, string question)
        {
            var newPrefab = NewPrefabName;
            bool addMode = LivePrefabSwapper.AddModeKeepOriginals;
            var detail = addMode
                ? " ADD mode: the new prefab is placed at each target's exact position/rotation and the ORIGINALS ARE " +
                  "KEPT (they will overlap until you delete them). A backup is taken first. Undo Last Rotation does not " +
                  "apply - the added entities are captured by Distribute's undo instead."
                : " Position/rotation are preserved. Non-default scale (if any) will NOT carry over - the live API " +
                  "has no way to set it after instantiation. A backup is taken first. This deletes and recreates entities - " +
                  "use Recent Swaps below to undo it if it doesn't look right.";
            var inquiry = new InquiryData(
                addMode ? "Add prefabs?" : "Swap prefabs?",
                question + detail,
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: addMode ? "Add" : "Swap", negativeText: "Cancel",
                affirmativeAction: () => RunSwap(targets, newPrefab),
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        private void RunSwap(List<GameEntity> targets, string newPrefab)
        {
            try
            {
                BackupManager.BackupNow("before-apply");
                var scene = EntitySelector.CurrentScene;
                var sceneName = scene?.GetName() ?? "(unknown scene)";
                var results = targets.Select(e => LivePrefabSwapper.SwapEntity(scene, e, newPrefab)).ToList();

                var okResults = results.Where(r => r.Success).ToList();
                var failCount = results.Count - okResults.Count;
                var scaleWarnCount = okResults.Count(r => r.ScaleWasNonDefault);

                // Add mode created entities without touching the originals - swap history's
                // "re-instantiate the old name" undo model doesn't apply, so the additions go on
                // Distribute's created-entities undo stack instead, like a distribute run.
                if (LivePrefabSwapper.AddModeKeepOriginals)
                {
                    if (okResults.Count > 0)
                        BannerlordSceneToolkit.EditUndo.CaptureCreated($"Add prefabs ({okResults.Count})",
                            okResults.Select(r => r.NewEntity));
                    var addMsg = $"Added {okResults.Count} '{newPrefab}' at the selected entit{(okResults.Count == 1 ? "y's spot" : "ies' spots")} - originals KEPT (they overlap until you delete them).";
                    if (failCount > 0) addMsg += $" {failCount} failed.";
                    if (scaleWarnCount > 0) addMsg += $" {scaleWarnCount} original(s) have non-default scale the new copy does NOT inherit.";
                    StatusText = addMsg;
                    return;
                }
                var autoPlacedCount = okResults.Sum(r => r.AutoPlacedSecondaries.Count);
                var autoPlaceFailedCount = okResults.Sum(r => r.AutoPlaceFailed.Count);
                var autoPlaceTextureFailedCount = okResults.Sum(r => r.AutoPlaceTextureFailed.Count);
                var childrenPreservedCount = okResults.Sum(r => r.ChildrenPreserved);
                var childrenSupersededCount = okResults.Sum(r => r.ChildrenSuperseded);
                var meshOverridesRestoredCount = okResults.Sum(r => r.MeshOverridesRestored);

                if (okResults.Count > 0)
                {
                    var batchId = PrefabSwapLogger.NewBatchId();
                    PrefabSwapHistory.RecordSessionBatch(batchId, okResults);

                    var logEntries = okResults.Select(r => new PrefabSwapLogEntry
                    {
                        BatchId = batchId,
                        TimestampUtc = DateTime.UtcNow,
                        SceneName = sceneName,
                        OldPrefabName = r.OldPrefabName,
                        NewPrefabName = r.NewPrefabName,
                        PosX = r.Frame.origin.x,
                        PosY = r.Frame.origin.y,
                        PosZ = r.Frame.origin.z,
                        RotForwardX = r.Frame.rotation.f.x,
                        RotForwardY = r.Frame.rotation.f.y,
                        RotForwardZ = r.Frame.rotation.f.z,
                    }).ToList();
                    PrefabSwapLogger.Append(logEntries);
                    ScreenshotManager.CaptureForBatch(batchId);
                }

                var msg = $"Swapped {okResults.Count} entit{(okResults.Count == 1 ? "y" : "ies")} to '{newPrefab}'.";
                if (failCount > 0) msg += $" {failCount} failed.";
                var notRemovedCount = okResults.Count(r => r.OriginalNotRemoved);
                if (notRemovedCount > 0) msg += $" WARNING: {notRemovedCount} original(s) could NOT be removed and still overlap the new " +
                                                "pieces (prefab-instance children or live-copy ghosts) - delete them by hand, or use ADD mode.";
                if (scaleWarnCount > 0) msg += $" {scaleWarnCount} had non-default scale that did NOT carry over.";
                if (autoPlacedCount > 0) msg += $" Auto-placed {autoPlacedCount} paired secondary(ies) (PrefabCreatorTool family/pairing).";
                if (autoPlaceFailedCount > 0) msg += $" {autoPlaceFailedCount} auto-place secondary(ies) failed.";
                if (autoPlaceTextureFailedCount > 0) msg += $" {autoPlaceTextureFailedCount} auto-placed secondary(ies) placed but its texture set failed to apply.";
                if (childrenPreservedCount > 0) msg += $" {childrenPreservedCount} existing child entit(y/ies) carried over onto the swapped-in prefab(s).";
                if (childrenSupersededCount > 0) msg += $" {childrenSupersededCount} old child(ren) dropped because the new prefab already had a same-named part of its own.";
                if (meshOverridesRestoredCount > 0) msg += $" {meshOverridesRestoredCount} mesh material/color override(s) carried over.";
                StatusText = msg;
            }
            catch (Exception ex)
            {
                StatusText = "Swap failed: " + ex.Message;
                Log.Error("PrefabSwapper swap failed: " + ex);
            }
        }

        // Captures whatever's currently manually selected (must be exactly one entity) as the live
        // reference for Swap Selected -> Live Reference below - no save-as-prefab step involved.
        public void ExecuteSetLiveReference()
        {
            if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

            var selected = EntitySelector.GetTargets(SelectionMode.Manual);
            if (selected.Count != 1)
            {
                StatusText = $"Select exactly one entity to use as the live reference - {selected.Count} selected.";
                return;
            }

            _liveReferenceEntity = selected[0];
            LiveReferenceLabel = $"Live reference: {_liveReferenceEntity.Name}";
            StatusText = $"Live reference set to '{_liveReferenceEntity.Name}'. Select what to replace and click Swap Selected -> Live Reference.";
        }

        // Swaps every currently-selected entity to a live COPY of the reference entity - skips the
        // save-a-prefab-first requirement entirely, via GameEntity.CopyFrom instead of Instantiate-
        // by-name. See LivePrefabSwapper.SwapEntityToLiveReference for the full explanation.
        public void ExecuteSwapSelectedToLiveReference()
        {
            if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }
            if (_liveReferenceEntity == null || !EntitySelector.IsValidEntity(_liveReferenceEntity))
            {
                StatusText = "Set a live reference entity first.";
                return;
            }

            var selected = EntitySelector.GetTargets(SelectionMode.Manual);
            if (selected.Count == 0) { StatusText = "Nothing selected - click an entity in the editor first."; return; }

            if (selected.Any(e => e.Pointer == _liveReferenceEntity.Pointer))
            {
                StatusText = "Selection includes the live reference entity itself - deselect it first.";
                return;
            }

            var refEntity = _liveReferenceEntity;
            var refName = refEntity.Name;
            bool addModeLive = LivePrefabSwapper.AddModeKeepOriginals;
            var inquiry = new InquiryData(
                addModeLive ? "Add live copies?" : "Swap to live reference?",
                (addModeLive
                    ? $"ADD mode: a live copy of '{refName}' is placed at each of the {selected.Count} selected entit{(selected.Count == 1 ? "y's" : "ies'")} " +
                      "positions and the ORIGINALS ARE KEPT (they overlap until you delete them). "
                    : $"This will replace {selected.Count} selected entit{(selected.Count == 1 ? "y" : "ies")} with a live copy of " +
                      $"'{refName}', as it currently exists in the scene - no saved prefab needed. Position/rotation are preserved. ") +
                "A backup is taken first. Continue?",
                isAffirmativeOptionShown: true, isNegativeOptionShown: true,
                affirmativeText: addModeLive ? "Add" : "Swap", negativeText: "Cancel",
                affirmativeAction: () => RunSwapToLiveReference(selected, refEntity, refName),
                negativeAction: null);
            InformationManager.ShowInquiry(inquiry);
        }

        private void RunSwapToLiveReference(List<GameEntity> targets, GameEntity refEntity, string refName)
        {
            try
            {
                BackupManager.BackupNow("before-apply");
                var scene = EntitySelector.CurrentScene;
                var sceneName = scene?.GetName() ?? "(unknown scene)";
                var results = targets.Select(e => LivePrefabSwapper.SwapEntityToLiveReference(scene, e, refEntity)).ToList();

                var okResults = results.Where(r => r.Success).ToList();
                var failCount = results.Count - okResults.Count;

                // Same add-mode branch as RunSwap - additions land on the created-entities undo.
                if (LivePrefabSwapper.AddModeKeepOriginals)
                {
                    if (okResults.Count > 0)
                        BannerlordSceneToolkit.EditUndo.CaptureCreated($"Add live copies ({okResults.Count})",
                            okResults.Select(r => r.NewEntity));
                    var addMsg = $"Added {okResults.Count} live cop{(okResults.Count == 1 ? "y" : "ies")} of '{refName}' at the selected entit{(okResults.Count == 1 ? "y's spot" : "ies' spots")} - originals KEPT.";
                    if (failCount > 0) addMsg += $" {failCount} failed.";
                    StatusText = addMsg;
                    return;
                }
                var childrenPreservedCount = okResults.Sum(r => r.ChildrenPreserved);
                var childrenSupersededCount = okResults.Sum(r => r.ChildrenSuperseded);
                var meshOverridesRestoredCount = okResults.Sum(r => r.MeshOverridesRestored);

                if (okResults.Count > 0)
                {
                    var batchId = PrefabSwapLogger.NewBatchId();
                    PrefabSwapHistory.RecordSessionBatch(batchId, okResults);

                    var logEntries = okResults.Select(r => new PrefabSwapLogEntry
                    {
                        BatchId = batchId,
                        TimestampUtc = DateTime.UtcNow,
                        SceneName = sceneName,
                        OldPrefabName = r.OldPrefabName,
                        NewPrefabName = r.NewPrefabName,
                        PosX = r.Frame.origin.x,
                        PosY = r.Frame.origin.y,
                        PosZ = r.Frame.origin.z,
                        RotForwardX = r.Frame.rotation.f.x,
                        RotForwardY = r.Frame.rotation.f.y,
                        RotForwardZ = r.Frame.rotation.f.z,
                    }).ToList();
                    PrefabSwapLogger.Append(logEntries);
                    ScreenshotManager.CaptureForBatch(batchId);
                }

                var msg = $"Swapped {okResults.Count} entit{(okResults.Count == 1 ? "y" : "ies")} to a live copy of '{refName}'.";
                if (failCount > 0) msg += $" {failCount} failed.";
                var liveNotRemoved = okResults.Count(r => r.OriginalNotRemoved);
                if (liveNotRemoved > 0) msg += $" WARNING: {liveNotRemoved} original(s) could NOT be removed and still overlap - delete by hand, or use ADD mode.";
                if (childrenPreservedCount > 0) msg += $" {childrenPreservedCount} existing child entit(y/ies) carried over.";
                if (childrenSupersededCount > 0) msg += $" {childrenSupersededCount} old child(ren) dropped because the reference already had a same-named part.";
                if (meshOverridesRestoredCount > 0) msg += $" {meshOverridesRestoredCount} mesh material/color override(s) carried over.";
                StatusText = msg;
            }
            catch (Exception ex)
            {
                StatusText = "Swap to live reference failed: " + ex.Message;
                Log.Error("PrefabSwapper swap-to-live-reference failed: " + ex);
            }
        }

        // Quick action: undoes the single most recent NOT-already-undone batch, without needing to
        // open the History flyout and pick it out by hand. Full history (including Redo) lives in
        // PrefabHistoryLayer now - see that VM for the shared PrefabSwapHistory.Undo/Redo engine
        // this and the flyout both call into.
        public void ExecuteUndoLastSwap()
        {
            if (!EntitySelector.HasOpenScene) { StatusText = "No scene is currently open."; return; }

            var last = PrefabSwapLogger.ListRecentBatches().FirstOrDefault(b => !b.AllUndone);
            if (last == null) { StatusText = "Nothing to undo."; return; }

            var result = PrefabSwapHistory.Undo(EntitySelector.CurrentScene, last.BatchId);
            StatusText = result.Message;
        }

        // ---- Distribution ----
        public void ExecuteToggleDistributeSpacingMode()
        {
            _distributeRelativeSpacing = !_distributeRelativeSpacing;
            DistributeSpacingModeLabel = _distributeRelativeSpacing
                ? "Spacing: Relative (measured size + the box as a gap; 0 = edge to edge)"
                : "Spacing: Absolute (uses the box to the right)";
        }

        // Fill Path Length skips typing a count entirely - it places one instance, measures/reads
        // its spacing the same way relative mode already does, then places as many more as fit
        // along the path's actual length. PathCountInput is ignored while this is on.
        public void ExecuteTogglePathCountMode()
        {
            _pathFillToLength = !_pathFillToLength;
            PathCountModeLabel = _pathFillToLength
                ? "Count: Fill Path Length (count below ignored)"
                : "Count: Manual (typed below)";
            RefreshDistributeSummary();
        }

        public void ExecuteToggleDistributeAnchorMode()
        {
            _distributeAnchorMode = _distributeAnchorMode == PrefabDistributor.AnchorMode.ExactOriginPoint
                ? PrefabDistributor.AnchorMode.BottomCenterOfGroup
                : PrefabDistributor.AnchorMode.ExactOriginPoint;
            DistributeAnchorModeLabel = _distributeAnchorMode == PrefabDistributor.AnchorMode.ExactOriginPoint
                ? "Anchor: Exact Origin Point"
                : "Anchor: Bottom-Center of Group";
        }

        // Places DistributePrefabName evenly along a native scene Path (the editor's own Path
        // tool), sampled by arc-length so items land evenly along curves, not just by raw
        // parameter t. The path must already exist in the scene - type its name (as authored in
        // the editor's Path tool) rather than typing coordinates.
        public void ExecuteDistributeAlongPath()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { DistributeStatus = "No scene is currently open."; return; }
                if (!_gridSourceIsSelection && string.IsNullOrWhiteSpace(DistributePrefabName))
                { DistributeStatus = "Enter a prefab name to distribute, or switch Source to Selection."; return; }
                if (string.IsNullOrWhiteSpace(PathNameInput)) { DistributeStatus = "Enter the path's name (as authored in the editor's Path tool)."; return; }
                var count = 0;
                if (!_pathFillToLength && (!int.TryParse(PathCountInput, out count) || count < 1)) { DistributeStatus = "Count must be a whole number >= 1 (or use Fill Path Length)."; return; }
                if (!float.TryParse(DistributeAbsoluteSpacing, out var absoluteSpacing)) absoluteSpacing = 3f;
                if (!float.TryParse(PathRotationInput, out var rotationDegrees)) rotationDegrees = 0f;

                var scene = EntitySelector.CurrentScene;
                TaleWorlds.Engine.Path path;
                try { path = scene.GetPathWithName(PathNameInput.Trim()); }
                catch (Exception ex) { DistributeStatus = "Path lookup failed: " + ex.Message; return; }
                if (path == null) { DistributeStatus = $"No path named '{PathNameInput}' found in this scene."; return; }

                // Selection source: the path supplies placement, the selection supplies what to
                // place - including a whole assembly, which rotates to follow the curve. See
                // PrefabDistributor.DistributeSelectionAlongPath.
                var selection = _gridSourceIsSelection
                    ? MaterialSwapTool.Core.SelectionMemory.GetSelection()
                    : null;
                if (_gridSourceIsSelection && selection.Count == 0)
                { DistributeStatus = "Nothing selected - select what should be repeated along the path."; return; }

                var sourceLabel = _gridSourceIsSelection ? (selection[0].Name ?? "Selection") : DistributePrefabName;
                var anchorName = string.IsNullOrWhiteSpace(DistributeAnchorName) ? sourceLabel + "_Path" : DistributeAnchorName;

                BackupManager.BackupNow("before-apply");
                if (_gridSourceIsSelection) ManipulationWatcher.SuppressSelfEdit();

                var result = _gridSourceIsSelection
                    ? PrefabDistributor.DistributeSelectionAlongPath(scene, selection, path, count, _distributeRelativeSpacing, absoluteSpacing, anchorName, _distributeAnchorMode, rotationDegrees, _pathFillToLength)
                    : PrefabDistributor.DistributeAlongPath(scene, path, DistributePrefabName, count, _distributeRelativeSpacing, absoluteSpacing, anchorName, _distributeAnchorMode, rotationDegrees, _pathFillToLength);

                // Everything this made, anchor included - undo has to take the anchor too or it
                // leaves an empty one behind that looks like a real object (see EditUndo).
                if (result.Success)
                    BannerlordSceneToolkit.EditUndo.CaptureCreated($"Distribute along path ({result.Placed.Count})",
                        result.Placed.Concat(new[] { result.AnchorEntity }));

                // The new copies become the selection so the result is visible immediately rather
                // than needing to be hunted for in the viewport. (This block appeared three times
                // in a row here - identical copies, so two were pure no-ops.)
                if (result.Success)
                    MaterialSwapTool.Core.LiveSceneChecks.SetEditorSelection(result.Placed);

                if (!result.Success) { DistributeStatus = "Failed: " + result.Error; return; }
                var baseCount = result.Placed.Count - result.AutoPlacedSecondaryCount;
                var msg = _pathFillToLength
                    ? $"Created '{result.AnchorEntity.Name}', filled path '{PathNameInput}' with {baseCount} instance(s) (spacing {result.PathSpacingUsed:F2})."
                    : $"Created '{result.AnchorEntity.Name}', placed {baseCount} of {count} along path '{PathNameInput}'.";
                if (result.AutoPlacedSecondaryCount > 0) msg += $" Plus {result.AutoPlacedSecondaryCount} auto-placed paired secondary(ies).";
                if (result.PathSpacingWasShrunkToFit) msg += $" Spacing shrunk to {result.PathSpacingUsed:F2} to fit the path (measured spacing wouldn't fit {count} instances).";
                if (result.Failed.Count > 0) msg += $" Failed: {string.Join("; ", result.Failed)}";
                DistributeStatus = msg;
            }
            catch (Exception ex)
            {
                DistributeStatus = "Distribute Along Path failed: " + ex.Message;
                Log.Error("ExecuteDistributeAlongPath failed: " + ex);
            }
        }

        private static readonly (PrefabDistributor.DistributionAxis Axis, string Label)[] AxisChoices =
        {
            (PrefabDistributor.DistributionAxis.LocalX, "Local X (right)"),
            (PrefabDistributor.DistributionAxis.LocalY, "Local Y (forward)"),
            (PrefabDistributor.DistributionAxis.LocalZ, "Local Z (up)"),
            (PrefabDistributor.DistributionAxis.WorldX, "World X"),
            (PrefabDistributor.DistributionAxis.WorldY, "World Y"),
            (PrefabDistributor.DistributionAxis.WorldZ, "World Z"),
        };

        // Two independent, simple cycle-buttons per axis (same proven Command.Click pattern used
        // everywhere else in this panel, e.g. the Relative/Absolute mode buttons) instead of either
        // a single 6-way cycle or the expand-list - Local/World is one button (2 states), X/Y/Z is
        // the other (3 states), and a third, non-interactive text shows the combined result. Faster
        // to reach any of the 6 combinations than cycling one button through all of them, and no
        // risk of the floating-overlay positioning bug the real DropdownWidget attempt hit.
        private static bool IsLocalAxis(PrefabDistributor.DistributionAxis axis) =>
            axis == PrefabDistributor.DistributionAxis.LocalX || axis == PrefabDistributor.DistributionAxis.LocalY || axis == PrefabDistributor.DistributionAxis.LocalZ;

        private static PrefabDistributor.DistributionAxis ToggleLocalWorld(PrefabDistributor.DistributionAxis axis)
        {
            switch (axis)
            {
                case PrefabDistributor.DistributionAxis.LocalX: return PrefabDistributor.DistributionAxis.WorldX;
                case PrefabDistributor.DistributionAxis.LocalY: return PrefabDistributor.DistributionAxis.WorldY;
                case PrefabDistributor.DistributionAxis.LocalZ: return PrefabDistributor.DistributionAxis.WorldZ;
                case PrefabDistributor.DistributionAxis.WorldX: return PrefabDistributor.DistributionAxis.LocalX;
                case PrefabDistributor.DistributionAxis.WorldY: return PrefabDistributor.DistributionAxis.LocalY;
                default: return PrefabDistributor.DistributionAxis.LocalZ;
            }
        }

        private static PrefabDistributor.DistributionAxis CycleComponent(PrefabDistributor.DistributionAxis axis)
        {
            switch (axis)
            {
                case PrefabDistributor.DistributionAxis.LocalX: return PrefabDistributor.DistributionAxis.LocalY;
                case PrefabDistributor.DistributionAxis.LocalY: return PrefabDistributor.DistributionAxis.LocalZ;
                case PrefabDistributor.DistributionAxis.LocalZ: return PrefabDistributor.DistributionAxis.LocalX;
                case PrefabDistributor.DistributionAxis.WorldX: return PrefabDistributor.DistributionAxis.WorldY;
                case PrefabDistributor.DistributionAxis.WorldY: return PrefabDistributor.DistributionAxis.WorldZ;
                default: return PrefabDistributor.DistributionAxis.WorldX;
            }
        }

        public void ExecuteToggleGridAxis1LocalWorld()
        {
            _gridAxis1Direction = ToggleLocalWorld(_gridAxis1Direction);
            GridAxis1Label = "Axis1: " + AxisChoices.First(c => c.Axis == _gridAxis1Direction).Label;
            RefreshDistributeSummary();
        }

        public void ExecuteCycleGridAxis1Component()
        {
            _gridAxis1Direction = CycleComponent(_gridAxis1Direction);
            GridAxis1Label = "Axis1: " + AxisChoices.First(c => c.Axis == _gridAxis1Direction).Label;
            RefreshDistributeSummary();
        }

        public void ExecuteToggleGridAxis2LocalWorld()
        {
            _gridAxis2Direction = ToggleLocalWorld(_gridAxis2Direction);
            GridAxis2Label = "Axis2: " + AxisChoices.First(c => c.Axis == _gridAxis2Direction).Label;
            RefreshDistributeSummary();
        }

        public void ExecuteCycleGridAxis2Component()
        {
            _gridAxis2Direction = CycleComponent(_gridAxis2Direction);
            GridAxis2Label = "Axis2: " + AxisChoices.First(c => c.Axis == _gridAxis2Direction).Label;
            RefreshDistributeSummary();
        }

        public void ExecuteToggleMirrorAxisLocalWorld()
        {
            _mirrorAxis = ToggleLocalWorld(_mirrorAxis);
            MirrorAxisLabel = "Mirror across: " + AxisChoices.First(c => c.Axis == _mirrorAxis).Label;
        }

        public void ExecuteCycleMirrorAxisComponent()
        {
            _mirrorAxis = CycleComponent(_mirrorAxis);
            MirrorAxisLabel = "Mirror across: " + AxisChoices.First(c => c.Axis == _mirrorAxis).Label;
        }

        public void ExecuteToggleRotateAxisLocalWorld()
        {
            _rotateAxis = ToggleLocalWorld(_rotateAxis);
            RotateAxisLabel = "Rotate around: " + AxisChoices.First(c => c.Axis == _rotateAxis).Label;
        }

        public void ExecuteCycleRotateAxisComponent()
        {
            _rotateAxis = CycleComponent(_rotateAxis);
            RotateAxisLabel = "Rotate around: " + AxisChoices.First(c => c.Axis == _rotateAxis).Label;
        }

        public void ExecuteToggleMirrorPointMode()
        {
            _mirrorUseCustomPoint = !_mirrorUseCustomPoint;
            MirrorPointModeLabel = _mirrorUseCustomPoint ? "Mirror Point: Custom (typed below)" : "Mirror Point: Group Center (auto)";
        }

        // Reads whatever's currently selected (one entity) and fills its position into the custom
        // point box - easier than typing exact coordinates by hand when you want to mirror a group
        // across, say, a doorway's own center rather than the group's own centroid.
        public void ExecuteFillMirrorPointFromSelection()
        {
            if (!EntitySelector.HasOpenScene) { MirrorStatus = "No scene is currently open."; return; }
            var selected = EntitySelector.GetTargets(SelectionMode.Manual);
            if (selected.Count == 0) { MirrorStatus = "Nothing selected - click the entity marking the mirror point first."; return; }

            var p = selected[0].GetGlobalFrame().origin;
            MirrorCustomPointInput = $"{p.x:F3}, {p.y:F3}, {p.z:F3}";
            MirrorStatus = $"Mirror point filled from selection: {MirrorCustomPointInput}";
        }

        private static bool TryParseVec3(string text, out Vec3 result)
        {
            result = default;
            var parts = (text ?? "").Split(',');
            if (parts.Length != 3) return false;
            if (!float.TryParse(parts[0].Trim(), out var x)) return false;
            if (!float.TryParse(parts[1].Trim(), out var y)) return false;
            if (!float.TryParse(parts[2].Trim(), out var z)) return false;
            result = new Vec3(x, y, z, 0f);
            return true;
        }

        // Select the group to mirror (multi-select in the editor), pick the mirror axis above,
        // Mirror Selected Group. By default the plane passes through the group's own bottom-center
        // pivot; toggle Mirror Point to Custom to instead mirror across an arbitrary typed/filled
        // point - e.g. a V-shaped group mirrored across a point below its own base comes out as a
        // taller upside-down V spanning further away, rather than folding back onto itself. Either
        // way every entity's own position AND rotation are independently reflected, never rotated
        // as a rigid group. Local X/Y/Z axis choices follow the FIRST selected entity's own
        // rotation (rotate it first to angle the mirror plane to match an angled wall), World X/Y/Z
        // ignore rotation entirely.
        public void ExecuteMirrorGroup()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { MirrorStatus = "No scene is currently open."; return; }
                var selection = EntitySelector.GetTargets(SelectionMode.Manual);
                if (selection.Count == 0) { MirrorStatus = "Nothing selected - select the group to mirror first."; return; }

                Vec3? customPivot = null;
                if (_mirrorUseCustomPoint)
                {
                    if (!TryParseVec3(MirrorCustomPointInput, out var parsed))
                    {
                        MirrorStatus = "Custom mirror point must be 'x, y, z' (or use Fill From Selection).";
                        return;
                    }
                    customPivot = parsed;
                }

                var anchorName = string.IsNullOrWhiteSpace(MirrorAnchorName) ? NextMirroredName(selection[0].Name) : MirrorAnchorName;
                var referenceFrame = selection[0].GetGlobalFrame();

                // Contamination tripwire - confirmed in CC_76: a source that already CONTAINS
                // mirror-flipped geometry (a stowaway copy from an earlier mirror run, nested
                // inside by a re-parent or spread by shift-drag CopyFrom) mirrors into doubled,
                // inside-out-looking chaos that reads as the tool exploding when it's really
                // garbage-in. A negative rotation determinant is the reliable signature of
                // already-mirrored geometry, so count them up front and say so in the status.
                int alreadyMirrored = 0;
                foreach (var entity in selection)
                    alreadyMirrored += CountMirrorFlippedInTree(entity, 0);

                BackupManager.BackupNow("before-apply");
                var result = PrefabDistributor.MirrorGroup(EntitySelector.CurrentScene, selection, _mirrorAxis, referenceFrame, anchorName, _distributeAnchorMode, customPivot);

                // Everything this made, anchor included - undo has to take the anchor too or it
                // leaves an empty one behind that looks like a real object (see EditUndo).
                if (result.Success)
                    BannerlordSceneToolkit.EditUndo.CaptureCreated($"Mirror group ({result.Placed.Count})",
                        result.Placed.Concat(new[] { result.AnchorEntity }));

                if (!result.Success) { MirrorStatus = "Failed: " + result.Error; return; }
                var msg = $"Created '{result.AnchorEntity.Name}', mirrored {result.Placed.Count - result.AutoPlacedSecondaryCount} of {selection.Count} entit(y/ies).";
                if (result.AutoPlacedSecondaryCount > 0) msg += $" Plus {result.AutoPlacedSecondaryCount} auto-placed paired secondary(ies).";
                if (result.Failed.Count > 0) msg += $" Failed: {string.Join("; ", result.Failed)}";
                if (alreadyMirrored > 0)
                    msg += $" WARNING: the source already contained {alreadyMirrored} mirror-flipped piece(s) nested in it " +
                           "(likely stowaways from an earlier mirror run) - the copy duplicated them too. If the result " +
                           "looks doubled or inside-out, clean the source first.";
                MirrorStatus = msg;
            }
            catch (Exception ex)
            {
                MirrorStatus = "Mirror failed: " + ex.Message;
                Log.Error("ExecuteMirrorGroup failed: " + ex);
            }
        }

        // "wall" -> "wall_Mirrored" -> "wall_Mirroredx2" -> "wall_Mirroredx3" - instead of the
        // old "wall_Mirrored_Mirrored_Mirrored" pileup (direct request). Applies only to the
        // DEFAULT name; a typed anchor name is always used verbatim.
        private static string NextMirroredName(string sourceName)
        {
            var name = sourceName ?? "";
            var match = System.Text.RegularExpressions.Regex.Match(name, @"^(?<base>.*_Mirrored)(x(?<n>\d+))?$");
            if (!match.Success) return name + "_Mirrored";
            int count = match.Groups["n"].Success && int.TryParse(match.Groups["n"].Value, out var parsed) ? parsed : 1;
            return match.Groups["base"].Value + "x" + (count + 1);
        }

        // Counts entities in a tree (the entity itself plus descendants) whose rotation has a
        // negative determinant - the signature of mirror-flipped geometry. Depth-capped and
        // exception-swallowing: this is a diagnostic, never worth failing a mirror over.
        private static int CountMirrorFlippedInTree(GameEntity entity, int depth)
        {
            if (entity == null || depth > 8) return 0;
            int count = 0;
            try
            {
                var r = entity.GetGlobalFrame().rotation;
                // det = (s x f) . u, hand-rolled like SurfaceSnap/PrefabDistributor's own Cross.
                var cx = r.s.y * r.f.z - r.s.z * r.f.y;
                var cy = r.s.z * r.f.x - r.s.x * r.f.z;
                var cz = r.s.x * r.f.y - r.s.y * r.f.x;
                if (cx * r.u.x + cy * r.u.y + cz * r.u.z < 0f) count++;

                foreach (var child in entity.GetChildren())
                    count += CountMirrorFlippedInTree(child, depth + 1);
            }
            catch { }
            return count;
        }

        public void ExecuteRotateGroup45() => RotateSelectedGroup(45f);
        public void ExecuteRotateGroup90() => RotateSelectedGroup(90f);

        // Arbitrary angle. RotateSelectedGroup already took a float - the 45/90 buttons were only
        // ever two fixed callers - so this needed a text box, not new rotation maths.
        private string _rotateAngleInput = "15";

        [DataSourceProperty]
        public string RotateAngleInput
        {
            get => _rotateAngleInput;
            set { if (value != _rotateAngleInput) { _rotateAngleInput = value; OnPropertyChangedWithValue(value, nameof(RotateAngleInput)); } }
        }

        public void ExecuteRotateGroupSpecified()
        {
            var raw = (RotateAngleInput ?? "").Trim();
            // InvariantCulture so a typed "22.5" parses the same regardless of the machine's
            // decimal separator - the rest of this tool's numeric input already assumes that.
            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var angle))
            {
                StatusText = $"'{raw}' isn't a number - enter degrees, e.g. 22.5 or -30.";
                return;
            }
            if (Math.Abs(angle) < 0.001f)
            {
                StatusText = "0 degrees would do nothing.";
                return;
            }
            // Negatives are meaningful (counter-rotate) and values over 360 are harmless - the
            // rotation matrix wraps naturally - so neither is rejected.
            RotateSelectedGroup(angle);
        }

        // Rigid rotation of the current selection, in place - has its own axis picker (defaulting to
        // Local Z/up, the turntable-spin case that's actually common, unlike Mirror's own default of
        // Local X which used to be silently shared and made Rotate tip things onto their side) but
        // still shares Mirror Point (pivot: group center, or a typed/filled custom point) with Mirror
        // Selected Group above. Unlike Mirror this doesn't create new copies or an anchor - it moves
        // the existing entities directly, since a rotation preserves handedness and there's nothing
        // that needs re-instantiating.
        // Undoes the last undoable operation from ANY tool - the stack is shared (see EditUndo).
        // Deletions are never on it, and still rely on the backup taken before each apply.
        public void ExecuteUndoTransform()
        {
            var (ok, message) = BannerlordSceneToolkit.EditUndo.UndoLast();
            MirrorStatus = message;
            if (!ok) Log.Info("[EditUndo] " + message);
        }

        private void RotateSelectedGroup(float angleDegrees)
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { MirrorStatus = "No scene is currently open."; return; }
                var selection = EntitySelector.GetTargets(SelectionMode.Manual);
                if (selection.Count == 0) { MirrorStatus = "Nothing selected - select the group to rotate first."; return; }

                Vec3? customPivot = null;
                if (_mirrorUseCustomPoint)
                {
                    if (!TryParseVec3(MirrorCustomPointInput, out var parsed))
                    {
                        MirrorStatus = "Custom mirror point must be 'x, y, z' (or use Fill From Selection).";
                        return;
                    }
                    customPivot = parsed;
                }

                var referenceFrame = selection[0].GetGlobalFrame();

                BackupManager.BackupNow("before-apply");
                ManipulationWatcher.SuppressSelfEdit();

                // Rotation only moves existing entities, so remembering their frames is a complete
                // undo - see EditUndo for which operations qualify and which never can.
                BannerlordSceneToolkit.EditUndo.CaptureFrames($"Rotate {angleDegrees:0.##} deg", selection);

                // Recorded so Shift+R can replay it onto a different selection. Rotation only -
                // see RepeatLastTransform for why Mirror and Distribute are excluded.
                RepeatLastTransform.RecordRotation(_rotateAxis, angleDegrees, _mirrorUseCustomPoint,
                    customPivot ?? default(Vec3));

                var result = PrefabDistributor.RotateGroup(EntitySelector.CurrentScene, selection, _rotateAxis, angleDegrees, referenceFrame, customPivot);

                if (!result.Success) { MirrorStatus = "Failed: " + result.Error; return; }
                var msg = $"Rotated {result.Placed.Count} of {selection.Count} entit(y/ies) by {angleDegrees} degrees.";
                if (result.Failed.Count > 0) msg += $" Failed: {string.Join("; ", result.Failed)}";
                MirrorStatus = msg;
            }
            catch (Exception ex)
            {
                MirrorStatus = "Rotate failed: " + ex.Message;
                Log.Error("RotateSelectedGroup failed: " + ex);
            }
        }

        public void ExecuteToggleSnapDirection()
        {
            _snapDirection = _snapDirection == RaycastPlacement.CastDirection.StraightDown
                ? RaycastPlacement.CastDirection.EntityForward
                : RaycastPlacement.CastDirection.StraightDown;
            SnapDirectionLabel = _snapDirection == RaycastPlacement.CastDirection.StraightDown
                ? "Cast Direction: Straight Down"
                : "Cast Direction: Entity's Own Forward";
        }

        // Drops each selected entity onto whatever's directly below it (terrain or another entity)
        // and tilts it to match that surface, instead of only being able to move its origin with no
        // rotation snapping - see RaycastPlacement for the raycast/normal-estimate details and the
        // caution about this being new, not-yet-live-tested native call usage.
        public void ExecuteSnapToSurface()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { SnapStatus = "No scene is currently open."; return; }
                var selection = EntitySelector.GetTargets(SelectionMode.Manual);
                if (selection.Count == 0) { SnapStatus = "Nothing selected - select the entity/entities to snap first."; return; }
                if (!float.TryParse(SnapMaxDistanceInput, out var maxDist) || maxDist <= 0f) maxDist = 300f;

                BackupManager.BackupNow("before-apply");
                var result = RaycastPlacement.SnapToSurface(EntitySelector.CurrentScene, selection, _snapDirection, maxDist);

                var msg = $"Snapped {result.Snapped} of {selection.Count} entit(y/ies).";
                if (result.Failed.Count > 0) msg += $" Failed: {string.Join("; ", result.Failed)}";
                SnapStatus = msg;
            }
            catch (Exception ex)
            {
                SnapStatus = "Snap to Surface failed: " + ex.Message;
                Log.Error("ExecuteSnapToSurface failed: " + ex);
            }
        }

        // Scatter + drop - see RaycastPlacement.SnapIntoPile for why processing in selection order
        // gives a naturally uneven pile instead of everything landing flat.
        public void ExecuteSnapIntoPile()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { SnapStatus = "No scene is currently open."; return; }
                var selection = EntitySelector.GetTargets(SelectionMode.Manual);
                if (selection.Count == 0) { SnapStatus = "Nothing selected - select the entities to pile first."; return; }
                if (!float.TryParse(SnapMaxDistanceInput, out var maxDist) || maxDist <= 0f) maxDist = 300f;
                if (!float.TryParse(PileScatterRadiusInput, out var scatter) || scatter < 0f) scatter = 1.5f;

                BackupManager.BackupNow("before-apply");
                var result = RaycastPlacement.SnapIntoPile(EntitySelector.CurrentScene, selection, scatter, maxDist);

                var msg = $"Piled {result.Snapped} of {selection.Count} entit(y/ies) (scatter radius {scatter}).";
                if (result.Failed.Count > 0) msg += $" Failed: {string.Join("; ", result.Failed)}";
                SnapStatus = msg;
            }
            catch (Exception ex)
            {
                SnapStatus = "Snap Into Pile failed: " + ex.Message;
                Log.Error("ExecuteSnapIntoPile failed: " + ex);
            }
        }

        public void ExecuteToggleGridAxis1Mode()
        {
            _gridAxis1Relative = !_gridAxis1Relative;
            GridAxis1ModeLabel = "Axis1 Spacing: " + (_gridAxis1Relative ? "Relative" : "Absolute");
        }

        public void ExecuteToggleGridAxis2Mode()
        {
            _gridAxis2Relative = !_gridAxis2Relative;
            GridAxis2ModeLabel = "Axis2 Spacing: " + (_gridAxis2Relative ? "Relative" : "Absolute");
        }

        // Places DistributePrefabName in a rectangular grid starting at whatever's currently
        // selected, using ITS position and rotation as the grid's origin (Local axis choices
        // follow that rotation - rotate the selected reference entity first to angle the whole
        // grid to match a wall's actual direction; World axis choices ignore it). Each axis has
        // its own independent direction, Relative/Absolute mode, absolute value, and Gap - Gap is
        // always added on top of whichever base spacing is used, so Relative mode can still have
        // deliberate breathing room instead of items always sitting flush edge-to-edge.
        // Fills the prefab-to-distribute box from whatever is selected. Both distribution modes
        // ALREADY require exactly one entity selected (it supplies the grid's origin and
        // orientation, or the path run's reference), and that entity is almost always an
        // instance of the very prefab you want tiled - you place one wall segment, then grid it
        // out. Typing its name by hand next to a selection that already knows it was the odd
        // step out; the anchor-name box beside it, by contrast, already defaults sensibly
        // (<prefab>_Grid / _Path), so it never needed a button.
        //
        // RESOLVED VIA GetPrefabName(), NOT .Name - the same distinction that broke MirrorGroup
        // until v0.7. A display name drifts from its prefab (Material Swap's own "Rename Changed"
        // appends _mst; the editor appends .001 to duplicates) and Instantiate needs the real
        // prefab. Falling back to .Name when there is no prefab name keeps this useful on a
        // hand-placed entity while saying plainly that is what happened.
        public void ExecuteFillDistributeFromSelection()
        {
            if (!EntitySelector.HasOpenScene) { DistributeStatus = "No scene is currently open."; return; }

            var selected = MaterialSwapTool.Core.SelectionMemory.GetSelection();
            if (selected.Count == 0) { DistributeStatus = "No entity selected - click the prefab you want to repeat."; return; }

            var first = selected[0];
            string prefabName = null;
            try { prefabName = first.GetPrefabName(); } catch { }

            // ORIGIN COMES ALONG FOR FREE (2026-08-23, "when you have source prefab name and
            // you do fill from selection it ALSO defaults to origin coordinates and fills that
            // from selection - do you see how that's easier?"): one click captures both the
            // WHAT (prefab name) and the WHERE (this entity's position as typed coordinates),
            // so the selection is then free to be used for TARGET surfaces - which fill mode
            // needs it for. Heading still reads from a selected entity when one exists.
            try
            {
                var origin = first.GetGlobalFrame().origin;
                DistributeOriginInput = $"{origin.x:0.###}, {origin.y:0.###}, {origin.z:0.###}";
                if (!_originFromCoordinates)
                {
                    _originFromCoordinates = true;
                    OnPropertyChanged(nameof(DistributeOriginModeLabel));
                    OnPropertyChanged(nameof(DistributeOriginModeColor));
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(prefabName))
            {
                DistributePrefabName = prefabName.Trim();
                DistributeStatus = $"Distributing '{DistributePrefabName}'. Origin switched to Coordinates and filled from " +
                                   "this entity - the selection is now free for target surfaces.";
                return;
            }

            DistributePrefabName = first.Name ?? "";
            DistributeStatus = $"'{first.Name}' has no prefab name behind it - filled in its display name instead, which only " +
                               "works if a prefab really is called that. Origin switched to Coordinates and filled from this entity.";
        }

        // WHAT GOES IN EACH GRID CELL: a fresh instance of a named prefab, or a copy of what is
        // selected. Selection mode is the one that works on an entity with no saved prefab, keeps
        // per-instance recolours, and tiles a multi-entity assembly as a unit - see
        // PrefabDistributor.DistributeSelectionInGrid.
        private bool _gridSourceIsSelection;

        [DataSourceProperty]
        public string GridSourceModeLabel =>
            _gridSourceIsSelection ? "Source: Selection (copies)" : "Source: Prefab name";

        [DataSourceProperty]
        public string GridSourceModeColor => _gridSourceIsSelection ? "#8a5fc9FF" : "#3ba1c9FF";

        public void ExecuteToggleGridSourceMode()
        {
            // (visibility + summary updates ride at the end - the prefab row hides entirely in
            // Selection mode instead of sitting there with an "ignored when..." caveat)
            _gridSourceIsSelection = !_gridSourceIsSelection;
            OnPropertyChanged(nameof(GridSourceModeLabel));
            OnPropertyChanged(nameof(GridSourceModeColor));
            DistributeStatus = _gridSourceIsSelection
                ? "Path and Grid will COPY the current selection (the originals stay put and fill the first slot). " +
                  "The prefab name box is ignored; works on unsaved composites, keeps per-instance colours, and " +
                  "repeats a multi-entity assembly as a unit."
                : "Path and Grid will instantiate the named prefab. Select ONE entity to set the grid's origin.";
            OnPropertyChanged(nameof(IsPrefabRowVisible));
            RefreshDistributeSummary();
        }

        // ---- Distribution mode: one mode's controls on screen at a time ----
        //
        // The panel used to show Path, Grid and Onto Surface controls in one undivided stack with
        // three Distribute buttons at the bottom, and reading it required already knowing which
        // fields fed which button (Path's name/count were dead weight for Grid, the top spacing
        // row was Path-only, and Onto Surface's own controls sat scattered between Grid's).
        // Direct feedback: "the distribute onto surface UI is REALLY confusing". A mode selector
        // plus per-mode collapsing sections (the same IsVisible group-collapse pattern the
        // browser panels use) means what is on screen is exactly what Distribute will read.
        private const int DistributeModeGrid = 0;
        private const int DistributeModePath = 1;
        private const int DistributeModeSurface = 2;

        [DataSourceProperty] public bool IsPathMode => _distributeMode == DistributeModePath;
        [DataSourceProperty] public bool IsSurfaceMode => _distributeMode == DistributeModeSurface;
        // Grid and Onto Surface share the whole origin+axes block - Onto Surface IS the grid,
        // just with each cell dropped onto whatever is beneath it.
        [DataSourceProperty] public bool IsGridLayoutVisible => _distributeMode != DistributeModePath;
        // The prefab row hides entirely in Selection mode - see ExecuteToggleGridSourceMode.
        [DataSourceProperty] public bool IsPrefabRowVisible => !_gridSourceIsSelection;

        private const string DistributeModeOnColor = "#5a8f3cFF";
        private const string DistributeModeOffColor = "#3a4a55FF";
        [DataSourceProperty] public string ModeGridButtonColor => _distributeMode == DistributeModeGrid ? DistributeModeOnColor : DistributeModeOffColor;
        [DataSourceProperty] public string ModePathButtonColor => _distributeMode == DistributeModePath ? DistributeModeOnColor : DistributeModeOffColor;
        [DataSourceProperty] public string ModeSurfaceButtonColor => _distributeMode == DistributeModeSurface ? DistributeModeOnColor : DistributeModeOffColor;

        [DataSourceProperty]
        public string DistributeModeHint =>
            _distributeMode == DistributeModePath
                ? "Repeats along a scene Path (authored with the editor's own Path tool). The PATH supplies every position and facing - no origin entity needed."
                : _distributeMode == DistributeModeSurface
                    ? "The same grid as In Grid, but each cell is raycast STRAIGHT DOWN and lands on whatever is beneath it. The origin supplies only the horizontal start + heading - HEIGHT comes from the surface, so the run follows a hillside or stepped terrace."
                    : "Repeats in a flat grid on the origin's own plane. Select ONE entity (or type coordinates below) to set where the grid starts and which way it runs.";

        [DataSourceProperty]
        public string DistributeButtonLabel =>
            _distributeMode == DistributeModePath ? "Distribute Along Path"
            : _distributeMode == DistributeModeSurface ? "Distribute Onto Surface"
            : "Distribute In Grid";

        public void ExecuteSetDistributeModeGrid() => SetDistributeMode(DistributeModeGrid);
        public void ExecuteSetDistributeModePath() => SetDistributeMode(DistributeModePath);
        public void ExecuteSetDistributeModeSurface() => SetDistributeMode(DistributeModeSurface);

        private void SetDistributeMode(int mode)
        {
            if (_distributeMode == mode) return;
            _distributeMode = mode;
            OnPropertyChanged(nameof(IsPathMode));
            OnPropertyChanged(nameof(IsSurfaceMode));
            OnPropertyChanged(nameof(IsGridLayoutVisible));
            OnPropertyChanged(nameof(ModeGridButtonColor));
            OnPropertyChanged(nameof(ModePathButtonColor));
            OnPropertyChanged(nameof(ModeSurfaceButtonColor));
            OnPropertyChanged(nameof(DistributeModeHint));
            OnPropertyChanged(nameof(DistributeButtonLabel));
            RefreshDistributeSummary();
        }

        // The single Distribute button - runs whichever mode is on screen, so there is never a
        // question of which fields feed which of three buttons.
        public void ExecuteDistribute()
        {
            switch (_distributeMode)
            {
                case DistributeModePath: ExecuteDistributeAlongPath(); break;
                case DistributeModeSurface: ExecuteDistributeOntoSurface(); break;
                default: ExecuteDistributeInGrid(); break;
            }
        }

        [DataSourceProperty]
        public string DistributeSummary
        {
            get => _distributeSummary;
            set { if (value != _distributeSummary) { _distributeSummary = value; OnPropertyChangedWithValue(value, nameof(DistributeSummary)); } }
        }

        // Plain-language read-back of what Distribute is about to do, recomputed as inputs
        // change. Built from the TYPED inputs only, never the live scene - this runs on every
        // keystroke, and it must be safe to call from setters during panel construction.
        private void RefreshDistributeSummary()
        {
            try
            {
                string what = _gridSourceIsSelection
                    ? "copies of the current selection"
                    : string.IsNullOrWhiteSpace(DistributePrefabName) ? "(type a prefab name)" : $"'{DistributePrefabName.Trim()}'";

                if (_distributeMode == DistributeModePath)
                {
                    string countPart = _pathFillToLength ? "as many as fit" : $"{PathCountInput?.Trim()} instance(s)";
                    string pathPart = string.IsNullOrWhiteSpace(PathNameInput) ? "(type a path name)" : $"'{PathNameInput.Trim()}'";
                    DistributeSummary = $"Next run: {countPart} of {what} along path {pathPart}.";
                    return;
                }

                string grid = $"a {GridCountAxis1Input?.Trim()}x{GridCountAxis2Input?.Trim()} grid of {what}";
                if (_distributeMode == DistributeModeSurface)
                {
                    string ground = string.IsNullOrWhiteSpace(SurfaceTargetInput)
                        ? "whatever is first beneath each cell"
                        : SurfaceTargetInput.Contains(",")
                            ? $"any of [{SurfaceTargetInput.Trim()}] only (the ray passes through anything else)"
                            : $"'{SurfaceTargetInput.Trim()}' only (the ray passes through anything else)";
                    string upDown = _surfaceAlign ? "tilted to match the ground" : "kept upright";

                    if (_surfaceFillExtent)
                    {
                        // Worded around the LIVE selection without reading it - a summary rebuilt
                        // per keystroke must never touch the scene, so it can't know the current
                        // selection count. The captured count is plain VM state and safe.
                        string filterPart = string.IsNullOrWhiteSpace(SurfaceTargetInput)
                            ? "filter auto-derived from the targets' names"
                            : $"each cell dropped onto {ground}";
                        string fallbackPart = _surfaceTargetEntities.Count > 0
                            ? $" (empty selection falls back to the {_surfaceTargetEntities.Count} captured)"
                            : "";
                        DistributeSummary = $"Next run: fill the footprint of the surfaces SELECTED when you click " +
                                            $"Distribute{fallbackPart} with {what}, spacing from the Axis1/Axis2 boxes " +
                                            $"along world X/Y, {filterPart}, {upDown}.";
                        return;
                    }

                    var summary = $"Next run: {grid}, each cell dropped onto {ground}, {upDown}.";
                    if (IsVerticalWithSteps(_gridAxis1Direction, GridCountAxis1Input) ||
                        IsVerticalWithSteps(_gridAxis2Direction, GridCountAxis2Input))
                        summary += " WARNING: an axis with a count above 1 points up (Z) - a dropped grid can only spread horizontally, pick X or Y for it.";
                    DistributeSummary = summary;
                    return;
                }

                DistributeSummary = $"Next run: {grid} on the origin's plane " +
                                    $"({AxisChoices.First(c => c.Axis == _gridAxis1Direction).Label} by {AxisChoices.First(c => c.Axis == _gridAxis2Direction).Label}).";
            }
            catch { /* a half-typed number must never break the panel */ }
        }

        // Onto Surface flattens step directions into the ground plane, so a vertical axis that
        // actually steps (count above 1) cannot work there - matches the check in
        // PrefabDistributor.DistributeOntoSurface, which skips axes with a count of 1.
        private static bool IsVerticalWithSteps(PrefabDistributor.DistributionAxis axis, string countInput)
        {
            bool vertical = axis == PrefabDistributor.DistributionAxis.LocalZ || axis == PrefabDistributor.DistributionAxis.WorldZ;
            return vertical && int.TryParse(countInput, out var count) && count > 1;
        }

        public void ExecuteDistributeInGrid()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { DistributeStatus = "No scene is currently open."; return; }
                if (_gridSourceIsSelection) { DistributeSelectionGrid(); return; }
                if (string.IsNullOrWhiteSpace(DistributePrefabName)) { DistributeStatus = "Enter a prefab name to distribute, or switch Grid source to Selection."; return; }

                // Coordinates mode makes a selection optional here: the origin no longer has to
                // come from an entity, so "place a grid over there" needs nothing selected.
                var selected = MaterialSwapTool.Core.SelectionMemory.GetSelection();
                if (!_originFromCoordinates && selected.Count != 1)
                {
                    DistributeStatus = $"Select exactly ONE entity to use as the grid's origin + orientation " +
                                       $"(selected: {selected.Count}), or switch Origin to Coordinates.";
                    return;
                }

                if (!int.TryParse(GridCountAxis1Input, out var count1) || count1 < 1) { DistributeStatus = "Axis 1 count must be a whole number >= 1."; return; }
                if (!int.TryParse(GridCountAxis2Input, out var count2) || count2 < 1) { DistributeStatus = "Axis 2 count must be a whole number >= 1."; return; }
                if (!float.TryParse(GridAxis1AbsoluteInput, out var absolute1)) absolute1 = 3f;
                if (!float.TryParse(GridAxis2AbsoluteInput, out var absolute2)) absolute2 = 3f;
                if (!float.TryParse(GridAxis1GapInput, out var gap1)) gap1 = 0f;
                if (!float.TryParse(GridAxis2GapInput, out var gap2)) gap2 = 0f;

                // Sane upper bound - a mistyped count (e.g. an extra zero) instantiating thousands
                // of entities synchronously on the main thread would stall the game for a long
                // time with no progress feedback, which looks exactly like a crash/freeze.
                int maxTotal = GetMaxInstancesPerRun();
                if (count1 * count2 > maxTotal) { DistributeStatus = $"{count1}x{count2} = {count1 * count2} instances - above the Max Instances cap of {maxTotal}. Lower the counts, or raise Max Instances."; return; }

                var anchorName = string.IsNullOrWhiteSpace(DistributeAnchorName) ? DistributePrefabName + "_Grid" : DistributeAnchorName;
                if (!TryResolveOriginFrame(selected, out var originFrame, out var originError)) { DistributeStatus = originError; return; }

                var axis1 = new PrefabDistributor.GridAxisSpec
                {
                    Direction = PrefabDistributor.ResolveAxisDirection(_gridAxis1Direction, originFrame),
                    Count = count1, Relative = _gridAxis1Relative, Absolute = absolute1, Gap = gap1,
                };
                var axis2 = new PrefabDistributor.GridAxisSpec
                {
                    Direction = PrefabDistributor.ResolveAxisDirection(_gridAxis2Direction, originFrame),
                    Count = count2, Relative = _gridAxis2Relative, Absolute = absolute2, Gap = gap2,
                };

                BackupManager.BackupNow("before-apply");
                var result = PrefabDistributor.DistributeInGrid(EntitySelector.CurrentScene, originFrame, DistributePrefabName, axis1, axis2, anchorName, _distributeAnchorMode);

                // Everything this made, anchor included - undo has to take the anchor too or it
                // leaves an empty one behind that looks like a real object (see EditUndo).
                if (result.Success)
                    BannerlordSceneToolkit.EditUndo.CaptureCreated($"Distribute in grid ({result.Placed.Count})",
                        result.Placed.Concat(new[] { result.AnchorEntity }));

                if (!result.Success) { DistributeStatus = "Failed: " + result.Error; return; }
                var msg = $"Created '{result.AnchorEntity.Name}', placed {result.Placed.Count - result.AutoPlacedSecondaryCount} of {count1 * count2} in a {count1}x{count2} grid.";
                if (result.AutoPlacedSecondaryCount > 0) msg += $" Plus {result.AutoPlacedSecondaryCount} auto-placed paired secondary(ies).";
                if (result.Failed.Count > 0) msg += $" Failed: {string.Join("; ", result.Failed)}";
                DistributeStatus = msg;
            }
            catch (Exception ex)
            {
                DistributeStatus = "Distribute in Grid failed: " + ex.Message;
                Log.Error("ExecuteDistributeInGrid failed: " + ex);
            }
        }

        // THREE SEPARATE ROLES, which this panel used to collapse into one selection (v0.7 fix,
        // prompted by the fair question "how do I specify the target if the selection is what I
        // am copying?"):
        //
        //   WHAT to place    - Grid source: a prefab name, or copies of the selection.
        //   WHERE to start   - the origin below: the selection's frame, or typed coordinates.
        //   WHAT to land on  - Surface target below: any surface, or one named entity.
        //
        // With all three taken from the selection there was no way to say "copy THIS, starting
        // THERE, onto THAT" - the copied thing was also the origin, and the surface was always
        // whatever happened to be closest.
        private bool _originFromCoordinates;

        [DataSourceProperty]
        public string DistributeOriginModeLabel =>
            _originFromCoordinates ? "Origin: Coordinates" : "Origin: Selected entity";

        [DataSourceProperty]
        public string DistributeOriginModeColor => _originFromCoordinates ? "#8a5fc9FF" : "#3ba1c9FF";

        [DataSourceProperty]
        public string DistributeOriginInput
        {
            get => _distributeOriginInput;
            set { if (value != _distributeOriginInput) { _distributeOriginInput = value; OnPropertyChangedWithValue(value, nameof(DistributeOriginInput)); } }
        }
        private string _distributeOriginInput = "";

        public void ExecuteToggleDistributeOriginMode()
        {
            _originFromCoordinates = !_originFromCoordinates;
            OnPropertyChanged(nameof(DistributeOriginModeLabel));
            OnPropertyChanged(nameof(DistributeOriginModeColor));
            DistributeStatus = _originFromCoordinates
                ? "The grid starts at the typed coordinates. Heading still comes from the selected entity if there is one, so Local axes keep working."
                : "The grid starts at the selected entity's own position and heading.";
        }

        public void ExecuteFillDistributeOriginFromSelection()
        {
            var selection = MaterialSwapTool.Core.SelectionMemory.GetSelection();
            if (selection.Count == 0) { DistributeStatus = "No entity selected to read a position from."; return; }

            var p = selection[0].GetGlobalFrame().origin;
            DistributeOriginInput = $"{p.x:0.###}, {p.y:0.###}, {p.z:0.###}";
            DistributeStatus = $"Origin set to '{selection[0].Name}' at {DistributeOriginInput}." +
                               (_originFromCoordinates ? "" : " Switch Origin to Coordinates to actually use it.");
        }

        // Empty means "land on whatever is beneath each cell" - the original behaviour. One or
        // more names (comma-separated) restrict it to those entities (and their children, since
        // collision usually lives there); clutter in the way is passed through rather than
        // blocking the cell. See SurfaceSnap, which parses the commas.
        [DataSourceProperty]
        public string SurfaceTargetInput
        {
            get => _surfaceTargetInput;
            set { if (value != _surfaceTargetInput) { _surfaceTargetInput = value; OnPropertyChangedWithValue(value, nameof(SurfaceTargetInput)); RefreshDistributeSummary(); } }
        }
        private string _surfaceTargetInput = "";

        // Two independent surface-mode rotations (2026-08-23, split by request: "Extra heading
        // only rotates it after placement, it doesn't allow a non-world-aligned orientation of
        // the grid itself"): ENTITY spins each placed piece in place; GRID pivots the whole
        // cell lattice around the origin. Both about world Z, both 0 = off.
        [DataSourceProperty]
        public string SurfaceEntityRotInput
        {
            get => _surfaceEntityRotInput;
            set { if (value != _surfaceEntityRotInput) { _surfaceEntityRotInput = value; OnPropertyChangedWithValue(value, nameof(SurfaceEntityRotInput)); } }
        }
        private string _surfaceEntityRotInput = "0";

        [DataSourceProperty]
        public string SurfaceGridRotInput
        {
            get => _surfaceGridRotInput;
            set { if (value != _surfaceGridRotInput) { _surfaceGridRotInput = value; OnPropertyChangedWithValue(value, nameof(SurfaceGridRotInput)); } }
        }
        private string _surfaceGridRotInput = "0";

        private float ParsedSurfaceEntityRot =>
            float.TryParse(SurfaceEntityRotInput, out var deg) ? deg : 0f;
        private float ParsedSurfaceGridRot =>
            float.TryParse(SurfaceGridRotInput, out var deg) ? deg : 0f;

        public void ExecuteFillSurfaceTargetFromSelection()
        {
            var selection = MaterialSwapTool.Core.SelectionMemory.GetSelection();
            if (selection.Count == 0) { DistributeStatus = "No entity selected to use as the surface target."; return; }

            // ALL selected entities, comma-joined - one run can span several surfaces at once (a
            // grid crossing two terrace pieces and the rocks between them). Distinct because a
            // multi-select can easily hold same-named instances, and the filter is by name anyway.
            var names = selection.Select(e => e.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
            if (names.Count == 0) { DistributeStatus = "The selected entities have no usable names to filter by."; return; }

            // The references power Fill Target Surfaces' footprint; the names power the ray filter.
            _surfaceTargetEntities.Clear();
            _surfaceTargetEntities.AddRange(selection);

            SurfaceTargetInput = string.Join(", ", names);
            DistributeStatus = names.Count == 1
                ? $"Surface filter: '{names[0]}'. Only that entity (and its children) counts as ground; " +
                  "clear the box to land on whatever is beneath instead."
                : $"Surface filter: {names.Count} names from {selection.Count} selected entities. A cell lands on " +
                  "whichever of them is beneath it; clear the box to land on anything.";
            // Explicit: the SurfaceTargetInput setter skips its refresh when the joined names are
            // unchanged, but the captured-entity COUNT (which fill mode's summary reports) can
            // still have changed.
            RefreshDistributeSummary();
        }

        // The grid's starting frame. Coordinates supply the POSITION only - heading still comes
        // from the selected entity when there is one, because throwing the rotation away would
        // silently turn every Local axis choice into a World one.
        private bool TryResolveOriginFrame(System.Collections.Generic.List<GameEntity> selection,
                                           out MatrixFrame frame, out string error)
        {
            error = null;
            frame = MatrixFrame.Identity;

            if (_originFromCoordinates)
            {
                if (!TryParseVec3(DistributeOriginInput, out var origin))
                {
                    error = "Origin coordinates must be 'x, y, z' - use Fill From Selection, or switch Origin back to Selected entity.";
                    return false;
                }
                if (selection != null && selection.Count > 0)
                    frame = selection[0].GetGlobalFrame();
                frame.origin = origin;
                return true;
            }

            if (selection == null || selection.Count == 0)
            {
                error = "Select an entity to set the grid's origin and heading, or switch Origin to Coordinates.";
                return false;
            }
            frame = selection[0].GetGlobalFrame();
            return true;
        }

        // (Coordinates use this class's existing TryParseVec3 - same "x, y, z" format the
        // Mirror pivot box accepts.)

        // ALIGN EACH PIECE TO THE SURFACE, or keep it upright. Off by default: a column on a
        // slope should be vertical and simply meet the ground at a different height, not lean.
        // Worth turning on for things that genuinely lie ON a surface - tiles, decals, debris.
        private bool _surfaceAlign;

        // The ACTUAL entities captured by Use Selected Entities, kept alongside the names it
        // writes into the box (same capture-until-replaced pattern as the Live Reference). Fill
        // Target Surfaces needs real references for its footprint: a bounding box is a scene-wide
        // claim in a way a raycast is not, and resolving the names instead would let a same-named
        // entity across the map silently inflate the box to span both. The text box stays the
        // source of truth for the per-cell ray FILTER; these are only the footprint.
        private readonly System.Collections.Generic.List<GameEntity> _surfaceTargetEntities =
            new System.Collections.Generic.List<GameEntity>();

        // Extent: typed Axis1/Axis2 counts (the default), or derived from the captured target
        // surfaces' combined footprint. Same manual/derived pairing as the path's Fill Path Length.
        private bool _surfaceFillExtent;

        // The per-run instance cap, previously hard-coded at 500 in every distributor. It exists
        // to stop an accidental 100x100 from stalling the editor, but Fill Target Surfaces hits
        // it legitimately (two plazas tiled edge-to-edge with a 1-unit column IS 650 cells), so
        // it's typed now. Unparseable input falls back to 500; clamped to [1, 100000].
        private string _maxInstancesInput = "500";

        [DataSourceProperty]
        public string MaxInstancesInput
        {
            get => _maxInstancesInput;
            set { if (value != _maxInstancesInput) { _maxInstancesInput = value; OnPropertyChangedWithValue(value, nameof(MaxInstancesInput)); } }
        }

        private int GetMaxInstancesPerRun()
        {
            if (!int.TryParse(MaxInstancesInput, out var max)) return 500;
            return Math.Max(1, Math.Min(100000, max));
        }

        [DataSourceProperty]
        public string SurfaceExtentModeLabel =>
            _surfaceFillExtent
                ? "Extent: Fill Target Surfaces (axis counts + origin ignored)"
                : "Extent: From Axis1/Axis2 counts";

        public void ExecuteToggleSurfaceExtentMode()
        {
            _surfaceFillExtent = !_surfaceFillExtent;
            OnPropertyChanged(nameof(SurfaceExtentModeLabel));
            DistributeStatus = _surfaceFillExtent
                ? "Select the surfaces to fill, then Distribute - the selection IS the targets (footprint + filter). " +
                  "Axis counts and origin are ignored; spacing/gap still apply, stepping along world X/Y."
                : "The grid uses the typed Axis1/Axis2 counts, starting at the origin.";
            RefreshDistributeSummary();
        }

        [DataSourceProperty]
        public string SurfaceAlignLabel =>
            _surfaceAlign ? "Onto surface: tilt to match ground" : "Onto surface: keep upright";

        [DataSourceProperty]
        public string SurfaceAlignColor => _surfaceAlign ? "#8a5fc9FF" : "#3ba1c9FF";

        public void ExecuteToggleSurfaceAlign()
        {
            _surfaceAlign = !_surfaceAlign;
            OnPropertyChanged(nameof(SurfaceAlignLabel));
            OnPropertyChanged(nameof(SurfaceAlignColor));
            DistributeStatus = _surfaceAlign
                ? "Each piece will be tilted to sit flush with the ground beneath it."
                : "Each piece keeps its upright rotation; only its height follows the ground.";
            RefreshDistributeSummary();
        }

        // DISTRIBUTE ONTO SURFACE. Same grid, but every cell is dropped onto whatever is beneath
        // it - so a grid of columns follows a hillside or a stepped terrace instead of hanging in
        // the air off one flat plane. Honours the same Grid source toggle as the flat grid, so it
        // can tile either a named prefab or copies of the selection.
        public void ExecuteDistributeOntoSurface()
        {
            try
            {
                if (!EntitySelector.HasOpenScene) { DistributeStatus = "No scene is currently open."; return; }

                // Fill Target Surfaces derives its own footprint from the captured targets - no
                // origin entity, no counts - so it branches off before the selection checks.
                if (_surfaceFillExtent) { DistributeFillTargetSurfaces(); return; }

                var selected = MaterialSwapTool.Core.SelectionMemory.GetSelection();
                if (selected.Count == 0)
                {
                    DistributeStatus = _gridSourceIsSelection
                        ? "Nothing selected - select what should be repeated across the surface."
                        : "Select ONE entity to set the grid's origin and heading, then Distribute Onto Surface.";
                    return;
                }
                if (!_gridSourceIsSelection && string.IsNullOrWhiteSpace(DistributePrefabName))
                {
                    DistributeStatus = "Enter a prefab name to distribute, or switch Grid source to Selection.";
                    return;
                }

                if (!int.TryParse(GridCountAxis1Input, out var count1) || count1 < 1) { DistributeStatus = "Axis 1 count must be a whole number >= 1."; return; }
                if (!int.TryParse(GridCountAxis2Input, out var count2) || count2 < 1) { DistributeStatus = "Axis 2 count must be a whole number >= 1."; return; }
                if (!float.TryParse(GridAxis1AbsoluteInput, out var absolute1)) absolute1 = 3f;
                if (!float.TryParse(GridAxis2AbsoluteInput, out var absolute2)) absolute2 = 3f;
                if (!float.TryParse(GridAxis1GapInput, out var gap1)) gap1 = 0f;
                if (!float.TryParse(GridAxis2GapInput, out var gap2)) gap2 = 0f;

                int maxTotal = GetMaxInstancesPerRun();
                int perCell = _gridSourceIsSelection ? selected.Count : 1;
                int cells = _gridSourceIsSelection ? count1 * count2 - 1 : count1 * count2;
                if (cells * perCell > maxTotal)
                {
                    DistributeStatus = $"{count1}x{count2} x {perCell} per cell = {cells * perCell} instances - above " +
                                       $"the Max Instances cap of {maxTotal}. Lower the counts, or raise Max Instances.";
                    return;
                }

                if (!TryResolveOriginFrame(selected, out var originFrame, out var originError)) { DistributeStatus = originError; return; }
                var sourceLabel = _gridSourceIsSelection ? (selected[0].Name ?? "Selection") : DistributePrefabName;
                var anchorName = string.IsNullOrWhiteSpace(DistributeAnchorName) ? sourceLabel + "_Surface" : DistributeAnchorName;

                var axis1 = new PrefabDistributor.GridAxisSpec
                {
                    Direction = PrefabDistributor.ResolveAxisDirection(_gridAxis1Direction, originFrame),
                    Count = count1, Relative = _gridAxis1Relative, Absolute = absolute1, Gap = gap1,
                };
                var axis2 = new PrefabDistributor.GridAxisSpec
                {
                    Direction = PrefabDistributor.ResolveAxisDirection(_gridAxis2Direction, originFrame),
                    Count = count2, Relative = _gridAxis2Relative, Absolute = absolute2, Gap = gap2,
                };

                BackupManager.BackupNow("before-apply");
                ManipulationWatcher.SuppressSelfEdit();

                var result = PrefabDistributor.DistributeOntoSurface(
                    EntitySelector.CurrentScene,
                    _gridSourceIsSelection ? null : DistributePrefabName,
                    selected, originFrame, axis1, axis2, _surfaceAlign, SurfaceTargetInput?.Trim(), anchorName, _distributeAnchorMode,
                    ParsedSurfaceEntityRot, ParsedSurfaceGridRot);

                if (result.Success)
                    BannerlordSceneToolkit.EditUndo.CaptureCreated($"Distribute onto surface ({result.Placed.Count})",
                        result.Placed.Concat(new[] { result.AnchorEntity }));

                if (!result.Success) { DistributeStatus = "Failed: " + result.Error; return; }

                var msg = $"Created '{result.AnchorEntity.Name}': dropped {result.Placed.Count} piece(s) onto the surface " +
                          $"in a {count1}x{count2} grid" + (_surfaceAlign ? ", tilted to match the ground." : ", kept upright.");
                if (result.Failed.Count > 0) msg += $" {string.Join("; ", result.Failed.Take(3))}";
                DistributeStatus = msg;
            }
            catch (Exception ex)
            {
                DistributeStatus = "Distribute Onto Surface failed: " + ex.Message;
                Log.Error("ExecuteDistributeOntoSurface failed: " + ex);
            }
        }

        // Fill Target Surfaces: same drop-onto-surface placement, but the footprint is derived
        // from the surfaces captured by Use Selected Entities instead of typed as counts. Spacing
        // and gap come from the same Axis1/Axis2 boxes (axis DIRECTIONS and counts are ignored -
        // the steps are world X/Y, the only ones that tile a world-aligned bounding box).
        private void DistributeFillTargetSurfaces()
        {
            if (_gridSourceIsSelection)
            {
                DistributeStatus = "Fill Target Surfaces places a named prefab - switch Source to Prefab Name.";
                return;
            }
            if (string.IsNullOrWhiteSpace(DistributePrefabName))
            {
                DistributeStatus = "Enter a prefab name to fill the surfaces with.";
                return;
            }

            if (!float.TryParse(GridAxis1AbsoluteInput, out var absolute1)) absolute1 = 3f;
            if (!float.TryParse(GridAxis2AbsoluteInput, out var absolute2)) absolute2 = 3f;
            if (!float.TryParse(GridAxis1GapInput, out var gap1)) gap1 = 0f;
            if (!float.TryParse(GridAxis2GapInput, out var gap2)) gap2 = 0f;

            // THE ACTIVE SELECTION IS THE TARGETS - direct feedback: "can't the active selection
            // be sufficient without specifying the names + fill selection????". In counts mode
            // the selection is already taken (it means grid origin), which is why the capture
            // button exists at all - but in fill mode nothing else uses the selection, so
            // requiring a separate capture step was pure ceremony. What's selected when you click
            // Distribute is what gets filled; the captured set (Use Selected Entities) is only a
            // fallback for an empty selection, so the old workflow still works too.
            var liveSelection = MaterialSwapTool.Core.SelectionMemory.GetSelection();
            var targets = liveSelection.Count > 0 ? liveSelection : _surfaceTargetEntities;
            string targetSource = liveSelection.Count > 0 ? "selected" : "captured";

            // NAME-ONLY TARGETING (2026-08-23, "is fill target surface compatible with a
            // name-only option?"): nothing selected or captured, but the Only-land-on box has
            // names - resolve them to entities right here with the same exact-name match
            // SurfaceSnap uses for the rays, and fill those. The footprint then spans EVERY
            // same-named entity in the scene, which is exactly why selection is the primary
            // path - but as an explicitly typed request that span is the point, and the status
            // reports how many matched so a surprise is visible immediately.
            if (targets.Count == 0 && !string.IsNullOrWhiteSpace(SurfaceTargetInput))
            {
                var wanted = SurfaceTargetInput.Split(',')
                    .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                var all = new List<GameEntity>();
                try { EntitySelector.CurrentScene.GetEntities(ref all); } catch { }
                targets = all.Where(e => MaterialSwapTool.Core.EntitySelector.IsValidEntity(e) &&
                        wanted.Any(w => string.Equals(e.Name, w, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                targetSource = $"matched by name ({targets.Count})";
            }

            if (targets.Count == 0)
            {
                DistributeStatus = "Select the surfaces to fill (or type their exact names in 'Only land on'), " +
                                   "then Distribute. Capturing with Use Selected Entities also still works.";
                return;
            }

            // A blank filter box auto-derives the filter from the targets' own names: the mode
            // is called Fill TARGET Surfaces, so landing on random clutter sitting on them (the
            // blank-box behaviour of counts mode) would betray the name. A typed filter is
            // respected as-is.
            string filterNames = SurfaceTargetInput?.Trim();
            bool filterDerived = string.IsNullOrWhiteSpace(filterNames);
            if (filterDerived)
                filterNames = string.Join(", ", targets
                    .Where(MaterialSwapTool.Core.EntitySelector.IsValidEntity)
                    .Select(e => e.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());

            var anchorName = string.IsNullOrWhiteSpace(DistributeAnchorName)
                ? DistributePrefabName + "_Surface"
                : DistributeAnchorName;

            BackupManager.BackupNow("before-apply");
            ManipulationWatcher.SuppressSelfEdit();

            var result = PrefabDistributor.DistributeFillSurfaces(
                EntitySelector.CurrentScene, DistributePrefabName, targets,
                _gridAxis1Relative, absolute1, gap1,
                _gridAxis2Relative, absolute2, gap2,
                _surfaceAlign, filterNames, anchorName, _distributeAnchorMode, GetMaxInstancesPerRun(),
                ParsedSurfaceEntityRot, ParsedSurfaceGridRot);

            if (result.Success)
                BannerlordSceneToolkit.EditUndo.CaptureCreated($"Fill target surfaces ({result.Placed.Count})",
                    result.Placed.Concat(new[] { result.AnchorEntity }));

            if (!result.Success) { DistributeStatus = "Failed: " + result.Error; return; }

            var msg = $"Created '{result.AnchorEntity.Name}': filled {targets.Count} {targetSource} surface(s) " +
                      $"with {result.Placed.Count} piece(s) (derived {result.DerivedCount1}x{result.DerivedCount2} grid, " +
                      $"cells off the surfaces skipped){(_surfaceAlign ? ", tilted to match the ground." : ", kept upright.")}" +
                      (filterDerived ? $" Filter auto-derived from the targets: {filterNames}." : "");
            if (result.Failed.Count > 0) msg += $" {string.Join("; ", result.Failed.Take(3))}";
            DistributeStatus = msg;
        }

        // Selection-source grid. Unlike the by-name path this accepts ANY number of selected
        // entities: one tiles one thing, several tile the whole assembly as a unit. The FIRST
        // selected entity supplies the origin and orientation, matching what the by-name path
        // does with its single required entity.
        private void DistributeSelectionGrid()
        {
            var selected = MaterialSwapTool.Core.SelectionMemory.GetSelection();
            if (selected.Count == 0) { DistributeStatus = "Nothing selected - select what should be repeated."; return; }

            if (!int.TryParse(GridCountAxis1Input, out var count1) || count1 < 1) { DistributeStatus = "Axis 1 count must be a whole number >= 1."; return; }
            if (!int.TryParse(GridCountAxis2Input, out var count2) || count2 < 1) { DistributeStatus = "Axis 2 count must be a whole number >= 1."; return; }
            if (!float.TryParse(GridAxis1AbsoluteInput, out var absolute1)) absolute1 = 3f;
            if (!float.TryParse(GridAxis2AbsoluteInput, out var absolute2)) absolute2 = 3f;
            if (!float.TryParse(GridAxis1GapInput, out var gap1)) gap1 = 0f;
            if (!float.TryParse(GridAxis2GapInput, out var gap2)) gap2 = 0f;

            // The cap counts ENTITIES, not cells: with a multi-entity selection each cell places
            // one copy per selected entity, so a modest-looking 5x5 of a 12-piece assembly is
            // already 288 instantiations.
            int maxTotal = GetMaxInstancesPerRun();
            int cells = count1 * count2 - 1;                 // cell (0,0) keeps the originals
            int totalInstances = cells * selected.Count;
            if (totalInstances > maxTotal)
            {
                DistributeStatus = $"{count1}x{count2} cells x {selected.Count} selected = {totalInstances} copies - above " +
                                   $"the Max Instances cap of {maxTotal}. Lower the counts, select fewer pieces, or raise Max Instances.";
                return;
            }

            if (!TryResolveOriginFrame(selected, out var originFrame, out var originError)) { DistributeStatus = originError; return; }
            var anchorName = string.IsNullOrWhiteSpace(DistributeAnchorName)
                ? (selected[0].Name ?? "Selection") + "_Grid"
                : DistributeAnchorName;

            var axis1 = new PrefabDistributor.GridAxisSpec
            {
                Direction = PrefabDistributor.ResolveAxisDirection(_gridAxis1Direction, originFrame),
                Count = count1, Relative = _gridAxis1Relative, Absolute = absolute1, Gap = gap1,
            };
            var axis2 = new PrefabDistributor.GridAxisSpec
            {
                Direction = PrefabDistributor.ResolveAxisDirection(_gridAxis2Direction, originFrame),
                Count = count2, Relative = _gridAxis2Relative, Absolute = absolute2, Gap = gap2,
            };

            BackupManager.BackupNow("before-apply");
            ManipulationWatcher.SuppressSelfEdit();

            var result = PrefabDistributor.DistributeSelectionInGrid(
                EntitySelector.CurrentScene, selected, originFrame, axis1, axis2, anchorName, _distributeAnchorMode);

            if (result.Success)
                BannerlordSceneToolkit.EditUndo.CaptureCreated($"Distribute selection in grid ({result.Placed.Count})",
                    result.Placed.Concat(new[] { result.AnchorEntity }));

            if (!result.Success) { DistributeStatus = "Failed: " + result.Error; return; }

            var msg = $"Created '{result.AnchorEntity.Name}': {result.Placed.Count} copy(ies) of {selected.Count} selected " +
                      $"entity(ies) across a {count1}x{count2} grid. Your originals were left alone and fill the first cell.";
            if (result.Failed.Count > 0) msg += $" Failed: {string.Join("; ", result.Failed.Take(3))}";
            DistributeStatus = msg;
        }
    }
}

using System;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // Accordion-style reference covering every tool in the mod, not just Material Swap Tool - a
    // vertical scrollable list of topics that expand in place when clicked (see
    // DocumentationTopicVM), replacing the old "3 buttons + one shared body pane" layout that
    // didn't scale and was reachable only from Material Swap Tool's own panel. Opened from any
    // tool's own Documentation button now (Material Swap, Flora Swap, Continuous Recolor, Scene
    // Analyzer, Prefab Swapper) - it's the same shared window/content everywhere, just an entry
    // point on each panel.
    public class DocumentationVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;
        private MBBindingList<DocumentationTopicVM> _topics;

        // technical: true builds the TECHNICAL DOCS topic set (engineering history, confirmed
        // bugs and their diagnoses, engine limitations with the evidence) - opened from its own
        // button on F9. false builds the USER GUIDE (how things behave today), opened from the
        // Documentation buttons everywhere else. Split 2026-08-24 by request: "Separate User
        // Guide vs. Technical Docs" - the guide had accumulated changelog-style narratives that
        // belong in reference material, not in instructions.
        public DocumentationVM(Action closeAction, Action beginDragAction, bool technical = false)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
            _topics = new MBBindingList<DocumentationTopicVM>();

            if (technical)
            {
                BuildTechnicalTopics();
                return;
            }

            // --- Material Swap Tool (F8) ---
            AddTopic("Material Swap Tool (F8) - Overview", OverviewDoc);
            AddTopic("Material Swap Tool - Rules (FROM/TO materials)", RulesDoc);
            AddTopic("Material Swap Tool - Color factor (2 kinds)", ColorFactorDoc);
            AddTopic("Material Swap Tool - Selection modes & Get Input Rules", SelectionDoc);
            AddTopic("Material Swap Tool - Fill In Overrides & Fill Rules From Entity", FillInOverridesDoc);
            AddTopic("Material Swap Tool - Check LOD Mismatches & Fix Color Mismatches", LodMismatchDoc);
            AddTopic("Material Swap Tool - Rename Changed / Track by UID / Tag Changed", CheckboxesDoc);
            AddTopic("Material Swap Tool - Revert to Normal, Undo Last Batch, Simplify for Export", RevertUndoSolidifyDoc);
            AddTopic("Material Swap Tool - Presets: save, load, browse", PresetsDoc);
            AddTopic("Material Swap Tool - Preset Version History", PresetHistoryDoc);
            AddTopic("Material Swap Tool - Batch History & screenshots", BatchHistoryDoc);
            AddTopic("Backups - how they work", BackupsDoc);
            AddTopic("Backups (F9) - the panel, settings and no-change skip", BackupPanelDoc);
            AddTopic("Backups - Notification settings", NotificationSettingsDoc);
            AddTopic("Selecting in the editor (why it is delayed)", SelectInEditorDoc);
            AddTopic("Preview vs Dry Run, the results flyout and Go buttons", PreviewAndFlyoutDoc);
            AddTopic("Set Input Entity (A) - Infer Material / Infer Color", InferRulesDoc);
            AddTopic("Import / Export - where everything goes", ImportExportDoc);
            AddTopic("Clearing rules and text fields (Ctrl/Shift+Backspace)", ClearingDoc);
            AddTopic("Keyboard shortcuts - Isolate, Repeat Last, and the F9 switches", ShortcutsDoc);
            AddTopic("Numeric transform - drag, then type a number", NumericTransformDoc);

            // --- Flora Swap Tool (F9) ---
            AddTopic("Flora Swap Tool - RETIRED (F9 is now Backups)", FloraSwapDoc);

            // --- Continuous Recolor ---
            AddTopic("Continuous Recolor - Overview & arming", ContinuousRecolorDoc);
            AddTopic("Continuous Recolor - Palettes (multi-category presets)", RecolorPaletteDoc);
            AddTopic("Continuous Recolor - Get Input from Selection & seeded sampling", RecolorSamplingDoc);

            // --- Editors ---
            AddTopic("Category Editor", CategoryEditorDoc);
            AddTopic("Culture Editor", CultureEditorDoc);
            AddTopic("Culture Preset Generator", CultureGeneratorDoc);

            // --- Scene Analyzer (F7) ---
            AddTopic("Scene Analyzer (F7) - Overview", SceneAnalyzerOverviewDoc);
            AddTopic("Scene Analyzer - Full Scan: what each check looks for", SceneAnalyzerChecksDoc);
            AddTopic("Scene Analyzer - Battle / Skirmish / Siege mode checks", SceneAnalyzerModeChecksDoc);
            AddTopic("Scene Analyzer - Fix actions (duplicates, physics, LODs, broken prefabs)", SceneAnalyzerActionsDoc);
            AddTopic("Scene Analyzer - Scene Requirement Sets", SceneAnalyzerRequirementsDoc);
            AddTopic("Scene Analyzer - Tag / Select tools (invisible, locked, interior, unbroken)", TaggingToolsDoc);
            AddTopic("Scene Analyzer - Delete Interiors & the protected whitelist", InteriorWhitelistDoc);
            AddTopic("Scene Analyzer - Deprecated / Legacy Checks", DeprecatedChecksDoc);

            // --- Prefab Swapper (F6) ---
            AddTopic("Prefab Swapper (F5) & Distribution (F6) - Overview", PrefabSwapperDoc);
            AddTopic("Prefab Swapper (F6) - Per-scene history, Rotate (Specify), mirror & triad fixes", PrefabSwapperExtrasDoc);

            // --- Prefab Creator (F5) ---
            AddTopic("Prefab Creator (F5) - Overview", PrefabCreatorDoc);
            AddTopic("Pile Generator - layers, snapping, physics order, re-settle", PileGeneratorDoc);
        }

        // ---------------------------------------------------------------------------------
        // Added in the 2026-08-21 documentation pass: everything built after 2026-08-19, plus
        // two corrections. The overview used to say Material Swap opens with F9 (it is F8), and
        // Flora Swap used to claim F9 shows its placeholder (F9 is the Backups panel now).
        // ---------------------------------------------------------------------------------

        private const string SelectInEditorDoc =
            "Several buttons put things INTO your editor selection rather than only counting them: " +
            "'Select Rule Matches in Editor' and 'Select by Material' on F8, the Select buttons on F7, " +
            "and the automatic selection after Preview / Dry Run.\n\n" +
            "The selection appears a fraction of a second AFTER the click - that is deliberate, not " +
            "lag: the click that pressed the button also reaches the editor underneath, so the " +
            "selection is queued and applied once the editor has finished with that click. (Full " +
            "story: Technical Docs on F9, 'Selection timing'.)\n\n" +
            "The previous selection is cleared first, and it is cleared from the EDITOR'S own list " +
            "rather than from the entities we happened to scan - so anything selected outside the scope " +
            "you searched is cleared too, and results never pile onto what was already selected.\n\n" +
            "Preview and Dry Run select their matches as well, with one deliberate exception: MANUAL " +
            "mode. There your selection IS the input, so re-selecting the matched subset would feed a " +
            "narrower input into the next run - press Preview twice and the scope would silently shrink " +
            "each time. It says so in the status line instead of quietly doing nothing.\n\n" +
            "If a selection ever seems not to take, tool.log records what the editor actually confirmed: " +
            "look for '[SelectInEditor] applied: N/M confirmed selected by MBEditor.'";

        private const string PreviewAndFlyoutDoc =
            "Dry Run and Preview answer different questions - they are no longer two ways of asking the " +
            "same one.\n\n" +
            "DRY RUN gives counts: how many entities, how many material slots, how many entity-wide " +
            "colors would change. Nothing is written.\n\n" +
            "PREVIEW opens the results flyout and lists WHICH entities would change and WHERE they are: " +
            "one line per entity (a building changing five slots is one line, not five), with its world " +
            "position and the actual material changes on that line.\n\n" +
            "GO BUTTONS. Any line that prints world coordinates gets a 'Go' button. It moves the editor " +
            "camera there AND selects what is there - the same pair of things that double-clicking an " +
            "entry in the editor's own scene list does. Selection resolves to the NEAREST entity within " +
            "5cm of the printed position rather than the first one found, because several entities can " +
            "sit inside that tolerance and the closest is the one whose coordinates got printed. If " +
            "nothing is that close, the camera still moves and the footer says so instead of selecting " +
            "something arbitrary.\n\n" +
            "Go buttons show up on any report containing coordinates, including the Scene Analyzer's " +
            "out-of-bounds list - the flyout recognises the coordinate format itself rather than each " +
            "report having to opt in. Header and summary lines carry no coordinates and get no button.\n\n" +
            "SHOW FIRST N. The flyout lists 200 entities by default; the box at the bottom changes that " +
            "(1 to 5000) and re-renders in place. It does NOT re-run the scan - the already-computed " +
            "result is reused, so the list can never drift from what the header claims. The ceiling " +
            "exists because GauntletUI builds real widgets per row and a huge list will stall it.";

        private const string InferRulesDoc =
            "'Set Input Entity (A)' and the two Infer buttons turn example prefabs into a RULE SET " +
            "rather than applying colors directly. The point is to capture what you did to one prefab " +
            "by hand and replay it across everything else.\n\n" +
            "SET INPUT ENTITY (A): select exactly one entity, click it. That is the reference.\n\n" +
            "INFER MATERIAL FROM A (matching prefab): pairs A against your current selection by walking " +
            "both in the same order and matching on entity NAME, then writes one rule for every slot " +
            "where the material differs - 'A used this, B uses that'. Built for the case where you " +
            "overrode a five-entity column by hand and want that same conversion applied everywhere " +
            "else. When the two sides do not line up (different entity counts, or a name mismatch at " +
            "some index) it says so in the status rather than silently pairing the wrong things: the " +
            "rules it produced are probably misaligned and worth reading before you Apply.\n\n" +
            "INFER COLOR FROM A (matching material): instead of pairing entities, this groups by " +
            "MATERIAL. For each material on A it records the color factor A carries and emits a rule " +
            "applying that color to that material wherever it appears. Use this when the thing worth " +
            "reproducing is a tint per material, not a material substitution.\n\n" +
            "Both write into the normal Rules list, so you can read, edit, invert or delete individual " +
            "rows before applying, and save the result as a preset. Neither touches the scene by itself.\n\n" +
            "White (0xFFFFFFFF) is skipped when inferring colors: it is this tool's identity/no-tint " +
            "value, so a white slot means 'untinted' rather than 'tinted white', and emitting a rule " +
            "for it would paint white over things deliberately left alone.";

        private const string BackupPanelDoc =
            "F9 opens the Backups panel.\n\n" +
            "WHAT IT SHOWS: how old the newest backup for the open scene is, colour-graded green/orange/" +
            "red by age - the failure this panel exists to prevent is not noticing that backups stopped. " +
            "Below that, the verified outcome of the last backup, because the copy runs asynchronously " +
            "and a queued path is not proof that anything landed.\n\n" +
            "TWO SWITCHES, DELIBERATELY SEPARATE. 'Backups: ON/OFF' is the master. 'Timer: ON/OFF' is " +
            "the interval timer only. They carry different risk: the timer copies files in the " +
            "background whenever it fires, while a before-apply backup happens on an action you just " +
            "took. If background copying is ever suspect, switch off the timer and KEEP before-apply. " +
            "One combined switch could not express that.\n\n" +
            "SETTINGS: interval in minutes (1-120), how many to keep per scene (1-500), and a custom " +
            "location. These persist to backup_settings.json and take effect immediately.\n\n" +
            "NO-CHANGE SKIP. An automatic backup compares size and last-write time of scene.xscene, " +
            "terrain.bin and terrain_ed.bin against the newest existing backup and skips when nothing " +
            "moved, so leaving the editor idle no longer burns through retention. Before-apply backups " +
            "NEVER skip, and any error in the comparison falls through to backing up. Look for 'Backup " +
            "skipped (auto-4min)' in tool.log.\n\n" +
            "BACKUP TOOL DATA is manual-only by design: presets, palettes, categories, cultures and the " +
            "interior whitelist are things you edit deliberately, not scene state that drifts, so they " +
            "are never copied on a timer.\n\n" +
            "WHAT A BACKUP CONTAINS: the SAVED files on disk. It does not save the scene for you and it " +
            "does not cover navmesh or flora. Anything changed but not saved is not in it - which is " +
            "exactly what the 15-minute no-save warning is for.\n\n" +
            "FOLDER SIZE. Under the backup count is what the whole Backups folder costs on disk - " +
            "scene backups, the manual Tool Data copies, anything else in there - and how much " +
            "room is left on that drive. Shown separately from the per-scene figure because " +
            "retention ('keep per scene') only ever prunes scene backups: the scene number can " +
            "sit flat while Tool Data copies keep accumulating. Refresh re-measures it.";

        private const string NotificationSettingsDoc =
            "F9 -> 'Notifications...' opens a flyout with two switches.\n\n" +
            "BACKUP WARNINGS: the on-screen messages for 'backup saving in the background', 'backup did " +
            "NOT complete' and 'backup FAILED'.\n\n" +
            "SAVE REMINDERS: the 15-minute no-save warning and the post-apply 'save the scene' nudge.\n\n" +
            "They sit behind a flyout on purpose. Switching one off makes the tool quieter about things " +
            "going wrong, so the cost of hitting one by accident is that a real problem stops announcing " +
            "itself. Turning one OFF asks for confirmation and spells out what stops appearing, and the " +
            "dialog's buttons are 'Turn off' and 'Keep them on' rather than OK/Cancel so a reflexive " +
            "click lands on the safe one. Turning one back ON is never confirmed - restoring a warning " +
            "is not the risky direction.\n\n" +
            "These silence the POPUP only. Every one of those messages still goes to tool.log as " +
            "'[BackupWarning]' or '[SaveReminder]'. A backup that failed with no popup AND no record " +
            "would leave nothing to find later, which defeats the point of having backups.\n\n" +
            "The F9 button itself reads 'Notifications (1 OFF)...' in red while anything is silenced - " +
            "silencing a warning should not also hide the fact that you silenced it.";

        private const string TaggingToolsDoc =
            "F7 has a group of buttons that find a category of entity and either TAG it or SELECT it. " +
            "Tagging exists because the editor's own search box works on tags; selecting is usually the " +
            "faster route now that it works (see 'Selecting in the editor').\n\n" +
            "TAG/SELECT INVISIBLE: entities not visible including their parents - something hidden " +
            "inside a hidden parent counts, which is why it uses IsVisibleIncludeParents rather than " +
            "the entity's own flag.\n\n" +
            "TAG/SELECT LOCKED: there is NO managed API for the editor's lock state - it exists only as " +
            "edit_mode_data locked_for_selection=\"true\" inside scene.xscene. So this reads the SAVED " +
            "scene file and matches those entries back to live entities by position (5cm tolerance). " +
            "Consequence: it reflects the last SAVED state, not unsaved lock changes.\n\n" +
            "FINDING THE TAGS: save, then search in the editor with the t: prefix (hidden is the " +
            "keyword for invisible). Un-hiding or unlocking by hand leaves the tag in place; use " +
            "the Untag buttons to clear it, so retagging later only marks what is hidden NOW.\n\n" +
            "TAG/SELECT INTERIOR: matches on the entity name and the prefab name, so an entity renamed " +
            "away from its prefab is still found. Anything whose name or prefab contains 'interior', " +
            "plus everything in the interior class list.\n\n" +
            "TAG / SELECT / BREAK UNBROKEN PREFABS: finds custom (non-native) prefabs still present as " +
            "unbroken prefab instances. The PREFIX FILTER box narrows this to prefabs whose name starts " +
            "with one of the prefixes you type (comma-separated); left empty it accepts every " +
            "non-native prefab. Break Prefab Links is destructive and cannot be undone from inside the " +
            "toolkit - a backup is taken first.\n\n" +
            "OUT-OF-BOUNDS uses the border_soft markers on the X-Y ground plane (Bannerlord is Z-UP, " +
            "so height is not part of the polygon test). It refuses to report 'everything is outside' " +
            "- never a real result - and logs its reasoning as '[Boundary]'.\n\n" +
            "SELECT WHOLE PREFAB is the odd one out in this row: it reads your CURRENT selection " +
            "instead of scanning the scene, and replaces it with the top-level prefab root of every " +
            "selected part - 'I clicked a column, give me the building'. Same thing as the " +
            "Ctrl+Shift+P shortcut (see Keyboard Shortcuts), just as a button.";

        private const string InteriorWhitelistDoc =
            "DELETE INTERIOR ENTITIES removes interior geometry from the scene wholesale. It is " +
            "destructive, cannot be undone from inside the toolkit, and takes a backup first.\n\n" +
            "PROTECTED INTERIORS. Interiors classed KNOWN_GOOD are never deleted by that button. The " +
            "count of protected entities is reported so it is clear something was skipped rather than " +
            "missed.\n\n" +
            "EDIT WHITELIST opens a flyout listing every interior class the tool knows about, each " +
            "toggleable between PROTECTED and deletable. Entries can match EXACTLY or as a PATTERN " +
            "(substring), there is a filter box for long lists, and 'Add From Scene' seeds entries from " +
            "the interiors actually present in the open scene rather than making you type names.\n\n" +
            "The list is stored as tool data - so it is covered by 'Backup Tool Data' on F9, which is " +
            "manual-only, not by the scene backup timer.\n\n" +
            "READ THE CONFIRMATION. Interiors are deletable by default and the dialog lists what " +
            "will go. The safe routine: Add From Scene, mark the keepers PROTECTED, click SAVE in " +
            "the whitelist (an unsaved edit changes nothing), then Delete Interiors and read the " +
            "list before confirming. Skipping the Save step once cost a scene's interiors - the " +
            "backup taken first is what brought them back.";

        private const string RecolorSamplingDoc =
            "GET INPUT FROM SELECTION on Continuous Recolor is the counterpart to the Material Swap " +
            "tool's 'Get Input Rules from Selection'. This tool has no rules - its inputs are which " +
            "categories are switched on and what color each carries - so the analogue is: switch on the " +
            "categories present on what you selected, and preload each one's color from what is already " +
            "painted there.\n\n" +
            "MULTIPLE CATEGORIES PER MATERIAL IS EXPECTED. A material can classify into several " +
            "categories at once (stone_wall_a is both 'stone' and 'wall') and all of them are switched " +
            "on. That widens what you can seed without changing what gets painted: at apply time the " +
            "overlap is already resolved deterministically - the first armed category in category order " +
            "wins for any given slot.\n\n" +
            "SEEDED RANDOMISATION. A category routinely carries several different colors across one " +
            "selection (three stone materials, three tints). Always picking the most common one would " +
            "make this button return the same answer forever, which is useless for exploring a palette. " +
            "Each press advances a SEED and draws a different combination, so pressing it four times " +
            "gives four coherent sets rather than four copies of one.\n\n" +
            "The draw is uniform over DISTINCT colors, not weighted by how many slots use each - " +
            "weighting would keep returning the dominant tint and defeat re-rolling. Categories are " +
            "walked in sorted order rather than dictionary order, so one seed always reproduces exactly " +
            "one set.\n\n" +
            "THE SEED BOX shows the seed just used and can be typed back in to reproduce a set you " +
            "liked. Leave it alone and each press moves to the next one.\n\n" +
            "White is skipped (identity/no-tint), so untinted slots never become a seeded 'color'. " +
            "Materials matching NO category are counted and reported - that number is your category " +
            "coverage gap, and the ones worth adding in the Category Editor.\n\n" +
            "Nothing is painted by this button. It fills in the toggles and colors; click Apply Colors " +
            "when you like what you see.";

        private const string PrefabCreatorDoc =
            "F5 - Prefab Creator. Builds and manages composite prefabs, prefab families, texture sets " +
            "and pile recipes.\n\n" +
            "This documentation panel is reachable from F6/F7/F8 and Continuous Recolor; the Prefab " +
            "Creator's own panels do not currently carry a Documentation button, so its topics live " +
            "here.\n\n" +
            "TEXTURE SETS are saved color presets applied to a prefab's mesh slots - the same colour " +
            "machinery the Material Swap tool uses, saved under PrefabCreatorTool rather than shared, " +
            "because a texture set belongs to the prefab you are building.\n\n" +
            "ORIGIN TO ANCHOR moves a prefab's ORIGIN without moving the prefab: point at a spot (Fill " +
            "From Selection reads it off a selected entity), then Origin to Anchor moves the root " +
            "there and puts every child back where it was in world space, so only the point everything " +
            "measures from changes. This matters because mirror reflects the origin, distribute spaces " +
            "from it, and rotate turns about it - a bad origin makes all three look wrong when the " +
            "maths was right.\n\n" +
            "AUTO ORIGIN does the same job without needing an anchor entity placed first, and does it " +
            "to a whole selection at once - EACH prefab is measured on its own bounding box and gets " +
            "its own origin, not a shared group point. Two cycle buttons choose the preset between " +
            "them: AXIS (X/Y/Z) and SIDE (Min/Center/Max), with the label spelling out the result. " +
            "The named axis is pinned to that end of the box and the other two are centred, which is " +
            "where the seven useful presets come from: Z+Min = Bottom Center, Z+Max = Top Center, " +
            "X+Min = -X Center, X+Max = +X Center, Y+Min = -Y Center, Y+Max = +Y Center, and any " +
            "axis + Center = Middle Center (the box's true centre, the same point whichever axis is " +
            "showing).\n\n" +
            "IT SKIPS ENTITIES THAT HAVE GEOMETRY OF THEIR OWN, and says which. The trick that moves " +
            "an origin without moving the prefab is putting the CHILDREN back in world space after " +
            "the root moves - an entity's own mesh has no such offset to compensate with, it is drawn " +
            "at the entity's frame, so moving that frame moves the object. On a meshed entity this " +
            "cannot do what its name promises, so it declines instead of quietly sliding real geometry " +
            "around. Composite prefabs - an empty root with the meshes on children, which is what New " +
            "Prefab builds - are exactly the case it is for. If you clicked a child part rather than " +
            "the prefab, Ctrl+Shift+P promotes the selection to the roots first.\n\n" +
            "Both take a backup first and both are undoable.\n\n" +
            "AN ENTITY WITH NO EMPTY ABOVE IT CANNOT HAVE ITS ORIGIN CHANGED - its mesh is drawn at " +
            "its own frame, so there is nothing to offset against. Parent it under an empty first " +
            "(New Prefab does exactly this), then re-origin the empty.\n\n" +
            "SET PRIMARY / MATCH SECONDARY ORIGIN TO PRIMARY. Select one prefab and click Set " +
            "Primary; select another and click Match Secondary Origin to Primary - the second " +
            "prefab's origin is moved (without moving the prefab) to the same world point as the " +
            "first's, so the two now share a pivot. The use case is furniture decorating: build the " +
            "cupboard as one prefab and its bowls and bottles as a 'cupboard_details' prefab with a " +
            "matched origin, and from then on copying the cupboard's transform and pasting it onto " +
            "the details drops them exactly in place, rotation included. Name the details prefab " +
            "after the furniture so a filter for the cupboard finds both.\n\n" +
            "See 'Pile Generator' for the scattering tool.";

        private const string PileGeneratorDoc =
            "The Pile Generator places many instances of several prefabs at once - '30 rocks and 10 " +
            "sticks, slapped together into a debris pile' - instead of hand-placing each piece.\n\n" +
            "ROWS ARE LAYERS, TOP TO BOTTOM. The top row is the top of the pile and the bottom row is " +
            "the base. Generation runs in REVERSE (base first), because a piece that snaps has to have " +
            "something already there to land on.\n\n" +
            "PER-ROW SETTINGS: prefab name, count, Texture (a saved texture set), Physics, and Snap.\n" +
            "  - SNAP ON: the piece raycasts straight down and settles on whatever is beneath it.\n" +
            "  - SNAP OFF: flat scatter at the reference height.\n\n" +
            "PHYSICS: DELETE makes that row's pieces permanently collision-free. Three things to " +
            "know:\n" +
            "  - The strip runs AFTER the whole pile is built (snapping raycasts onto things that " +
            "HAVE collision, so stripping a base layer early would drop everything above it through " +
            "to the terrain), and covers each piece's children too.\n" +
            "  - Stripped pieces are created as broken-out copies tagged pile_nophys (they save as " +
            "loose meshes, not prefab instances - the only form whose physics removal survives a " +
            "reload), and they stop responding to click-selection - box-select them or grab the " +
            "pile anchor.\n" +
            "  - Collision in a LIVE session only updates on load: after saving, the strip pass runs " +
            "automatically (~2s after any save, confirmed by the 're-stripped' warning) and a " +
            "RELOAD is what makes the pieces actually walk-through. Judge results after a reload, " +
            "never mid-session. The 'Strip Physics From SAVED Scene' button is a MANUAL FALLBACK " +
            "only - for a scene file restored from a backup or edited outside the editor, or if " +
            "tool.log shows no '[Pile] auto re-strip' line after a save. In normal use it reports " +
            "'already stripped', which is correct. (Why it works this way: Technical Docs on F9, " +
            "'Delete Physics'.)\n\n" +
            "THE ANCHOR SHOULD BE AN EMPTY. Single Pile scatters around the selected entity's " +
            "origin; a scaled-down mesh entity as anchor has produced failed generations, and an " +
            "empty is the clean case: move the empty, regenerate, and the pile follows. Pieces in " +
            "upper layers do not always settle onto the layer beneath - nudge the stragglers down " +
            "by hand, or bury the pile slightly, which also hides any oddities.\n\n" +
            "TEXTURE per row: 'Capture From Selection' on the Texture Overrides flyout reads the " +
            "colors off a selected entity and saves them as a named texture set; pick it on a row " +
            "and every piece of that row is generated already recolored.\n\n" +
            "TWO MODES. Single Pile scatters around ONE selected entity within Scatter Radius. Scatter " +
            "Area spreads across the combined top-down footprint of everything you selected - built for " +
            "covering a floor. Area mode retries a few times per instance and then skips it: an " +
            "irregular footprint means some random samples legitimately miss the floor, and a miss " +
            "should skip that piece rather than invent a placement with no surface under it.\n\n" +
            "TAG PLACED ENTITIES (optional) writes a tag onto every piece as it is created. It is read " +
            "at placement time, so set it BEFORE clicking Generate - it is not retro-fitted onto an " +
            "existing pile. It is applied IN ADDITION to the internal pile_<anchor> tag, never instead " +
            "of it; losing that one would break the tool's own 'select what I just made'.\n\n" +
            "RE-SETTLE (FORCE DOWN) re-runs the downward raycast on pieces already in the scene, so " +
            "anything left floating - a layer whose support was deleted, or a pile dragged onto new " +
            "ground - drops onto whatever is beneath it now. It acts on your selection if you have one, " +
            "otherwise on the last pile generated. Three details make it work rather than no-op: each " +
            "piece is lifted above the pile before casting so it cannot hit its own collision and " +
            "refuse to move; pieces are processed lowest-first so anything that lands somewhere new is " +
            "already settled before the piece above casts onto it; and selecting a pile ANCHOR settles " +
            "its children individually instead of dropping the pile as a rigid block. A piece with " +
            "nothing beneath it is put back exactly where it was and counted as 'missed'.\n\n" +
            "INTERACTION WORTH KNOWING: pieces with Physics: Delete have no collision afterwards, so a " +
            "later Re-Settle passes straight through them. Settle first, then strip, if you want both.\n\n" +
            "RANDOMIZE ROTATION breaks up the too-uniform look of a fresh pile or a Distribute run. " +
            "Type a max degrees per axis (X / Y / Z) and every targeted piece draws its OWN random " +
            "angle from within that range - per piece, not one shared spin, which is the point. Z is " +
            "yaw and defaults to 180 (anything within a half-turn either way); X and Y lean pieces " +
            "over and default to 0 so upright things stay upright unless you ask. Targets follow the " +
            "same rule as Re-Settle: your selection if you have one (a selected pile anchor expands " +
            "to its children, so the pieces vary individually instead of the pile swinging as a " +
            "lump), otherwise the last pile generated. Rotation only - the engine has no scale " +
            "setter, and position jitter is what Scatter Radius already does. Undoable.\n\n" +
            "RECIPES save the whole row list plus scatter radius and placement tag. Export All Recipes / " +
            "Import Recipes move them through PrefabCreatorTool\\Exports\\PileRecipes. Import keeps YOUR " +
            "copy on a name clash rather than overwriting, and refuses a file that parses but has no " +
            "entry with a prefab name - that would import as a recipe that loads fine and then generates " +
            "nothing.";

        private const string ImportExportDoc =
            "Everything shareable exports to a folder you can zip and send. Import never overwrites " +
            "what you already have unless you say so - a name clash keeps YOUR copy and reports it.\n\n" +
            "MATERIAL SWAP (F8) presets: MaterialSwapTool\\Exports\n" +
            "CONTINUOUS RECOLOR palettes: MaterialSwapTool\\Exports\\Palettes\n" +
            "MATERIAL CATEGORIES (Category Editor): MaterialSwapTool\\Exports\\Categories\n" +
            "PILE RECIPES: PrefabCreatorTool\\Exports\\PileRecipes\n\n" +
            "Categories import differently from everything else: they are ONE dictionary, not a " +
            "collection of named items, so a whole-file replace would throw away your local edits. " +
            "Import MERGES instead - new categories are added, existing ones gain any patterns they " +
            "were missing, and nothing is ever removed. Deleting patterns stays a Category Editor " +
            "job.\n\n" +
            "Palettes and recipes get their own subfolders on purpose: a preset, a palette and a recipe " +
            "are all 'a .json with a Name' and a folder scan could not otherwise tell them apart, so an " +
            "import of one would happily swallow the others.\n\n" +
            "Pile recipes live under PrefabCreatorTool rather than joining the Material Swap exports " +
            "because a recipe belongs to the Prefab Creator - otherwise 'zip my Exports folder and send " +
            "it' would mean different things depending on which panel you asked.\n\n" +
            "Imports are validated rather than trusted: a file that parses but carries nothing usable " +
            "(a palette with no category colours, a recipe with no prefab names) is refused and " +
            "reported, because importing it would produce something that loads cleanly and then does " +
            "nothing.\n\n" +
            "Each panel also has an Open Folder button, since there is no file dialog available inside " +
            "the editor's UI.";

        private const string PrefabSwapperExtrasDoc =
            "Details of F6 worth knowing about.\n\n" +
            "HISTORY IS PER SCENE. The swap log is a single shared file covering every scene you have " +
            "ever swapped in; the history panel lists only batches belonging to the OPEN scene, and " +
            "Undo/Redo refuse a batch from a different scene outright - a cross-scene undo matches by " +
            "name and position, and copied scenes have same-named entities at the same coordinates, so " +
            "it could silently swap the wrong thing. Refusals log as '[SceneGuard]'. (More: Technical " +
            "Docs, 'F6 fixes'.)\n\n" +
            "ROTATE (SPECIFY) takes an angle in degrees rather than fixed increments. Negatives rotate " +
            "the other way and values over 360 are allowed; 0 is rejected as a no-op.\n\n" +
            "MIRROR WITH 'EXACT ORIGIN POINT' reflects the anchor's origin across the mirror plane, so " +
            "the mirrored prefab gets a correctly mirrored origin of its own.";

        private const string ClearingDoc =
            "CTRL+BACKSPACE or SHIFT+BACKSPACE clears the whole text field you are currently typing " +
            "in - any field, in any panel of the toolkit, not just the rule rows. Both modifiers " +
            "work: Ctrl+Backspace already means \"delete previous word\" in most text fields and " +
            "muscle memory splits between the two, so neither is the wrong guess.\n\n" +
            "CAMERA LOCK WHILE TYPING: while a toolkit text field has focus, WASD and the other " +
            "camera keys do not move the viewport - the panel owns the keyboard until you click " +
            "outside it.\n\n" +
            "This exists instead of a pair of clear buttons on every rule row. A rule row already " +
            "carries from, to, colour, invert and delete; two more per row would be four more " +
            "widgets on every row of a list that can run to hundreds, and a shortcut covers every " +
            "field in the toolkit rather than only the ones that got buttons.\n\n" +
            "WHOLE-LIST CLEARS, on the Material Swap panel:\n" +
            "  Clear All Rules - removes the rows entirely.\n" +
            "  Clear All Input (FROM) - keeps the rows, blanks the left column.\n" +
            "  Clear All Output (TO) - keeps the rows, blanks the right column.\n\n" +
            "Clearing the TO side is the useful one: it leaves exactly the blank-rule state " +
            "(FROM set, TO empty) that Fill Rules From Entity and Fill In Overrides consume, so a " +
            "preset can be stripped back to its input list and re-pointed at a different prefab " +
            "without retyping every FROM material.\n\n" +
            "All three whole-list clears confirm first, and none of them touch presets already " +
            "saved to disk. There is no undo for the rules list itself - EditUndo covers scene " +
            "entities, not panel state - which is why they ask.";

        private const string ShortcutsDoc =
            "Optional keyboard shortcuts, all switchable from F9 -> Shortcuts. All are modifier " +
            "combos rather than bare letters, because the editor owns most single keys.\n\n" +
            "CTRL/SHIFT+BACKSPACE - clears the text field you are typing in, in any panel.\n\n" +
            "SHIFT+O (Ctrl NOT held) - ISOLATE. Hides everything except the selection; press again to " +
            "restore. (This exact combo avoids two native-editor key collisions - see Technical Docs.)\n" +
            "The restore is the part that matters: it does NOT simply make everything visible. A " +
            "scene routinely has things hidden on purpose, and blanket-unhiding would silently undo " +
            "that with no way to tell what had been deliberate. Every entity's previous visibility " +
            "is recorded on isolate and written back verbatim, so anything already hidden stays " +
            "hidden. Selected entities keep their children (hiding a building's own meshes would " +
            "isolate it into invisibility) and their parents (an entity under a hidden parent is " +
            "hidden regardless of its own flag). Visibility only - nothing moves or is deleted, " +
            "which is why it needs no undo step: the restore IS the undo, and it is exact.\n" +
            "SURVIVES SAVES, RELOADS AND TEST MODE. Every entity Isolate hides is also tagged " +
            "'mst_isolated', and tags are saved with the scene. So if you save while isolated and " +
            "reload, or go into test mode and back, the toolkit re-finds the tagged entities, " +
            "announces that Isolate is still active, and Shift+O restores them exactly as before. " +
            "Saving while isolated also raises a warning, because the hidden state is then IN THE " +
            "FILE - restore and save again before the scene goes anywhere else. Entities that were " +
            "already hidden before you isolated never get the tag and are never touched.\n\n" +
            "SHIFT+R - REPEAT LAST TRANSFORM on the current selection (with Ctrl NOT held; " +
            "Ctrl+Shift+R is left alone).\n" +
            "This is the sharp one. It replays an action you may have taken minutes ago onto a " +
            "selection that is by definition different from the one it was recorded against; aim it " +
            "at the wrong selection and it quietly transforms the wrong objects. Three things blunt " +
            "it: the modifier combo, the F9 switch, and an undo step taken BEFORE it acts, so a " +
            "wrong repeat is one Undo away rather than something to find by eye.\n\n" +
            "What gets recorded: rotations, moves, and shift-drag copy+moves. Repeating a " +
            "copy+move clones again on purpose - the copy is the point of that gesture - and the " +
            "new copies become the selection, so pressing again steps along instead of stacking " +
            "copies in place. Mirror and Distribute are NOT recorded: repeating those would only " +
            "ever pile up more geometry. Scale is never repeated.\n\n" +
            "KNOWN QUIRKS: a copy made by Shift+R can be odd to select or operate on until the " +
            "scene is SAVED - that is the engine's click registry, not the copy (see Technical " +
            "Docs, 'Live-reference swaps'); and the repeat occasionally comes out reversed on X " +
            "or Y (never Z), undiagnosed - one Undo away.\n\n" +
            "CTRL+SHIFT+P - SELECT WHOLE PREFAB. Promotes the selection to its top-level " +
            "prefab root(s). Composite prefabs keep their visible meshes on CHILD entities, so " +
            "a viewport click often lands on a column or a wall segment when the whole building " +
            "is what you meant - press this and the selection walks up to the top-level parent " +
            "of every selected part, deduplicated. Works on generated piles and distributions " +
            "too (the pieces' root is the group anchor), and does nothing to an entity that is " +
            "already top-level. Selection only, nothing in the scene changes; the same action " +
            "is on F7 as 'Select Whole Prefab' for when the hotkey is off. Like every " +
            "select-in-editor feature, the new selection lands a couple of ticks after the " +
            "press - that is the deliberate deferred-selection delay, not lag.\n\n" +
            "CTRL+NUMPAD+ - GROW SELECTION BY PROXIMITY. Opens a small live panel: everything " +
            "whose ORIGIN sits within the radius of any originally-selected entity's origin joins " +
            "the selection. THE PLUS/MINUS PAIRS AND THE ARROW PAIR ARE INTERCHANGEABLE - either +, " +
            "and Up all grow; either -, and Down all shrink - so whichever keys your hand is already " +
            "near do the same job. The modifier picks the step size: plain 1, Shift 5 (crossing " +
            "open ground), Ctrl 0.25 (picking apart a cluttered corner). Typing a number on the " +
            "number row sets an exact radius instead. Enter applies, Esc puts the original " +
            "selection back. THE OPENER IS NUMPAD-ONLY on purpose: the number-row Ctrl+Equals / " +
            "Ctrl+Minus / Ctrl+0 are the panel-size keys, so the numpad belongs to Grow and the " +
            "number row to scaling - no key means two things. While this panel is open the " +
            "panel-size keys are ignored entirely; resize it from any other panel.\n\n" +
            "THE RADIUS IS ALWAYS MEASURED FROM THE SELECTION YOU STARTED WITH, never from the " +
            "last result. That is what makes shrinking exact rather than a guess: 5 -> 10 -> 5 " +
            "lands on precisely the selection you had at 5, and radius 0 is what you began with. " +
            "Growing the previous result instead would not round-trip, and 'erode' has no sane " +
            "meaning in a dense scene - practically everything selected is within a few metres of " +
            "something unselected, so a literal erode would clear almost the whole selection on " +
            "the first press. Ctrl+Numpad- on its own therefore says there is nothing to shrink " +
            "yet rather than doing something arbitrary.\n\n" +
            "ON COST, since this is the one feature here that searches the whole scene: the " +
            "arithmetic is never the problem, the managed/native boundary is. Every entity's " +
            "origin is read ONCE into a plain array when the panel opens, cached for ten seconds, " +
            "and every radius change after that is pure float maths over that array with a " +
            "bounding-box pre-filter - no engine calls at all. That is why the radius can be held " +
            "down and watched live on a 10,000-entity scene. The result is capped at 2000 " +
            "entities as a backstop against a typo'd radius selecting a whole map.\n\n" +
            "Switching Isolate off while a scene is isolated restores it first, rather than " +
            "stranding a hidden scene with no key left to bring it back.\n\n" +
            "CTRL+ALT+0, or CTRL+SHIFT+NUMPAD 0 - CENTRE ALL OPEN PANELS. Every toolkit panel on " +
            "screen jumps to the middle of the screen, stacked, and that position is saved exactly " +
            "as if you had dragged it there. For the day a panel ends up off the edge with its " +
            "drag handle out of reach. Panels that were closed at the time keep their positions. " +
            "Works whether or not a panel has focus; not switchable. Ctrl+Shift+0 on the number " +
            "row is accepted too, but Windows reserves that combo for input-language switching " +
            "and swallows it on a default install - hence the two alternatives. Ctrl+0 on its own " +
            "is still the panel-SIZE reset.\n\n" +
            "NOT PRESENT, DELIBERATELY: the F5-F9 panel keys. Letting you disable the key that " +
            "opens a panel, from inside that panel, is a way to lock yourself out. tool_toggles.txt " +
            "switches whole tools off instead, before startup.\n\n" +
            "NATIVE CURSOR (not a key, but it lives on the same flyout). Every toolkit panel is a " +
            "Gauntlet layer, and by default a Gauntlet layer asks the engine for a visible mouse - " +
            "which in the editor means the engine swaps its own oversized game cursor in over the " +
            "normal Windows arrow the moment any panel opens. With this ON (the default) panels " +
            "open without that request, so the editor keeps its Windows cursor; clicks, hover, " +
            "focus and the wheel are routed exactly as before, because none of that depends on the " +
            "visibility flag. Switching it applies to every open panel immediately. If the cursor " +
            "ever disappears with a panel open, switch it OFF to get the old engine cursor back.";

        // FUTURE IDEAS now live in docs/ROADMAP.md in the source repo - the numeric transform
        // modal that used to be sketched here has since been BUILT (see NumericTransformDoc
        // below and PrefabSwapperTool.Core.NumericTransform). Still pending from the original
        // list: CYCLE THROUGH OVERLAPPING ENTITIES - press repeatedly to step through entities
        // stacked at the same position; LiveSceneChecks.CheckDuplicates already finds the
        // clusters and SelectInEditor can select one at a time, so the pieces exist.

        private const string NumericTransformDoc =
            "DRAG THE GIZMO, THEN TYPE A NUMBER. Move or rotate something the normal way - during " +
            "the drag or within 30 seconds of releasing, type a digit and a numeric panel takes " +
            "over. Enter commits, Esc puts everything back to where it was BEFORE the drag. " +
            "Ctrl+Shift+T opens the same panel cold, without a drag. Switchable from F9 -> " +
            "Shortcuts.\n\n" +
            "ONE AXIS AT A TIME, MEASURED FROM WHERE THE DRAG STARTED. A typed value replaces only " +
            "the component on the chosen axis; whatever the drag did on the other axes is kept " +
            "(so a diagonal G-drag plus a typed X value keeps its Y offset). The reference point " +
            "stays the pre-drag position, so you never see a negative number until you have gone " +
            "back past where you began. Until you type, the entities hold the full drag; " +
            "right-click returns to the full drag, not to zero; Esc returns to pre-drag.\n\n" +
            "WAIT FOR THE READOUT. The watcher that detects a drag runs at a low rate on purpose " +
            "(it would otherwise cost frames on a busy scene), so the numeric readout appears a " +
            "moment after a shift-click copy. A digit typed before it appears goes to the editor " +
            "and usually deselects - wait for the readout, then type.\n\n" +
            "The editor's own Alt+W / Alt+L switch the GIZMO between world and local; the panel's " +
            "Local toggle (L, or the axes button) is separate and is what gives an exact offset " +
            "along a rotated entity's own axis - the one thing eyeballing the red readout cannot.\n\n" +
            "CONTROLS: R/G switch rotate/move if the detected mode is wrong; X/Z switch axis and C " +
            "selects the Y axis (Z/X/C mirrors the editor's own rotate keys); L toggles World vs " +
            "Local axes (Local = the first captured entity's own basis); the range button cycles " +
            "the move slider between 5 and 500 units - typed values may always exceed the slider; " +
            "Ctrl+V pastes the first number found in the clipboard. Rotation accepts up to +/-360.\n\n" +
            "Typing outside the takeover window leaves digits alone; left-clicking the readout " +
            "dismisses it. Selections over 64 entities are skipped. Clicking a DIFFERENT entity " +
            "while the panel is open commits the pending value and closes - a box-selection " +
            "commits once, when the mouse is released against the finished selection.\n\n" +
            "WHY DESELECTION DOES NOT BREAK IT: the entities are captured as references when the " +
            "panel opens, so the editor can drop the selection (typing usually deselects) and the " +
            "transform still lands on the right things.\n\n" +
            "SHIFT-DRAG (DUPLICATE). A shift-drag copies and moves in one gesture; typing right " +
            "after acts on the COPIES, from where they started. Shift+R replays COPY AND MOVE - " +
            "replaying only the move would silently drop the copy, which is the point of the " +
            "gesture - and the new copies become the selection so pressing again steps along. " +
            "Copies are faithful (per-mesh materials and colours carried over); an entity with no " +
            "prefab name behind it, or a copy whose meshes don't line up with its source, is " +
            "reported rather than faked.\n\n" +
            "(How the takeover detects drags without any editor API, and the design history: " +
            "Technical Docs on F9, 'Numeric transform'.)";

        // =================================================================================
        // TECHNICAL DOCS - engineering history and evidence, migrated out of the User Guide
        // (2026-08-24). The full day-by-day record is docs/CHANGELOG.md in the source repo;
        // these topics keep the load-bearing diagnoses available inside the editor.
        // =================================================================================
        private void BuildTechnicalTopics()
        {
            AddTopic("About these notes", TechAboutDoc);
            AddTopic("Selection timing - why clicks used to wipe selections", TechSelectionDoc);
            AddTopic("Color mechanisms - the 2026-08-19 discoveries", TechColorDoc);
            AddTopic("Entity-wide color - why blank means reset", TechEntityColorDoc);
            AddTopic("Backups - consolidation history", TechBackupsDoc);
            AddTopic("Out-of-bounds check - the (x,z) bug", TechBoundsDoc);
            AddTopic("Isolate - why the binding is Shift+O", TechIsolateDoc);
            AddTopic("F6 fixes - mirror origin, gizmo sync, per-scene history", TechF6Doc);
            AddTopic("Numeric transform - watcher internals & the two-axis saga", TechNumericDoc);
            AddTopic("Delete Physics - five rounds and what the scene file proved", TechPhysicsDoc);
            AddTopic("Live-reference swaps - CopyFrom's tree behaviour", TechLiveRefDoc);
            AddTopic("Undo trust - UIDs, clones and position disambiguation", TechUndoDoc);
            AddTopic("Weighted rules - how the roll granularity evolved", TechWeightedDoc);
            AddTopic("Runtime-created entities - stale editor state", TechStaleDoc);
            AddTopic("Misc: Culture Editor lag, palette rows, Flora API limits", TechMiscDoc);
        }

        private const string TechAboutDoc =
            "These are the engineering notes behind the User Guide: confirmed bugs, how they were " +
            "diagnosed, and the engine limitations that shaped the tools' designs. Nothing here is " +
            "needed to USE the toolkit - it exists so that when something looks wrong, the history " +
            "of what already looked wrong (and why) is one click away.\n\n" +
            "The complete dated record lives in docs/CHANGELOG.md in the source repository; " +
            "docs/KNOWN-ISSUES.md tracks what is still open.";

        private const string TechSelectionDoc =
            "WHY SELECT-IN-EDITOR LOOKED BROKEN FOR A WHILE (fixed 2026-08-21). Selecting does not " +
            "happen at the moment you click. A click on one of the toolkit's panels also reaches the " +
            "editor's own click-to-select handling underneath, which then resets the selection to " +
            "'whatever the mouse is over' - the panel, i.e. nothing. The old code selected inline and " +
            "was wiped in the same frame: the reported count was correct while the editor showed " +
            "nothing selected. Selection is now queued and applied two ticks later, once the editor " +
            "has finished with that click. Two ticks, not one, because the mouse RELEASE arrives on " +
            "the following frame and can clear a selection just as well as the press did.\n\n" +
            "RELATED (2026-08-23): every panel's outside-click handler used to null the SCREEN-WIDE " +
            "focused widget, which stole focus from the editor's own resource browser at the exact " +
            "moment a drag-placement started - killing the native drag ghost whenever any toolkit " +
            "panel was open. All panels now drop focus only when the focused widget belongs to that " +
            "panel (PanelFocus.DropFocusOnOutsideClick).";

        private const string TechColorDoc =
            "THE 2026-08-19 COLOR DISCOVERIES, in order:\n\n" +
            "1. The per-rule color factor used to go through MetaMesh.SetFactor1 - a property " +
            "covering a whole MetaMesh component. A normal building has exactly ONE MetaMesh " +
            "covering every part across every LOD tier, so every rule matching anything on that " +
            "entity fought over the same single color value; whichever rule processed last silently " +
            "won. That is why several per-material colors used to collapse into one uniform tint. " +
            "Fixed by switching to Mesh.Color - the per-part granularity Fix Color Mismatches had " +
            "already proven live.\n\n" +
            "2. GameEntity.SetFactorColor does NOT behave like an independent tint layered over " +
            "Mesh.Color - confirmed via tool.log, GetFactorColor() read back the exact color just " +
            "written to individual meshes in the same Apply. It writes ACROSS the entity's own " +
            "mesh-color state. Running the entity-wide clear AFTER the per-mesh loop therefore " +
            "stomped every correctly-applied color back to white ('I applied #ffd580 but it's " +
            "white') - it now runs FIRST.\n\n" +
            "3. Entities colored by the pre-fix Factor1 path kept a permanent stale Factor1 tint " +
            "multiplying into the render on top of correct Mesh.Color values. Apply now clears " +
            "stale Factor1 on entities it touches (never scene-wide - unrelated native systems use " +
            "Factor1 too).\n\n" +
            "4. Mesh.Color was confirmed as a real live per-INDIVIDUAL-slot tint via a test scene " +
            "(european_city_house_d3) where exactly one LOD5 submesh carried a non-white Color " +
            "while Factor1 and FactorColor were both white. Continuous Recolor and Fix Color " +
            "Mismatches use this same mechanism.\n\n" +
            "LATER (2026-08-23): unparseable typed colors (e.g. '#ffwe193') used to be skipped " +
            "silently, indistinguishable from 'broken' - they now warn on screen and in the log. " +
            "MST undo used to restore an entity-wide entry via SetFactorColor(old), which (per " +
            "discovery 2) flattened per-layer colors the batch never touched; entries now snapshot " +
            "every mesh color before the forward SetFactorColor and undo restores them individually.";

        private const string TechEntityColorDoc =
            "WHY BLANK MEANS RESET on the entity-wide color box: GameEntity.SetFactorColor is a " +
            "persistent property that never resets itself between Apply calls. Treating blank as " +
            "'don't touch' (correct for per-rule boxes, where each row is independent) meant a " +
            "previously-applied saturated entity-wide tint silently kept multiplying into every " +
            "per-rule/per-LOD color applied afterwards - the new colors WERE applying correctly and " +
            "just looked wrong under the leftover tint. The box is a single always-visible field " +
            "representing current intent, so blank unambiguously means 'no entity-wide tint', same " +
            "as clicking Clear (both resolve to white, the multiplicative identity).";

        private const string TechBackupsDoc =
            "HISTORY. Backup settings used to be compile-time constants - the direct reason backups " +
            "once sat switched off for three days with nothing saying so; they persist to " +
            "backup_settings.json now. F9 used to open Flora Swap's placeholder before the Backups " +
            "panel took the key.\n\n" +
            "CONSOLIDATION (v0.7): Prefab Swapper and Prefab Creator used to run their own separate " +
            "copies of the backup system - three independent timers copying the same three files " +
            "into three folders, with only Material Swap's copy honouring the F9 settings. " +
            "Everything routes through the one system now; the old PrefabSwapperTool\\Backups and " +
            "PrefabCreatorTool\\Backups folders are frozen history, no longer written.\n\n" +
            "The 2026-08-17..19 crash-on-save investigation briefly blamed background File.Copy " +
            "racing the editor's save; the engine's own logs disproved it - builds were deploying " +
            "module files while the editor was open, and the editor read a half-written file. " +
            "Backups had been off the whole time. Hence the deploy-on-exit discipline.";

        private const string TechBoundsDoc =
            "The out-of-bounds check computes a 2D convex hull from border_soft marker positions. " +
            "Bannerlord is Z-UP, so the ground plane is X-Y - an earlier version tested (x,z), " +
            "mixing a horizontal axis with HEIGHT, and therefore flagged the entire scene as " +
            "outside. It now also refuses to report 'everything is outside' (never a real result) " +
            "and logs its reasoning as '[Boundary]' in tool.log.";

        private const string TechIsolateDoc =
            "Isolate was Ctrl+Shift+I, then briefly Ctrl+Shift+O, both on 2026-08-22: the editor " +
            "places on bare I even with modifiers held (matching the ground's rotation), and a " +
            "native Ctrl+Shift+O HID the selection - both collided. Shift+O with Ctrl NOT held " +
            "avoids both natives, which is why the binding checks that Ctrl is up.\n\n" +
            "THE RESTORE RECORD (2026-10-03). The first version kept the record as a dictionary of " +
            "live entity pointers and its own comment claimed isolation never reached disk. Both " +
            "were wrong: scene.xscene stores visible=\"false\", and the record was dropped on every " +
            "scene-screen teardown - which includes ENTERING TEST MODE (the same HandleDeactivate " +
            "hook as a real close; see the test-mode crash notes). Isolate + save + reload, or " +
            "Isolate + test mode, therefore left the scene hidden with Shift+O reporting nothing " +
            "to restore. Fix: each hidden entity carries the tag 'mst_isolated' (saved with the " +
            "scene, untouched by test mode); Restore un-hides by tag as well as by record; " +
            "teardowns and scene opens arm a deferred tag scan (3-5 s after the scene is live, " +
            "never on the first resumed frame - that whole-scene native walk is what crashed the " +
            "editor on 2026-08-31) which rebuilds the record and warns. The scene file's write " +
            "time is watched while isolated so a save-while-isolated warns once per save.";

        private const string TechF6Doc =
            "MIRROR WITH 'EXACT ORIGIN POINT' used to reuse the source's origin unchanged, so a " +
            "mirrored prefab ended up with literally the same origin as its source; it reflects the " +
            "anchor's origin across the mirror plane now.\n\n" +
            "TRIAD/GIZMO SYNC: SetGlobalFrame moves an entity but the editor keeps its own cached " +
            "frame for the transform gizmo. For an entity just created, that cache is world origin - " +
            "the 'transform panel is right but the gizmo is at the map corner, and dragging it " +
            "teleports the entity' symptom. Every toolkit move now calls UpdateTriadFrameForEditor " +
            "(+ all-children) afterwards; this affected every F6 placement tool.\n\n" +
            "PER-SCENE HISTORY: the swap log is one shared file across all scenes. A batch from a " +
            "different scene, undone here, re-finds entities by NAME AND POSITION - and the fief_*/" +
            "material_test_* scenes are copies of each other with entities at near-identical " +
            "coordinates under near-identical names (and cloned mst_uid tags), so a cross-scene " +
            "undo could silently swap the wrong thing. Cross-scene batches are refused outright; " +
            "refusals log as '[SceneGuard]'.\n\n" +
            "MIRROR'S RESIDUAL LIMITATION: a hand-built prefab-LESS piece mirrored an ODD number of " +
            "times renders inside-out until the scene is saved and reloaded - the saved data is " +
            "correct; the live winding cache is not reachable from managed code.";

        private const string TechNumericDoc =
            "HOW THE TAKEOVER KNOWS ANYTHING: the editor exposes no manipulation state at all - no " +
            "rotate flag, no gizmo axis. The ManipulationWatcher polls the selected entities' frames " +
            "and looks for the SHAPE of an operation: change followed by stillness. That covers " +
            "gizmo drags, G/Z modal drags and typed transforms alike, and yields the frames from " +
            "BEFORE the drag - which is what lets a typed number replace the drag. Rotation is " +
            "checked before movement (rotating a group also moves each origin); the settle never " +
            "fires while the mouse button is held (a pause mid-drag used to split one drag into two " +
            "recorded operations); duplicates are recognised by the selection switching to a NEW " +
            "entity while the original sits untouched.\n\n" +
            "THE TWO-AXIS SAGA (2026-08-23, four iterations): the original behaviour - typed value " +
            "replaces the whole drag with a clean single-axis move - turned out to be what was " +
            "wanted all along; the intermediate 'preserve the perpendicular residual' and " +
            "'per-axis values' designs both read as bugs in practice. Final semantics: nothing " +
            "typed = hold the full drag (no jump on open or right-click reset); a typed value = " +
            "pure single-axis move from the pre-drag position, other axes reset; Esc = cancel to " +
            "pre-drag.\n\n" +
            "Shift+R replays through the same watcher; its early failures were, in order: clones " +
            "of prefab-less sources, selection not chained to the new copies, click-jitter " +
            "micro-moves overwriting the recipe (0.5m filter now), and typing Shift+R before the " +
            "settle window closed while Shift was still held (ForceSettlePending closes that race).";

        private const string TechPhysicsDoc =
            "DELETE PHYSICS took five rounds because each layer of the engine hides the previous " +
            "one:\n\n" +
            "1. RemovePhysics + skip-if-BodyFlags-None: the skip was wrong (collision shapes on " +
            "children can report None while solid), and RemovePhysics alone left collision.\n" +
            "2. + SetBodyFlags(None) with readback: readback showed all flags cleared - still " +
            "solid. Flags are not the registered body.\n" +
            "3. + EntityFlags.PhysicsDisabled (the lever that provably makes CopyFrom clones " +
            "unclickable): still solid. The flag is honoured at body CREATION, not retroactively.\n" +
            "4. Broken-out CopyFrom pieces (a plain Instantiate saves as <game_entity prefab=\"X\"> " +
            "and the LOADER rebuilds physics from the prefab definition every load - nothing " +
            "per-entity persists on a prefab reference): the pieces now serialize with explicit " +
            "components... and the serializer STILL wrote <physics shape=\"bo_...\"/> from entity " +
            "metadata no runtime API clears.\n" +
            "5. File surgery: the pieces are tagged pile_nophys at generation; the stripper removes " +
            "their <physics> nodes from the saved scene.xscene (timestamped .bak first). An editor " +
            "save from a stale session re-emits the nodes - confirmed live by matching timestamps - " +
            "so the stripper also re-runs automatically ~2s after every detected save. Collision " +
            "state in a LIVE session only updates on load; judge results after a reload.\n\n" +
            "Every claim above was verified against the saved xscene, not inferred.";

        private const string TechLiveRefDoc =
            "GameEntity.CopyFrom copies the ROOT entity and its prefab-definitional children, but " +
            "NOT children attached at runtime (re-parented survivors of earlier swaps, hand-added " +
            "decorations) - on a worked-on composite that is all the visible geometry, so early " +
            "live-reference swaps produced empty invisible copies. The swap now clones missing " +
            "children explicitly and recursively, comparing matched source/copy pairs level by " +
            "level (grandchildren CopyFrom drops are cloned too).\n\n" +
            "The name-swap 'preserve extras' pass then DOUBLED every part on like-for-like variant " +
            "swaps: clones carry auto-suffixed unique names, so the supersede-by-name check never " +
            "matched. Live-reference mode now carries NOTHING from the old entity - an exact copy " +
            "of the reference is the mode's entire meaning. Name-mode swaps distinguish the old " +
            "prefab's DEFINITIONAL children (probed from a throwaway instance of the old prefab - " +
            "they die with it) from genuinely hand-added extras (preserved) - the fix for " +
            "cross-culture swaps keeping the old culture's merlons.\n\n" +
            "CopyFromPrefab (a different API) DAMAGES live source entities' children and must " +
            "never be used against scene entities - confirmed live 2026-08-23, reverted same day.\n\n" +
            "CopyFrom products are flagged DontSaveToScene | NonModifiableFromEditor | " +
            "WaitUntilReady | PhysicsDisabled at birth - unselectable and unsaveable until cleared. " +
            "Every clone path clears the tree recursively; a scene-open sweep repairs copies made " +
            "before the recursive clear existed (clean root + flagged descendants is the signature " +
            "that makes a whole-scene sweep safe). Click-selection registration of fresh copies " +
            "remains imperfect until a save rebuilds the editor's registry - the hand-rolled clone " +
            "in docs/ROADMAP.md is the tracked real fix.";

        private const string TechUndoDoc =
            "RevertManager only auto-applies a change-log entry it can TRUST to the right entity:\n\n" +
            "- A UID tag match within position tolerance is trusted. Cloning duplicates tags " +
            "(including UIDs), and multiple tag-sharers used to demote the whole batch to " +
            "confirmation - the 'undo is fully broken' report was a recolor-then-clone-copies " +
            "workflow. Position now disambiguates: exactly ONE tag-sharer within 10cm of the " +
            "logged position is the original (recolors move nothing; clones were dragged away).\n" +
            "- Entries without UIDs were NEVER auto-trusted - Continuous Recolor entries carried " +
            "none, so CR batches were always un-undoable until 2026-08-23. CR stamps UIDs now, and " +
            "a no-UID entry is trusted when exactly one same-named entity sits within 10cm.\n" +
            "- Cross-scene batches always demote (see the F6 topic).\n\n" +
            "Prefab-swap undo runs the swap machinery backwards, which used to honour the panel's " +
            "global toggles: ADD mode made 'undo' place the restored prefab NEXT TO the thing it " +
            "should remove; 1x Scale stripped a scaled original on restore; the Z-rot/scale extras " +
            "would distort it the same way. Undo/redo force neutral toggles for their duration and " +
            "restore at the recorded PRE-swap frame (the swapped entity's current frame carries " +
            "whatever extras the forward swap applied).";

        private const string TechWeightedDoc =
            "A weighted To-spec ('matA:4, matB:1') resolves deterministically from a stable hash - " +
            "never string.GetHashCode(), which .NET randomises per process. The seed's granularity " +
            "took four iterations (2026-08-23):\n\n" +
            "per-mesh -> LOD tiers of one part rolled apart (material popped with distance);\n" +
            "per-part-name -> merged simplified LOD meshes pair with nothing and still rolled " +
            "alone;\n" +
            "per-target -> filter/scene modes hand children over as their own targets, patchwork " +
            "again;\n" +
            "per TOPMOST PARENT (final) -> everything under one placed prefab shares one roll, " +
            "cached across the Apply; variety lives BETWEEN instances, which is what weights were " +
            "for.\n\n" +
            "Duplicate From-rows used to be silently collapsed to the first (.First() in the rule " +
            "map); they are alternatives now, one row picked per top-level prefab, each row keeping " +
            "its own color factor. Rules also match through the '(copy)' suffix the engine gives " +
            "override-clone materials - a mesh reporting 'x(copy)' matches a rule written against " +
            "'x'; half-applied-looking swaps were the plain-named half matching while the cloned " +
            "half never could.";

        private const string TechStaleDoc =
            "Runtime-created entities (pile pieces, clones) hold STALE EDITOR STATE until a save + " +
            "reload rebuilds the editor's caches: visibility toggles don't take, physics changes " +
            "don't show, click-selection may miss. UpdateSceneTree is nudged where it helps; the " +
            "full heal is save + reload. Same underlying registry limitation as the CopyFrom ghost " +
            "story (see Live-reference swaps) - the hand-rolled clone in docs/ROADMAP.md is the " +
            "tracked real fix.\n\n" +
            "Related engine truths collected along the way: entity SCALE is encoded as the frame's " +
            "rotation basis lengths (there is no scale setter; 'scale can't carry over' was half " +
            "wrong - it silently ALWAYS carried in the frame, which is why 1x/Inherit is an " +
            "explicit toggle now); a freshly Instantiated entity's bounding box can still be dirty " +
            "on the next call (the rglEntity.h:2068 assert - RecomputeBoundingBox before " +
            "measuring); Instantiate auto-suffixes names for uniqueness, so name matching strips " +
            "trailing _NN throughout.";

        private const string TechMiscDoc =
            "CULTURE EDITOR LAG: a culture's Common list runs 60-145+ entries; rendering every " +
            "culture's list as live text at once is what made the panel lag - lists are collapsed " +
            "to a count + Edit button per row now.\n\n" +
            "RECOLOR PALETTES: category rows used to share ONE color for every armed category; " +
            "per-row colors replaced that, with Fill Color as a bulk-fill preserving the old " +
            "workflow.\n\n" +
            "FLORA LAYERS: the live scripting API exposes exactly two read-only flora counters " +
            "(GetFloraInstanceCount, GetFloraRendererTextureUsage) and nothing else - no enumerate, " +
            "no read-where-painted, no write. Confirmed by reflecting the shipped engine DLL. " +
            "Editing terrain flora layers from a mod is an engine-side impossibility, which is why " +
            "Flora Swap only ever handled placed flora PROPS and is retired.\n\n" +
            "CCMODULE_SP STARTUP: its wEditor bin was missing (DLL popups - fixed by populating " +
            "it); the RGL texture warnings are unfixable from outside - the sheets exist in the " +
            "module's pack0.tpac but the editor doesn't mount them and ignores loose GUI PNG paths " +
            "(extraction was tried; correct layout, still not consulted). An external watchdog " +
            "dismisses those popups at launch instead.";

        private void AddTopic(string title, string body) => _topics.Add(new DocumentationTopicVM(title, body));

        [DataSourceProperty]
        public MBBindingList<DocumentationTopicVM> Topics
        {
            get => _topics;
            set { if (value != _topics) { _topics = value; OnPropertyChangedWithValue(value, nameof(Topics)); } }
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();

        private const string OverviewDoc =
            "Bulk-swaps materials (and colors) on entities in the currently open editor scene, live.\n\n" +
            "WHY IT EXISTS. Overriding materials by hand means doing it once per LOD layer, and " +
            "copy-pasting an override block between layers goes wrong silently: higher LODs have " +
            "fewer slots, not always in the same order, so one missing slot shifts every slot after " +
            "it (a dirt decal pasted into a wall-decal slot turns transparent). A rule here writes " +
            "every tier of every selected entity in one pass by material NAME, so the slot order " +
            "never matters.\n\n" +
            "Open/close with F8. Basic workflow: pick a Selection mode (who gets touched), add one or " +
            "more Rules (which material becomes which), then Dry Run to preview or Apply to commit.\n\n" +
            "Every Apply is backed up automatically first (see 'Backups - how they work') and logged " +
            "to Batch History, so nothing here is truly one-way even without a manual undo.";

        private const string RulesDoc =
            "A rule swaps one material for another by name.\n\n" +
            "FROM: the exact material name currently on the entity's mesh (case-insensitive).\n" +
            "TO: the material name to switch it to. Must exist in the game's material list.\n\n" +
            "Only meshes whose CURRENT material matches FROM are touched - everything else on " +
            "the entity is left alone. Add multiple rules to cover multiple materials in one pass.\n\n" +
            "+ Add Rule adds a blank row. Delete Blank Rules removes any row with nothing usable in it " +
            "(no TO material). Invert Rules swaps every rule's FROM/TO in place, useful for building the " +
            "reverse of a swap you just did without retyping everything.\n\n" +
            "WEIGHTED / MULTI-OUTPUT RULES. The TO box accepts a comma-separated list with a weight " +
            "after a colon: 'stone_wall_11:3, stone_wall_14:10'. Each prefab rolls once against the " +
            "weights, so variance shows up ACROSS prefabs and never inside one - half a house in " +
            "each material is exactly what this avoids. Listing the same FROM material on several " +
            "rows does the same thing (one row is picked per prefab), which is how imported presets " +
            "with many overlapping rules end up random.\n\n" +
            "DEDUPE ROWS removes repeated FROM rows (first one wins); DEDUPE MULTI-OUTPUT trims each " +
            "weighted TO list to its first material. Together they turn a noisy imported preset " +
            "into a deterministic one: undo, dedupe both, apply again.";

        private const string ColorFactorDoc =
            "There are TWO separate color mechanisms - don't mix them up.\n\n" +
            "1) Per-rule color factor (right-most box on a rule row): tints ONLY the individual " +
            "mesh part whose material matched that rule (Mesh.Color) - a wall rule and a roof " +
            "rule on the same building each tint just their own part, independently, no conflict " +
            "even though they're on the same entity.\n\n" +
            "2) Entity-wide color factor (its own highlighted row below the rule list): tints the " +
            "WHOLE entity, every part, regardless of which materials matched any rule " +
            "(GameEntity.SetFactorColor). Use this when the color is applied once across the " +
            "entire prop rather than per-material.\n\n" +
            "A typed color that does not parse (a typo like '#ffwe193') is never silently skipped - " +
            "the status line and an editor warning name it, because a skipped tint looks identical " +
            "to a broken feature.\n\n" +
            "Both boxes accept hex in #RRGGBB or #RRGGBBAA form (alpha optional, defaults to FF).\n\n" +
            "Clearing the ENTITY-WIDE box back to no tint: just leave it empty and Apply - blank " +
            "there means 'no entity-wide tint,' same as clicking the Clear button next to it (both " +
            "resolve to white, #FFFFFFFF, the neutral/no-tint value). It's a single always-visible " +
            "field representing your current intent, so blank is unambiguous.\n\n" +
            "A PER-RULE color factor box works differently, on purpose: leaving one blank means " +
            "'no color for this specific rule' without affecting any other rule's color, since " +
            "there can be several rule rows each with their own box. The same is true of Continuous " +
            "Recolor's per-category color boxes. The entity-wide box is different because its value " +
            "persists between Applies - blank must mean 'reset', or a leftover tint would keep " +
            "multiplying into everything applied afterwards. (Why: Technical Docs, 'Entity-wide " +
            "color'.)\n\n" +
            "To set an explicit color on either kind of box, type #RRGGBB or #RRGGBBAA (alpha " +
            "optional, defaults to FF). The words #, clear, white, none, or a single space also all " +
            "resolve to white if you want to type an explicit reset rather than deleting the text.\n\n" +
            "Continuous Recolor and LOD Mismatch's 'Fix Color Mismatches' use this exact same " +
            "Mesh.Color mechanism, not a separate third one - see the Continuous Recolor topic.";

        private const string SelectionDoc =
            "Selection modes decide which entities the rules are checked against:\n" +
            "  Manual - whatever you have selected in the editor viewport right now.\n" +
            "  Filtered - every entity whose name or prefab matches the filter text box.\n" +
            "  Whole Scene - every entity in the scene, no filtering.\n\n" +
            "Use Preview or Dry Run first to see how many entities/slots would be touched " +
            "before committing with Apply.\n\n" +
            "Get Input Rules from Selection reads whatever's currently selected in the editor " +
            "(independent of the Manual/Filtered/WholeScene mode above, which only governs Apply's " +
            "target set) and adds one new blank-TO rule row per distinct material found on those " +
            "entities, skipping anything that already has a rule - a fast way to start a rule set " +
            "from 'here's what's actually on this building' instead of guessing material names.\n\n" +
            "Dump Slot Info is a diagnostic: logs exact per-slot data (mesh index, node name, current " +
            "material, LOD tier) for whatever's selected to tool.log. Mainly useful if a swap isn't " +
            "landing where you expect and you want to see exactly what slots exist on that entity.";

        private const string FillInOverridesDoc =
            "Two related 'learn from an existing entity instead of typing rules by hand' tools.\n\n" +
            "FILL IN OVERRIDES (A -> B, by matching material): for copying colors between two " +
            "entities that already use the same materials. Select a reference entity (the one with " +
            "the colors you want) and click Set Reference (A). Then select a DIFFERENT entity (the " +
            "one that needs the colors) and click 'Fill In Overrides -> Selected (B)'. This finds " +
            "every mesh slot on B (and its children) whose CURRENT MATERIAL matches a material found " +
            "somewhere on A, and sets that slot's color to match A's color for that material. Only " +
            "the color changes - B's material is never touched, since a match means it was already " +
            "the same material as A. A confirmation dialog shows how many slots would change before " +
            "anything is applied, and a backup is taken first, same as every other Apply-style action.\n\n" +
            "FILL RULES FROM ENTITY (blank rules only): an alternate mode of Get Input Rules from " +
            "Selection above, not a separate tool. It only does anything once you already have BLANK " +
            "rules queued - rows with a FromMaterial but no ToMaterial, exactly what Get Input Rules " +
            "from Selection produces. With those blank rules still in the list, select a prefab (or " +
            "several) and click Fill Rules From Entity. It walks that selection's own hierarchy - " +
            "the prefab itself plus every child, recursively - and groups entities by exact Name+Tags " +
            "(duplicate copies of the same named part). If a group contains both a blank rule's " +
            "FromMaterial AND exactly one other, different material, that other material fills in the " +
            "rule's ToMaterial. This is for a prefab where some copies of a repeated part were already " +
            "manually swapped to a new material and others weren't yet - the already-swapped copies " +
            "become the answer key for the rest, instead of you having to type the new material name " +
            "yourself. A group with more than one candidate 'other' material is reported as ambiguous " +
            "and left blank rather than guessed at. Like the Merge preset mode, this only ever fills " +
            "an empty ToMaterial - it never adds new rows and never overwrites a row that already has " +
            "a target.";

        private const string LodMismatchDoc =
            "Check LOD Mismatches scans your selection for slots where the full-detail mesh and its " +
            "LOD5 counterpart disagree - either a different MATERIAL, or a different Mesh.Color tint " +
            "(a per-individual-mesh color, separate from both color factor mechanisms). This shows up " +
            "in-game as a visible 'pop' when the LOD switches at distance - an edit (yours or a manual " +
            "one) that touched one detail tier and not the other.\n\n" +
            "Results open in a preview list prefixed [MATERIAL] or [COLOR] per mismatch found.\n\n" +
            "TWO FIX BUTTONS. Fix Color Mismatches syncs each LOD slot's color to its LOD0 " +
            "counterpart; Fix Material Mismatches does the same for the material. LOD0 is what " +
            "you see up close, so it is treated as the correct value and copied outward. Both are " +
            "backed up first and logged to Batch History.\n\n" +
            "THEY WORK ABOUT 90% OF THE TIME. The copy is slot-by-slot from LOD0, and higher LODs " +
            "sometimes have FEWER slots or slots in a different order - a lost slot offsets every " +
            "slot after it, which is the exact way manual copy-paste between LOD layers goes " +
            "wrong. When a fix leaves something still broken: Revert to Normal on that entity, " +
            "then re-apply your rules, which writes every tier in one pass.";

        private const string CheckboxesDoc =
            "Three independent toggles that change what Apply does alongside the material swap:\n\n" +
            "Tag Changed - adds a small tag to every entity actually touched by Apply, so you can " +
            "find them later in the editor by searching that tag.\n\n" +
            "Rename Changed - appends a marker to the entity's name when touched, similar idea to Tag " +
            "Changed but visible directly in the name instead of needing a tag search.\n\n" +
            "Track by UID - assigns each touched entity a stable unique ID (stored as a tag) instead " +
            "of relying on name+position to identify it later. This is what lets Revert to Normal and " +
            "Undo find the RIGHT entity even if it's been moved or renamed since the swap - without " +
            "it, tracking falls back to name/position matching, which can misfire if multiple " +
            "identical entities sit near each other. Simplify for Export (see the next topic) " +
            "downgrades UID-tracked history back to position-only tracking, if you ever want to " +
            "stop carrying the UID tags around.\n\n" +
            "THE TAG IS WRITTEN THE FIRST TIME AN ENTITY IS CHANGED and the history is keyed to it, " +
            "so: delete the tag by hand and that entity's undo and redo are orphaned (Revert to " +
            "Normal still works - it needs no history); shift-drag COPIES of a tagged entity carry " +
            "the SAME UID, which is then not unique, and their undo misfires until they are " +
            "changed again and re-tagged; and because the log lives on disk, history survives " +
            "saving and reloading the scene.";

        private const string RevertUndoSolidifyDoc =
            "Revert to Normal clears material/color changes back to an entity's default appearance " +
            "for whatever's selected - independent of Batch History, this doesn't need a prior swap " +
            "to have been logged. It works on entities this session never touched, including " +
            "overrides saved in earlier sessions, because it reads the toolkit's reference of " +
            "native materials and colors per prefab rather than any history. That makes it the " +
            "reliable reset when undo gets confused - and it does occasionally: a color factor " +
            "re-applied to something just colored sometimes does nothing, and an undo now and " +
            "then leaves a color factor wrong. Revert, then re-apply.\n\n" +
            "Undo Last Batch reverts the most recent Apply's changes specifically (from Batch " +
            "History's log), even if you've since done other things in the editor - matches by UID " +
            "when Track by UID was on for that batch, otherwise by name+position.\n\n" +
            "Simplify for Export is a whole-scene cleanup: downgrades every UID-tracked history " +
            "entry to position-only tracking and strips the UID tags from the scene's entities - " +
            "which ends UID-based undo for them, so treat it as 'I am done iterating'. Use it once " +
            "you're done iterating and don't want tracking tags left lying around in the saved scene. " +
            "It never touches the original change log - always writes a new one - so nothing is lost " +
            "by running it.";

        private const string PresetsDoc =
            "Presets save the current rule list (including color factors) under a name for reuse.\n\n" +
            "Saving: type a Name and optional comma-separated Tags, then Save Preset. Culture tags " +
            "are inferred automatically from the FROM materials (see the Culture Editor topic for how) " +
            "and merged with whatever you typed.\n\n" +
            "Loading: Load Preset replaces your current rules with the named preset's rules. Browse " +
            "Presets opens a searchable/filterable list instead of typing the exact name - filter by " +
            "name, tag, or a material either side of a rule uses.\n\n" +
            "In the Browse Presets window, Overwrite mode replaces your current rules on choose; " +
            "Add mode appends the preset's rules to what you already have, skipping any rule that's " +
            "an exact duplicate (same from/to/color) of one already present; Merge combines by FROM " +
            "material. Invert flips FROM/TO as the preset loads (independent of Invert Rules on the " +
            "main panel, which flips whatever's already loaded).\n\n" +
            "Presets are plain JSON files under Documents\\Mount and Blade II Bannerlord\\" +
            "MaterialSwapTool\\Presets. Older BSA .txt template files are also readable here for " +
            "compatibility, but can't be deleted from this tool - only JSON presets can. Built-in " +
            "presets ship with the module and can't be deleted either, only personal ones you've saved.";

        private const string PresetHistoryDoc =
            "Every time you Save Preset over an existing name, the OLD version is archived first, " +
            "not overwritten silently - Preset Version History lets you browse and restore any prior " +
            "version of a named preset.\n\n" +
            "Restore loads an old version's rules back as the CURRENT rule set in the main panel. A " +
            "toggle in the history window controls whether restoring also immediately re-saves that " +
            "version as the preset's current one, or just loads it into the editor for you to review/" +
            "adjust before deciding to Save yourself.\n\n" +
            "Restoring does NOT itself create a new history entry - only a genuine edit-and-save does. " +
            "That's deliberate: clicking through old versions to compare them would otherwise flood " +
            "the history list with near-duplicate entries every time.";

        private const string BatchHistoryDoc =
            "Every Apply (and every other logged mutating action across the mod - LOD fixes, " +
            "duplicate deletion, prefab swaps, etc.) is recorded as a batch: what changed, on which " +
            "entities, when. Open it from Material Swap Tool's Batch History button.\n\n" +
            "A screenshot is captured automatically with each batch. Since the game's texture loader " +
            "can't decode PNGs (only its own proprietary format), thumbnails aren't embedded in the " +
            "panel - instead there's a 'View Screenshot' button per entry that opens the saved PNG in " +
            "your system's default image viewer.\n\n" +
            "Only the 30 most recent batches are kept - both the log entries AND their screenshot " +
            "files are pruned together automatically whenever a new batch pushes the count over 30, " +
            "so disk usage doesn't grow forever.\n\n" +
            "EVERY ROW HAS UNDO AND REDO, in any order. Undoing a middle entry leaves the later " +
            "ones intact and still undoable; redo puts that one batch back. Undo Last Batch on the " +
            "main panel is just the newest row's Undo. Both find entities by their UID tag when " +
            "Track by UID was on for that batch - delete the tag and that entity drops out of the " +
            "batch's undo (see 'Rename Changed / Track by UID / Tag Changed').";

        private const string BackupsDoc =
            "Before any destructive action anywhere in this mod (Apply, Simplify for Export, Scene Analyzer's " +
            "fix actions, Prefab Swapper's swaps, etc.), a full backup of the scene's saved files is " +
            "taken automatically - you don't need to remember to do this yourself.\n\n" +
            "Retention is PER SCENE, not global: each scene gets its own backup folder with up to 50 " +
            "backups kept, tiered by age (recent ones kept densely, older ones thinned out) rather " +
            "than a flat 'delete anything past #50' rule. Working across many different scenes won't " +
            "cause one scene's backups to crowd out another's.\n\n" +
            "Backups live under Documents\\Mount and Blade II Bannerlord\\MaterialSwapTool\\Backups, " +
            "one subfolder per scene name. Restoring one is a plain file copy of scene.xscene (and " +
            "the terrain files) back into the scene's SceneObj folder; that has been done and works.\n\n" +
            "NO WARRANTY, SO KEEP YOUR OWN. This copies SAVED files on a timer - it is a safety net, " +
            "not a versioning system. Keep your own backups as well, and rotate the scene name " +
            "every few hours of work (scene_A, scene_B, scene_C): each name gets its own backup " +
            "folder, so a corrupted file never takes its whole history with it.\n\n" +
            "ONE SYSTEM FOR ALL TOOLS: every tool routes through this single backup system - one " +
            "timer, one Backups folder (the path above), one set of F9 settings. (Older " +
            "PrefabSwapperTool\\Backups and PrefabCreatorTool\\Backups folders are frozen history " +
            "from before the consolidation - see Technical Docs.) A backup (and its 'avoid Save' " +
            "warning) is skipped whenever the scene's files on disk are identical to the newest " +
            "existing backup - including before an apply, where the newest backup already IS the " +
            "pre-apply marker. Only a manual Backup Now always copies.";

        private const string FloraSwapDoc =
            "RETIRED - potential future feature. F9 now opens the BACKUPS panel; Flora Swap no longer " +
            "has a hotkey at all. It previously lived on F9 and opened a placeholder instead of the " +
            "real tool. Description below is preserved for when/if it comes back.\n\n" +
            "F9, entirely separate identity from Material Swap Tool (F8) - same underlying material-" +
            "swap mechanism (rules, selection modes, dry run/apply), just branded and hotkeyed for " +
            "flora props specifically, with its own panel so it reads as obviously a flora tool.\n\n" +
            "IMPORTANT - what this does NOT do: it does not edit terrain flora LAYERS (the density/" +
            "species painting that determines what grass/bushes/trees the engine scatters across the " +
            "ground procedurally). That data lives in the terrain system's binary layer format, and " +
            "the live scripting API only exposes two read-only diagnostic counters for it - " +
            "Scene.GetFloraInstanceCount() and Scene.GetFloraRendererTextureUsage() - with no method " +
            "to enumerate individual flora instances, read which layer/species is painted where, or " +
            "write new layer data. Confirmed by reflecting the shipped engine DLL directly: there is " +
            "no GetFloraInstance/GetFloraLayer/SetFloraLayer-shaped API at all, unlike terrain height " +
            "and physics-material data which DO have several read methods. This is a hard engine-side " +
            "limitation, not something this mod chose not to build.\n\n" +
            "What Flora Swap Tool DOES do: swap the MATERIAL on individually-PLACED flora game_entity " +
            "props (a placed tree/bush prefab instance sitting in the scene's entity list, not a " +
            "terrain-layer-scattered instance) - the same live per-mesh material/color mechanism used " +
            "everywhere else in this mod.";

        private const string ContinuousRecolorDoc =
            "A different workflow from Material Swap Tool's rule-based Apply: instead of swapping " +
            "materials by name, Continuous Recolor tints whatever's currently selected based on which " +
            "MATERIAL CATEGORY each mesh slot's material classifies into (see the Category Editor " +
            "topic for how categories work), and stays armed - every new selection you make while " +
            "armed gets recolored immediately, no repeated Apply clicks needed.\n\n" +
            "Toggle one or more categories ON, set a color per row, then either:\n" +
            "  ARM - the continuous paintbrush: every new selection you make while armed recolors " +
            "on the spot. Click Arm/Disarm again to stop.\n" +
            "  APPLY COLORS - a one-shot: recolors the CURRENT selection once, armed or not. Use it " +
            "when you don't want the brush following you around.\n\n" +
            "Uses Mesh.Color - a real per-INDIVIDUAL-mesh-slot tint. Because it's per-slot, a " +
            "building with mixed materials (stone AND timber-frame in the same object) can have " +
            "just its timber-frame slots recolored without touching the stone ones - there is no " +
            "'mixed materials, refuse to recolor' restriction.\n\n" +
            "Edit Categories opens the Category Editor directly from this panel.";

        private const string RecolorPaletteDoc =
            "Each category row has its OWN color box - toggle several categories on, give each a " +
            "different color, and one pass applies all of them together. The 'Fill Color' box at " +
            "the bottom bulk-fills every currently-ON row with one color, for when a single shared " +
            "color is all you need.\n\n" +
            "Palettes let you save/load a whole multi-category color scheme at once. Toggle the " +
            "categories and colors you want, type a name, click Save. Saved palettes appear in the " +
            "list below - clicking one LOADS it, which fully replaces the current arm configuration: " +
            "every category in the saved palette is switched on with its saved color, and every " +
            "category NOT in it is switched off (not a merge with whatever was already toggled).\n\n" +
            "Palettes are plain JSON files under Documents\\Mount and Blade II Bannerlord\\" +
            "MaterialSwapTool\\Palettes. The shipped ones: CC74 Battle and CC76 Battle (from those " +
            "maps), European (drawn from Craglow) and Italian (drawn from Terra Volus).\n\n" +
            "A color with the wrong number of hex digits is refused rather than guessed - copy the " +
            "length of a working value.";

        private const string CategoryEditorDoc =
            "Material categories group related material names together (e.g. 'stone', 'timberframe', " +
            "'thatch') so Continuous Recolor and other category-aware features can act on 'everything " +
            "stone-like' instead of needing an exact material name.\n\n" +
            "Include/Exclude are comma-separated SUBSTRINGS, matched case-insensitively against the " +
            "material name - not exact names, not regex. A material counts as this category if its " +
            "name contains any Include substring AND doesn't contain any Exclude substring. Exclude " +
            "is for carving out false positives (e.g. a category matching 'wall' that needs to exclude " +
            "'drywall_interior' specifically).\n\n" +
            "Test Material at the bottom lets you type a real material name and see which of the " +
            "CURRENT (unsaved) rows above would match it - check an edit does what you expect before " +
            "committing with Save.\n\n" +
            "+ New Category adds a blank row; Delete removes one. Categories are stored the same " +
            "editable-override way as everything else: a shipped default under the module's own " +
            "ReferenceData folder, superseded once you Save (which writes to your Documents folder " +
            "and always wins after that).\n\n" +
            "EXPORT ALL / IMPORT (MERGE) / OPEN FOLDER move category definitions through " +
            "MaterialSwapTool\\Exports\\Categories. Export writes the SAVED categories, not unsaved " +
            "rows on screen. Import merges rather than replaces - new categories added, existing " +
            "ones gain missing patterns, nothing removed - and refreshes the rows, so Save any " +
            "on-screen edits you care about first. See 'Import / Export - where everything goes'.";

        private const string CultureEditorDoc =
            "Two different lists per culture, used for two different purposes - don't confuse them.\n\n" +
            "Unique: strict substring PATTERNS used for CULTURE INFERENCE - if a rule's FROM material " +
            "contains one of these, that rule (and any preset built from it) gets tagged with this " +
            "culture automatically. Kept deliberately narrow (e.g. Vlandia's real architecture " +
            "materials mostly have NO culture branding at all - stone_wall_11, planks_5 - the same " +
            "generic names other cultures' buildings use - so Vlandia's Unique list leans on a handful " +
            "of names that genuinely are Vlandia-specific instead).\n\n" +
            "Common: EXACT material names (not patterns) empirically seen on that culture's real " +
            "architecture prefabs, including ones shared with other cultures. Not used for inference - " +
            "used by the Culture Preset Generator's 'stub' button to scaffold a starter preset: 'these " +
            "are the materials real buildings of this culture actually use, go fill in what they " +
            "become.'\n\n" +
            "Common is shown COLLAPSED by default (a count + Edit button) rather than as a live text " +
            "box - a real culture's Common list runs 60-145+ entries, too much to render live for " +
            "every culture at once. Click Edit on a row to expand just that one culture's list, " +
            "Done to collapse it back.\n\n" +
            "+ New Culture adds a brand new culture, not limited to the 6 built-in ones - useful for a " +
            "custom-culture mod's own architecture set.";

        private const string CultureGeneratorDoc =
            "Two separate features in one panel, opened from Material Swap Tool.\n\n" +
            "1) Bridge generator (top section): pick a FROM and a TO culture by clicking a built-in " +
            "preset name from the list (alternates filling From then To on each click - the boxes " +
            "stay directly typeable too), then Generate. This finds the two chosen presets' shared " +
            "Empire-source materials (most built-in presets are Empire-to-CultureX swaps) and chains " +
            "them into a new direct FromCulture-to-ToCulture preset, saved under Personal in Browse " +
            "Presets. When the two share no Empire-source material, a second pass matches by " +
            "CATEGORY ROLE instead (wall to wall, roof to roof - role categories outrank material-" +
            "type ones). A material that bridges to nothing in either pass is dropped and logged, " +
            "and the usual cause is a missing category: timber frame only started bridging once it " +
            "was categorised as a wall as well as timber. Expect alpha-tested materials to produce " +
            "the ugliest results; remove them from the culture's lists if they bother you.\n\n" +
            "2) Stub generator (culture list below): click a culture name to generate a blank starter " +
            "preset from that culture's Common material list (see Culture Editor) - one blank " +
            "'material -> ' rule per material real buildings of that culture use, ready for you to " +
            "fill in the -> targets by hand. Useful for a culture with no built-in preset yet, or a " +
            "more complete starting point than the hand-authored ones cover.\n\n" +
            "Edit Cultures opens the Culture Editor directly from this panel; Edit Categories opens " +
            "the Category Editor - bridge generation matches materials by CATEGORY role, so a " +
            "material that bridges to nothing usually needs a category tweak (the generator logs " +
            "every material it drops, with the reason, as [CultureGen] in tool.log).";

        private const string SceneAnalyzerOverviewDoc =
            "F7, a separate tool from material swapping entirely - scene-wide diagnostics and fixes " +
            "ported from the community BannerlordSceneAnalyzer PowerShell project (with Skirmish and " +
            "Editor-Spawn checks specifically credited to Gotha's BL_AddTestScene mod, which is where " +
            "that logic was originally sourced from).\n\n" +
            "Every check runs against the LIVE scene via the editor's GameEntity/Scene API, not the " +
            "saved scene.xscene file on disk - so it reflects whatever you've changed in the editor " +
            "even before saving, and any fix action applies instantly with no 'reload the scene' step. " +
            "The one exception is references.txt sanity, which reads an auxiliary file alongside the " +
            "scene by nature (it's not entity data at all).\n\n" +
            "Full Scene Scan runs every check below at once and opens the combined results in a " +
            "preview list. Battle/Skirmish/Siege buttons run just that mode's own requirement checks. " +
            "Fix actions are separate, confirm-gated buttons - see the Fix Actions topic.";

        private const string SceneAnalyzerChecksDoc =
            "What Full Scene Scan checks for (each becomes its own section in the results):\n\n" +
            "Bugged Physics Shapes - entities matching a curated list of known-problematic collision " +
            "shapes.\n" +
            "Misleading Physics - entities that visually look like they have gaps/openings but whose " +
            "collision is actually solid (players get stuck expecting to walk through).\n" +
            "Duplicate / Overlapping Entities - top-level entities of the same type sitting at the " +
            "exact same position and rotation (see the Fix Actions topic for why this is restricted " +
            "to top-level entities specifically).\n" +
            "LOD Substitution Recommendations - entities matching a curated 'this LOD variant has a " +
            "known-better replacement' list.\n" +
            "Known Bad / Early-Popping LODs - entities on a curated list of LODs that pop to a " +
            "noticeably worse state too early at normal viewing distance.\n" +
            "Non-Uniform Scale on Physics Entities - an entity (or a direct child of it) with a body " +
            "flag set, scaled unevenly across axes (e.g. 1.0/1.0/1.5) - physics behaviour under non-" +
            "uniform scale is undefined in this engine.\n" +
            "Sittable / Animation-Interact Prefabs - entities with a sit/interact-triggering script " +
            "(on themselves or a child), flagged so you can verify that's intentional for the game " +
            "mode you're building for.\n" +
            "map_ Prefix Entities - entities named with the map_ prefix (world-map assets), usually " +
            "fine but worth a look if they ended up in a non-map scene.\n" +
            "Interior Entities - entities with 'interior' in the name, cross-referenced against a " +
            "curated known-good/verify list.\n" +
            "Walk / Barrier Volumes - reports how many walk_volume/barrier_volume entities exist (used " +
            "by AI pathing in Skirmish/Siege) - zero of either isn't necessarily wrong, but worth " +
            "knowing.\n" +
            "Entities Outside border_soft Boundary - computes a 2D convex hull from all border_soft " +
            "entity positions, expands it by a 10-unit margin, and flags anything (other than border/" +
            "spawn/camera/envmap entities themselves) sitting outside it.\n" +
            "General MP - spawn_visual (missing this WILL crash team-select), mp_camera_start_pos, " +
            "envmap_prop reflection capture, and flee_line (flagged as a legacy check only - it's been " +
            "non-functional since the War Sails 1.3 patch).\n" +
            "Climbable Civilian Ladders - civilian ladder entities with a skeleton or the Ladder body " +
            "flag, which crash the game if touched in Battle/Skirmish/TDM (Siege is exempt).\n" +
            "Editor Playtest Spawns - entities tagged sp_play or spawnpoint_player, needed to playtest " +
            "in the editor at all.\n" +
            "references.txt Sanity - checks the scene folder's references.txt entry count against its " +
            "own declared header count.";

        private const string DeprecatedChecksDoc =
            "Some curated reference-data entries get retired when the underlying problem is believed " +
            "fixed by TaleWorlds, rather than deleted outright - they're moved to ReferenceData\\" +
            "Deprecated_Legacy_Checks.txt (Documents\\Mount and Blade II Bannerlord\\MaterialSwapTool\\ " +
            "if you've made an override copy) instead, verbatim, with a dated header explaining why " +
            "and exactly which active file to paste the block back into if the issue ever resurfaces.\n\n" +
            "Currently deprecated (2026-08-17): the wooden_platform_2_* LOD Substitution recommendations " +
            "(LOD_Substitutions.txt) and the original wooden_platform_* series' entries in the Known " +
            "Bad / Early-Popping LODs check (Known_Bad_LODs.txt) - both were judged legacy/fixed. " +
            "Everything else in those two files (fences, castle walls, structural details) is untouched.";

        private const string SceneAnalyzerModeChecksDoc =
            "Battle checks: attacker/defender spawn zones (sergeant_spawn / skirmish_start_spawn / " +
            "mp_spawnpoint_attacker-defender), warns if a spawn container uses a non-standard name " +
            "(the actual spawn controller only recognizes 'skirmish_start_spawn' after warmup), the " +
            "three sergeant capture flags, and border_soft count.\n\n" +
            "Skirmish checks (ported from Gotha's BL_AddTestScene): defender/attacker starting spawn " +
            "zones and their 'starting'/'spawn_zone' tags, respawn zone counts against the documented " +
            "'up to 3 per side' guidance, the three capture flags, border_soft presence, and - " +
            "importantly - flags any Siege-only destructible entity (rock piles, pot piles, arrow " +
            "barrels, or anything with a StonePile/DestructibleComponent script) present in a non-" +
            "Siege scene, since those crash Skirmish/other non-Siege modes on touch.\n\n" +
            "Siege checks: sp_zone_0 through sp_zone_6 tags (0 and 6 specifically required, missing " +
            "either WILL crash the server), duplicate zone tags across multiple entities, all seven " +
            "capture flags, mp_spawnpoint presence, WallSegment script entities and their broken_child/" +
            "solid_child setup, outer_gate/inner_gate tags and CastleGate script, and spawner presence " +
            "for the battering ram, siege tower/ladder, and ranged siege engines - these last three are " +
            "WARNINGS not errors, since a map missing equipment is incomplete rather than broken (won't " +
            "crash the server the way a missing sp_zone tag will).";

        private const string SceneAnalyzerActionsDoc =
            "Delete Duplicates / Tag Duplicates - act on the same duplicate-detection logic as the " +
            "Full Scan check, but restricted to TOP-LEVEL entities only (no parent), even though the " +
            "live API can technically resolve world-space position for entities at any nesting depth. " +
            "That restriction is deliberate: nested children of unrelated prefab instances often share " +
            "identical internal part names (e.g. every wall prefab's internal plank sub-part is named " +
            "the same thing) with identical relative offsets, which produced real false-positive " +
            "matches across entirely different, legitimately-placed structures when this wasn't " +
            "restricted. Delete uses an exact-position match only (0 units) - Tag uses the same " +
            "matching but just marks entities with BSA_LIKELY_DUPLICATE instead of removing them, " +
            "safer for reviewing first.\n\n" +
            "Tag Bugged Physics - tags every entity matching the known-bugged-physics reference list " +
            "with BSA_BUGGED_PHYSICS_SHAPE, so you can find them in the editor by tag search.\n\n" +
            "Apply LOD Fixes - instantly swaps every entity on the LOD Substitution list for its " +
            "recommended replacement, live, preserving position/rotation.\n\n" +
            "Fix Known-Broken Prefabs - Scan then Apply Fix swaps entities matching a curated broken-" +
            "prefab list (currently module_wall_plank_a/b, which ship with inverted collision) for " +
            "clean-named fixed replacements. Scale does NOT carry over on any of these swap-based " +
            "fixes - the live API has no way to set an instantiated entity's scale after the fact, " +
            "only read it - so an entity with non-default scale will lose that scale on the " +
            "replacement; check affected entities visually afterward.\n\n" +
            "All of these are confirm-gated (a dialog before anything happens) and backed up first.";

        private const string SceneAnalyzerRequirementsDoc =
            "A separate feature from the checks above: named checklists of what a specific SCENE TYPE " +
            "needs, grounded in TaleWorlds' own documentation rather than inferred (the seaborne " +
            "village raid checklist, for example, is sourced from moddocs.bannerlord.com's War Sails " +
            "page). Click a requirement set's name in the list to run it - results show each " +
            "requirement as [OK]/[MISSING]/[i] (informational items that can't be checked from a live " +
            "entity at all, like NavMesh depth, are always shown but never marked found/missing).\n\n" +
            "Requirement sets are defined in scene_requirements.json (same editable-override " +
            "convention as everything else) - add more scene types there without needing a redeploy.";

        private const string PrefabSwapperDoc =
            "THE SWAP SECTION LIVES ON F5 (Prefab Creator); F6 is purely Distribution. This topic " +
            "covers both.\n\n" +
            "SWAPPING (F5). The general-purpose version of Scene Analyzer's curated 'Fix Known-Broken " +
            "Prefabs': swap ANY entity for ANY other prefab by name.\n\n" +
            "MODE: ADD vs SWAP - the toggle at the top of the swap section, and it governs Swap " +
            "Selected, Swap All Matching, live-reference swaps AND swap-set Apply alike. SWAP " +
            "(default) deletes the original and recreates it as the new prefab. ADD places the new " +
            "prefab at the original's exact spot and KEEPS the original - they overlap until you " +
            "delete the old ones by hand. Use ADD whenever the targets are pieces INSIDE placed " +
            "prefab instances: removing those trips the editor's own 'break prefab?' dialog once per " +
            "piece and half-fails. Add-mode additions are undone from Distribute's undo (entities " +
            "were created, nothing was removed); Swap-mode batches from Recent Swaps as always.\n\n" +
            "Fill From Selection fills the Old (or New) Prefab Name box from your selection - the " +
            "REAL prefab name via GetPrefabName, falling back to the display name only when there is " +
            "no prefab behind the entity (and saying so). Display names drift (_2 duplicate " +
            "suffixes, _mst renames, recolor suffixes); the prefab name is the one that swaps and " +
            "matches correctly. Swap-set Apply matches the same way.\n\n" +
            "POSITION/ROTATION are preserved on every swap. SCALE is a toggle: 1x (default) gives " +
            "the new entity the prefab's own authored scale; Inherit keeps the old entity's scale. " +
            "Two optional inputs apply to everything swapped in - regular swaps, live reference, " +
            "and swap-set Apply alike: Z Rot (degrees of extra world-Z rotation) and Scale mult " +
            "(uniform multiplier); 0 and 1 mean off.\n\n" +
            "CHILDREN: parts belonging to the OLD prefab's own definition are removed with it; " +
            "children you added by hand are carried onto the new prefab as extras. That is what " +
            "keeps a cross-culture swap from stacking both cultures' merlons on one tower.\n\n" +
            "Undo Last Swap reverts the most recent swap batch - restoring at the pre-swap frame, " +
            "so Z-rot/scale extras are undone too, and always in replace-mode regardless of the " +
            "ADD/SWAP toggle.\n\n" +
            "WHICH SWAP TO USE: if the new thing is a REAL prefab (saved with Save As, so it has a " +
            "prefab name the engine can instantiate), use Swap Selected / Swap All Matching / a " +
            "swap set. If it is something you assembled in the scene and never saved as a prefab - " +
            "a 'broken' prefab, which is what one entity parented under another is until you save " +
            "it - only Set Live Reference can copy it.\n\n" +
            "SWAP TO LIVE REFERENCE: skips saving a prefab first entirely. Select an entity " +
            "currently in the scene - saved or not - and click Set Live Reference; then select " +
            "whatever you want replaced and click Swap Selected -> Live Reference. The result is an " +
            "EXACT copy of the reference as it currently exists, whole tree included - and NOTHING " +
            "from the old entity rides along (no children, no color overrides): making it look " +
            "exactly like the reference is the point of the mode. Use name-mode swaps when you want " +
            "hand-added extras carried over. Undo within the same session works normally. (The " +
            "engine-level details: Technical Docs on F9, 'Live-reference swaps'.)\n\n" +
            "DISTRIBUTION (F6) opens on a MODE SELECTOR - In Grid / Along Path / Onto Surface - " +
            "showing only the active mode's controls, with ONE Distribute button that runs whatever " +
            "is on screen and a plain-language summary line above it reading back exactly what the " +
            "next run will do.\n\n" +
            "ONTO SURFACE is the grid, but every cell raycasts STRAIGHT DOWN and lands on whatever " +
            "is beneath it - the origin supplies only the horizontal start and heading, height comes " +
            "from the surface. The 'Only land on' box is a FILTER, not a destination: one or more " +
            "entity names (comma-separated, or Use Selected Entities to fill from a multi-select) " +
            "that count as ground - the ray passes through everything else, so clutter cannot block " +
            "a cell. Blank = first thing hit.\n\n" +
            "ALONG PATH places copies along a path authored with the editor's own Path tool: type " +
            "the path's name, then either a manual count or 'fill path', which derives the count " +
            "from the path length and spacing. Relative spacing with a NEGATIVE gap packs pieces " +
            "into each other for wall-type prefabs. Most prefabs were not built with this tool in " +
            "mind and come out facing the wrong way: the Z Rotation box (90 or 180) fixes that " +
            "without re-origining anything. Anchor choice: exact origin point, or bottom center.\n\n" +
            "SPACING, RELATIVE vs ABSOLUTE (all modes). Relative measures from the prefab's own " +
            "size - 0 means side by side, negative overlaps, positive leaves a gap. Absolute is a " +
            "fixed distance in units between origins, regardless of size. Relative is the easier " +
            "one for most work. A grid count must be at least 1 on each axis; 0 is refused.\n\n" +
            "TWO ROTATION BOXES, both world-Z degrees, both 0 = off, independent: Rotation (Entity) " +
            "spins each placed piece in place; Rotation (Grid) pivots the whole cell lattice around " +
            "the origin - a non-world-aligned grid without rotating any surface.\n\n" +
            "Extent: Fill Target Surfaces derives the grid's footprint from the SELECTED surfaces' " +
            "combined bounding box instead of typed counts - select the surfaces, Distribute, done; " +
            "the lattice follows the first surface's own rotation, and cells with no target beneath " +
            "them miss and are skipped, so irregular surfaces self-trim. With nothing selected, " +
            "exact names typed in 'Only land on' are resolved to entities and those are filled " +
            "(scene-wide by name - the status says how many matched). Cell size is the prefab's own " +
            "measured size plus Gap - the number of surfaces never sets the cell count, footprint " +
            "divided by spacing does. Max Instances (typed, default 500) caps every distribute " +
            "run.\n\n" +
            "FILL FROM SELECTION (by-name source) fills the prefab name AND switches Origin to " +
            "Coordinates filled from that entity's position - one click captures the what and the " +
            "where, freeing the selection to become the target surfaces.\n\n" +
            "MIRROR IS A TRUE FLIP, not a 180-degree rotation: an asymmetric feature on the left of " +
            "the source ends up on the right of the copy, which no rotation can produce. To mirror " +
            "across a point of your choosing, Fill From Selection on the mirror point AND switch to " +
            "the Custom option - with Custom left off, the mirror runs on the world axis and the " +
            "typed point is ignored. Mirroring twice occasionally leaves a copy rendering inside-" +
            "out; save and reload and it displays correctly (the saved data is right).\n\n" +
            "MIRROR names its anchors wall_Mirrored, then wall_Mirroredx2, x3... Mirroring a " +
            "previous run's ANCHOR mirrors its children individually (prefab pieces re-instantiate " +
            "at their final frames, which renders correctly at any handedness). One residual " +
            "limitation: a hand-built prefab-LESS piece mirrored an ODD number of times renders " +
            "inside-out until the scene is saved and reloaded - the saved data is correct, the " +
            "live winding cache is not reachable from managed code. Mirror also warns when the " +
            "source already CONTAINS mirror-flipped pieces (stowaways from earlier runs) - clean " +
            "the source instead of mirroring the contamination onward.\n\n" +
            "GRID SOURCE: PREFAB NAME vs SELECTION. The toggle sits above the axis controls " +
            "because it changes what lands in each cell.\n\n" +
            "PREFAB NAME instantiates a fresh copy of a named prefab into " +
            "every cell, including the first. FILL FROM SELECTION next to the name box reads that " +
            "name off whatever you have selected - and reads the REAL prefab name, not the display " +
            "name, which are not the same thing once 'Rename Changed' has appended _mst or the " +
            "editor has appended .001 to a duplicate.\n\n" +
            "SELECTION copies what you already have selected into each cell instead. Three things " +
            "that does which the by-name path cannot: it works on an entity with NO saved prefab " +
            "(a composite you have assembled but not turned into a prefab yet is exactly what you " +
            "most want to tile); the copies keep their per-instance material and colour overrides, " +
            "where Instantiate would rebuild from the prefab and drop them; and a MULTI-ENTITY " +
            "selection tiles as a unit, every piece keeping its offset from the others, so a wall " +
            "plus its buttress and torch repeat as one arrangement.\n\n" +
            "In Selection mode the FIRST cell is deliberately left empty - your originals already " +
            "occupy it - and your originals are not re-parented under the new anchor either, so " +
            "nothing you did not ask about gets reorganised. The instance cap counts entities " +
            "rather than cells, because a 5x5 grid of a 12-piece assembly is already 288 copies.";
    }
}

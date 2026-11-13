using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace MaterialSwapTool.Core
{
    // SCENE DIFF (2026-11-12): compares two scene.xscene FILES and lists what changed between
    // them - the saved scene against one of its backups, or two backups against each other.
    //
    // Deliberately pure: System.Xml and nothing from TaleWorlds, so both the parse and the
    // compare can run on a background Task (the files are 6-8 MB / 12,000+ entities) and the
    // whole class can be exercised outside the editor against real backup files.
    //
    // WHAT A FILE GIVES US TO MATCH ON. An xscene entity has no id. It has a name and/or a
    // prefab attribute, a transform, and a position in the tree - and duplicate names are the
    // norm (hundreds of the same rock). So entities are matched, never looked up:
    //
    //   - Top-down, sibling group by sibling group: the two root lists are matched first, then
    //     the children of each matched pair, and so on. A child is only ever compared with the
    //     children of the entity its parent was matched to, which keeps child transforms (local
    //     to the parent) comparable and means a moved parent does not report every child.
    //   - Within a group, by "origin" (prefab, else old_prefab_name, else name), in passes that
    //     go from certain to plausible:
    //       1. same origin + name + byte-identical transform        -> the same entity, in place
    //       2. same origin + name + same position                   -> rotated / scaled in place
    //       3. same origin + name, nearest remaining position       -> moved (greedy, closest first)
    //       4. same origin, same position, different name           -> renamed in place
    //       5. different origin, byte-identical transform           -> prefab changed in place
    //     Whatever is left is removed (only in FROM) or added (only in TO).
    //
    // LIMITS (also in docs\not-yet-in-editor\SCENE-DIFF.md):
    //   - Pass 3 cannot tell "moved" from "deleted one, placed another of the same prefab
    //     somewhere else" - both are one fewer here and one more there. It reports the cheapest
    //     explanation: a move, with the distance, so an implausible one is visible.
    //   - Among identical duplicates, WHICH one moved is a guess (closest pair first). The count
    //     and the positions are right; the pairing may not be the one you made.
    //   - An entity that changed BOTH its name/prefab and its transform is reported as one
    //     removed plus one added.
    //   - Reparenting (dragging an entity under another) shows as removed + added.
    //   - Child positions are local to the parent; a child row carries its top-level ancestor's
    //     position for navigation and prints its own local position as text.
    public static class SceneFileDiff
    {
        // The file stores three decimals, so anything under 1.5 mm is formatting, not a move.
        public const double Epsilon = 0.0015;

        // Above this many candidate pairs a same-name group falls back from the globally sorted
        // greedy match to a per-entity nearest search (no pair list to sort or hold in memory).
        private const long SortedGreedyPairLimit = 200000;

        public sealed class MeshRec
        {
            public string Name, Material, Factor, Factor2;
        }

        public sealed class Ent
        {
            public string Name, Prefab, OldPrefab;
            public int Parent = -1, Root = -1, Depth;
            public List<int> Children;
            public bool HasPos;
            public double X, Y, Z;
            public string PosRaw, RotRaw, ScaleRaw;
            public bool RotIsMatrix;
            public List<string> Tags;
            public List<MeshRec> Meshes;
            public ulong ScriptSig, PhysSig, LightSig, MeshArgSig, ComponentSig, FlagSig, OverrideSig, RestSig;
            public int Descendants;
            public int Match = -1;
            public ulong ContentSig;   // everything about the entity itself except its transform

            // What the entity was made from. Stable across "break prefab link" (prefab= becomes
            // old_prefab_name=) and across a rename, which is why matching groups by it.
            public string Origin => Prefab ?? OldPrefab ?? Name ?? "(unnamed)";

            public string Display
            {
                get
                {
                    var o = Origin;
                    return (Name != null && !string.Equals(Name, o, StringComparison.Ordinal))
                        ? "'" + Name + "' [" + o + "]"
                        : "'" + o + "'";
                }
            }
        }

        public sealed class SceneFile
        {
            public string Path;
            public List<Ent> Entities = new List<Ent>();
            public List<int> Roots = new List<int>();
            public ulong GlobalSig;
            public int LightCount;
        }

        public enum Kind { Removed, Added, PrefabRef, Renamed, Moved, RotScale, Material, Color, Other }

        private static readonly string[] SectionTitles =
        {
            "Removed", "Added", "Prefab reference changed", "Renamed", "Moved",
            "Rotated / scaled in place", "Material changed", "Color changed", "Other changes",
        };

        public sealed class Change
        {
            public Kind Primary;
            public string Text;
            public double SortKey;
        }

        public sealed class Result
        {
            public string FromLabel, ToLabel;
            public int FromCount, ToCount;
            public List<Change> Changes = new List<Change>();
            public int Added, AddedInside, Removed, RemovedInside, Moved, RotScaled, Renamed, PrefabRef, Material, Color, Other;
            public bool SceneSettingsChanged;
            public long ElapsedMs;

            public bool HasChanges => Changes.Count > 0 || SceneSettingsChanged;

            public string Summary
            {
                get
                {
                    if (Changes.Count == 0)
                        return "No entity differences (" + FromCount.ToString("N0", CultureInfo.InvariantCulture) + " entities in both)"
                               + (SceneSettingsChanged ? "; scene-level settings differ." : ".");
                    var parts = new List<string>();
                    if (Added > 0) parts.Add("+" + Added + " added");
                    if (Removed > 0) parts.Add("-" + Removed + " removed");
                    if (Moved > 0) parts.Add(Moved + " moved");
                    if (RotScaled > 0) parts.Add(RotScaled + " rotated/scaled");
                    if (Renamed > 0) parts.Add(Renamed + " renamed");
                    if (PrefabRef > 0) parts.Add(PrefabRef + " prefab ref");
                    if (Material > 0) parts.Add(Material + " material");
                    if (Color > 0) parts.Add(Color + " color");
                    if (Other > 0) parts.Add(Other + " other");
                    return string.Join(", ", parts)
                           + "  (" + FromCount.ToString("N0", CultureInfo.InvariantCulture) + " -> "
                           + ToCount.ToString("N0", CultureInfo.InvariantCulture) + " entities)";
                }
            }
        }

        // ============================================================
        // PARSE
        // ============================================================

        private enum Cat { Rest, Transform, Tags, Scripts, Physics, Components, Flags, Children }

        private sealed class Frame
        {
            public int Ent;
            public int Depth;
            public Cat Cat;
            public bool FoldAll;   // a prefab-instance override record: everything goes into the owner's OverrideSig
        }

        // FNV-1a, 64 bit. 0 is reserved for "nothing was ever mixed in".
        private static ulong Mix(ulong h, string s)
        {
            if (h == 0) h = 14695981039346656037UL;
            if (s != null)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 1099511628211UL;
                }
            }
            h ^= 0xFF;
            h *= 1099511628211UL;
            return h;
        }

        // Mixes the current element's name and every attribute. Leaves the reader on the element.
        private static ulong MixElement(XmlReader reader, ulong h)
        {
            h = Mix(h, reader.LocalName);
            if (reader.MoveToFirstAttribute())
            {
                do
                {
                    h = Mix(h, reader.LocalName);
                    h = Mix(h, reader.Value);
                } while (reader.MoveToNextAttribute());
                reader.MoveToElement();
            }
            return h;
        }

        private static Cat CategoryFor(string element)
        {
            switch (element)
            {
                case "transform": return Cat.Transform;
                case "tags": return Cat.Tags;
                case "scripts": return Cat.Scripts;
                case "physics": return Cat.Physics;
                case "components": return Cat.Components;
                case "children": return Cat.Children;
            }
            if (element.IndexOf("flag", StringComparison.Ordinal) >= 0 ||
                element.IndexOf("visibility", StringComparison.Ordinal) >= 0)
                return Cat.Flags;
            return Cat.Rest;
        }

        // FileShare.ReadWrite: the "current saved file" is the editor's own scene.xscene, and
        // an exclusive open here would make the editor's next save fail. If the editor is
        // mid-save the read may see a truncated document - that surfaces as an XmlException,
        // which the caller reports as "try again".
        public static SceneFile Parse(string path)
        {
            var file = new SceneFile { Path = path };
            var ents = file.Entities;
            var frames = new List<Frame>();

            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                IgnoreProcessingInstructions = true,
                DtdProcessing = DtdProcessing.Prohibit,
            };

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan))
            using (var reader = XmlReader.Create(stream, settings))
            {
                while (reader.Read())
                {
                    var nodeType = reader.NodeType;

                    if (nodeType == XmlNodeType.EndElement)
                    {
                        if (frames.Count > 0 && frames[frames.Count - 1].Depth == reader.Depth && reader.LocalName == "game_entity")
                            frames.RemoveAt(frames.Count - 1);
                        continue;
                    }

                    if (nodeType == XmlNodeType.Text || nodeType == XmlNodeType.CDATA)
                    {
                        if (frames.Count == 0) file.GlobalSig = Mix(file.GlobalSig, reader.Value);
                        else
                        {
                            var owner = ents[frames[frames.Count - 1].Ent];
                            owner.RestSig = Mix(owner.RestSig, reader.Value);
                        }
                        continue;
                    }

                    if (nodeType != XmlNodeType.Element) continue;

                    string el = reader.LocalName;
                    int depth = reader.Depth;
                    bool isEmpty = reader.IsEmptyElement;
                    Frame top = frames.Count > 0 ? frames[frames.Count - 1] : null;

                    if (el == "game_entity")
                    {
                        string name = null, prefab = null, oldPrefab = null, index = null;
                        ulong attrSig = 0;
                        if (reader.MoveToFirstAttribute())
                        {
                            do
                            {
                                switch (reader.LocalName)
                                {
                                    case "name": name = reader.Value; break;
                                    case "prefab": prefab = reader.Value; break;
                                    case "old_prefab_name": oldPrefab = reader.Value; break;
                                    case "_index_": index = reader.Value; break;
                                    default:
                                        attrSig = Mix(attrSig, reader.LocalName);
                                        attrSig = Mix(attrSig, reader.Value);
                                        break;
                                }
                            } while (reader.MoveToNextAttribute());
                            reader.MoveToElement();
                        }
                        if (string.IsNullOrEmpty(name)) name = null;
                        if (string.IsNullOrEmpty(prefab)) prefab = null;
                        if (string.IsNullOrEmpty(oldPrefab)) oldPrefab = null;

                        // <game_entity _index_="1"> with no name and no prefab is not a placed
                        // entity: it is the record of what was overridden on child #1 of the
                        // enclosing prefab instance. It folds into its owner as "prefab child
                        // overrides" instead of showing up as an anonymous added/removed entity.
                        bool inOverride = top != null && top.FoldAll;
                        bool isOverrideRecord = top != null && (inOverride || (index != null && name == null && prefab == null));
                        if (isOverrideRecord)
                        {
                            var owner = ents[top.Ent];
                            owner.OverrideSig = Mix(owner.OverrideSig, "game_entity");
                            owner.OverrideSig = Mix(owner.OverrideSig, index);
                            owner.OverrideSig = Mix(owner.OverrideSig, name);
                            owner.OverrideSig = Mix(owner.OverrideSig, prefab);
                            owner.OverrideSig ^= attrSig;
                            if (!isEmpty) frames.Add(new Frame { Ent = top.Ent, Depth = depth, FoldAll = true });
                            continue;
                        }

                        var ent = new Ent { Name = name, Prefab = prefab, OldPrefab = oldPrefab, FlagSig = attrSig };
                        int id = ents.Count;
                        ents.Add(ent);
                        if (top != null)
                        {
                            ent.Parent = top.Ent;
                            var parent = ents[top.Ent];
                            ent.Depth = parent.Depth + 1;
                            ent.Root = parent.Root;
                            (parent.Children ?? (parent.Children = new List<int>())).Add(id);
                        }
                        else
                        {
                            ent.Root = id;
                            file.Roots.Add(id);
                        }
                        if (!isEmpty) frames.Add(new Frame { Ent = id, Depth = depth });
                        continue;
                    }

                    if (top == null)
                    {
                        // Outside every entity: atmosphere, terrain, water, nav mesh records...
                        // The root <scene> element is skipped because its revision attribute
                        // changes on every save and would make this always read "different".
                        // <editor_data> is skipped for the same reason: it holds the editor
                        // camera's position, which moves between any two saves.
                        if (depth > 0 && el != "editor_data") file.GlobalSig = MixElement(reader, file.GlobalSig);
                        continue;
                    }

                    var e = ents[top.Ent];
                    if (top.FoldAll)
                    {
                        e.OverrideSig = MixElement(reader, e.OverrideSig);
                        continue;
                    }

                    if (depth == top.Depth + 1) top.Cat = CategoryFor(el);

                    switch (top.Cat)
                    {
                        case Cat.Children:
                            break;

                        case Cat.Transform:
                            ReadTransform(reader, e);
                            break;

                        case Cat.Tags:
                            if (el == "tag")
                            {
                                var tag = reader.GetAttribute("name");
                                if (tag != null) (e.Tags ?? (e.Tags = new List<string>())).Add(tag);
                            }
                            break;

                        case Cat.Scripts:
                            e.ScriptSig = MixElement(reader, e.ScriptSig);
                            break;

                        case Cat.Physics:
                            e.PhysSig = MixElement(reader, e.PhysSig);
                            break;

                        case Cat.Flags:
                            e.FlagSig = MixElement(reader, e.FlagSig);
                            break;

                        case Cat.Components:
                            if (el == "mesh") ReadMesh(reader, e);
                            else if (el.StartsWith("light", StringComparison.Ordinal))
                            {
                                if (el == "light_component") file.LightCount++;
                                e.LightSig = MixElement(reader, e.LightSig);
                            }
                            else if (el != "components")
                                e.ComponentSig = MixElement(reader, e.ComponentSig);
                            break;

                        default:
                            e.RestSig = MixElement(reader, e.RestSig);
                            break;
                    }
                }
            }

            // Children always follow their parent in document order, so one reverse sweep
            // totals every subtree.
            for (int i = ents.Count - 1; i >= 0; i--)
            {
                var p = ents[i].Parent;
                if (p >= 0) ents[p].Descendants += ents[i].Descendants + 1;
                ents[i].ContentSig = ComputeContentSig(ents[i]);
            }
            return file;
        }

        // Used only to pair stacked duplicates correctly (see MatchGroup): two entities of the
        // same prefab at the same transform are told apart by what they carry. Tags are summed
        // so their order in the file does not matter.
        private static ulong ComputeContentSig(Ent e)
        {
            ulong h = Mix(0, e.Prefab);
            h = Mix(h, e.OldPrefab);
            h = Mix(h, e.Name);
            if (e.Meshes != null)
                foreach (var m in e.Meshes)
                {
                    h = Mix(h, m.Name); h = Mix(h, m.Material); h = Mix(h, m.Factor); h = Mix(h, m.Factor2);
                }
            ulong tags = 0;
            if (e.Tags != null) foreach (var t in e.Tags) tags += Mix(0, t);
            h ^= tags * 0x9E3779B97F4A7C15UL;
            h ^= e.ScriptSig; h *= 1099511628211UL;
            h ^= e.PhysSig; h *= 1099511628211UL;
            h ^= e.LightSig; h *= 1099511628211UL;
            h ^= e.MeshArgSig; h *= 1099511628211UL;
            h ^= e.ComponentSig; h *= 1099511628211UL;
            h ^= e.FlagSig; h *= 1099511628211UL;
            h ^= e.OverrideSig; h *= 1099511628211UL;
            h ^= e.RestSig; h *= 1099511628211UL;
            h ^= (ulong)(e.Children?.Count ?? 0); h *= 1099511628211UL;
            return h;
        }

        private static void ReadTransform(XmlReader reader, Ent e)
        {
            if (!reader.MoveToFirstAttribute()) return;
            do
            {
                switch (reader.LocalName)
                {
                    case "position":
                        e.PosRaw = reader.Value;
                        var p = ParseNumbers(e.PosRaw);
                        if (p != null && p.Length >= 3) { e.X = p[0]; e.Y = p[1]; e.Z = p[2]; e.HasPos = true; }
                        break;
                    case "rotation_euler":
                        e.RotRaw = reader.Value; e.RotIsMatrix = false;
                        break;
                    case "non_orthogonal_rotation_mat":
                        e.RotRaw = reader.Value; e.RotIsMatrix = true;
                        break;
                    case "scale":
                        e.ScaleRaw = reader.Value;
                        break;
                    default:
                        e.RestSig = Mix(e.RestSig, reader.LocalName);
                        e.RestSig = Mix(e.RestSig, reader.Value);
                        break;
                }
            } while (reader.MoveToNextAttribute());
            reader.MoveToElement();
        }

        private static void ReadMesh(XmlReader reader, Ent e)
        {
            var mesh = new MeshRec();
            if (reader.MoveToFirstAttribute())
            {
                do
                {
                    switch (reader.LocalName)
                    {
                        case "name": mesh.Name = reader.Value; break;
                        case "material": mesh.Material = reader.Value; break;
                        case "factor": mesh.Factor = reader.Value; break;
                        case "factor2": mesh.Factor2 = reader.Value; break;
                        default:
                            e.MeshArgSig = Mix(e.MeshArgSig, reader.LocalName);
                            e.MeshArgSig = Mix(e.MeshArgSig, reader.Value);
                            break;
                    }
                } while (reader.MoveToNextAttribute());
                reader.MoveToElement();
            }
            (e.Meshes ?? (e.Meshes = new List<MeshRec>())).Add(mesh);
        }

        private static double[] ParseNumbers(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(',');
            var result = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
                    return null;
            }
            return result;
        }

        // ============================================================
        // COMPARE
        // ============================================================

        private sealed class Ctx
        {
            public List<Ent> A, B;
            public Result R;
        }

        public static Result Compare(SceneFile from, SceneFile to)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var r = new Result { FromCount = from.Entities.Count, ToCount = to.Entities.Count };
            foreach (var e in from.Entities) e.Match = -1;
            foreach (var e in to.Entities) e.Match = -1;

            var ctx = new Ctx { A = from.Entities, B = to.Entities, R = r };
            MatchGroup(ctx, from.Roots, to.Roots);

            r.SceneSettingsChanged = from.GlobalSig != to.GlobalSig;
            r.ElapsedMs = stopwatch.ElapsedMilliseconds;
            return r;
        }

        private static string TransformKey(Ent e) =>
            (e.PosRaw ?? "") + "|" + (e.RotIsMatrix ? "m" : "e") + (e.RotRaw ?? "") + "|" + (e.ScaleRaw ?? "");

        private static string PosKey(Ent e) =>
            e.X.ToString("F3", CultureInfo.InvariantCulture) + "," +
            e.Y.ToString("F3", CultureInfo.InvariantCulture) + "," +
            e.Z.ToString("F3", CultureInfo.InvariantCulture);

        private static void MatchGroup(Ctx ctx, List<int> aKids, List<int> bKids)
        {
            int aCount = aKids?.Count ?? 0, bCount = bKids?.Count ?? 0;
            if (aCount == 0 && bCount == 0) return;

            var ua = aCount > 0 ? new List<int>(aKids) : new List<int>();
            var ub = bCount > 0 ? new List<int>(bKids) : new List<int>();
            var pairs = new List<KeyValuePair<int, int>>();
            var pass5 = new HashSet<int>();   // A-side indices matched by pass 5 (different origin)

            if (ua.Count > 0 && ub.Count > 0)
            {
                // Each certain pass runs twice: first requiring identical CONTENT too, then
                // without. Stacked duplicates (same prefab, same transform - common, and what
                // Delete Duplicates exists for) differ only in content, and the editor does not
                // keep their file order stable; pairing them by order alone reported material
                // and tag changes swapping back and forth between two untouched entities.
                Func<Ent, string> content = e => e.ContentSig.ToString("X16", CultureInfo.InvariantCulture);

                // 1. identical in place
                KeyPass(ctx, ua, ub, pairs, e => e.Origin + "\n" + (e.Name ?? "") + "\n" + TransformKey(e) + "\n" + content(e), null);
                KeyPass(ctx, ua, ub, pairs, e => e.Origin + "\n" + (e.Name ?? "") + "\n" + TransformKey(e), null);
                // 2. same position, rotated/scaled
                KeyPass(ctx, ua, ub, pairs, e => e.Origin + "\n" + (e.Name ?? "") + "\n" + PosKey(e) + "\n" + content(e), null);
                KeyPass(ctx, ua, ub, pairs, e => e.Origin + "\n" + (e.Name ?? "") + "\n" + PosKey(e), null);
                // 3. nearest of the same origin + name (again: same content first)
                NearestPass(ctx, ua, ub, pairs, e => e.Origin + "\n" + (e.Name ?? "") + "\n" + content(e));
                NearestPass(ctx, ua, ub, pairs, e => e.Origin + "\n" + (e.Name ?? ""));
                // 4. renamed in place
                KeyPass(ctx, ua, ub, pairs, e => e.Origin + "\n" + PosKey(e), null);
                // 5. different origin, identical transform. Needs a real position: two
                //    unrelated entities that both lack a transform would otherwise "match".
                KeyPass(ctx, ua, ub, pairs, e => e.HasPos ? TransformKey(e) : null, pass5);
            }

            foreach (var ia in ua) ReportUnmatched(ctx, ctx.A, ia, Kind.Removed);
            foreach (var ib in ub) ReportUnmatched(ctx, ctx.B, ib, Kind.Added);

            foreach (var pair in pairs)
            {
                bool differentOrigin = pass5.Contains(pair.Key);
                ReportPair(ctx, pair.Key, pair.Value, differentOrigin);
                // A swapped prefab has a different internal tree; diffing its children against
                // the old prefab's would list every one of them as removed + added.
                if (!differentOrigin)
                    MatchGroup(ctx, ctx.A[pair.Key].Children, ctx.B[pair.Value].Children);
            }
        }

        private static void KeyPass(Ctx ctx, List<int> ua, List<int> ub, List<KeyValuePair<int, int>> pairs,
                                    Func<Ent, string> key, HashSet<int> record)
        {
            if (ua.Count == 0 || ub.Count == 0) return;

            var byKey = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
            foreach (var ib in ub)
            {
                var k = key(ctx.B[ib]);
                if (k == null) continue;
                if (!byKey.TryGetValue(k, out var queue)) byKey[k] = queue = new Queue<int>();
                queue.Enqueue(ib);
            }

            bool any = false;
            foreach (var ia in ua)
            {
                var k = key(ctx.A[ia]);
                if (k == null) continue;
                if (!byKey.TryGetValue(k, out var queue) || queue.Count == 0) continue;
                int ib = queue.Dequeue();
                ctx.A[ia].Match = ib;
                ctx.B[ib].Match = ia;
                pairs.Add(new KeyValuePair<int, int>(ia, ib));
                record?.Add(ia);
                any = true;
            }

            if (any)
            {
                ua.RemoveAll(i => ctx.A[i].Match >= 0);
                ub.RemoveAll(i => ctx.B[i].Match >= 0);
            }
        }

        private static double DistSq(Ent a, Ent b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        private static void NearestPass(Ctx ctx, List<int> ua, List<int> ub, List<KeyValuePair<int, int>> pairs, Func<Ent, string> groupKey)
        {
            if (ua.Count == 0 || ub.Count == 0) return;

            var groupsB = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            foreach (var ib in ub)
            {
                var k = groupKey(ctx.B[ib]);
                if (!groupsB.TryGetValue(k, out var list)) groupsB[k] = list = new List<int>();
                list.Add(ib);
            }
            var groupsA = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            foreach (var ia in ua)
            {
                var k = groupKey(ctx.A[ia]);
                if (!groupsB.ContainsKey(k)) continue;
                if (!groupsA.TryGetValue(k, out var list)) groupsA[k] = list = new List<int>();
                list.Add(ia);
            }

            bool any = false;
            foreach (var kv in groupsA)
            {
                var la = kv.Value;
                var lb = groupsB[kv.Key];

                if ((long)la.Count * lb.Count <= SortedGreedyPairLimit)
                {
                    // Every candidate pair, closest first: the globally cheapest pairing a
                    // greedy pass can give, and the one that leaves the FARTHEST leftovers as
                    // added/removed when the counts differ.
                    var candidates = new List<KeyValuePair<double, long>>(la.Count * lb.Count);
                    for (int i = 0; i < la.Count; i++)
                        for (int j = 0; j < lb.Count; j++)
                            candidates.Add(new KeyValuePair<double, long>(DistSq(ctx.A[la[i]], ctx.B[lb[j]]), ((long)i << 32) | (uint)j));
                    candidates.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Value.CompareTo(y.Value));

                    foreach (var c in candidates)
                    {
                        int ia = la[(int)(c.Value >> 32)], ib = lb[(int)(c.Value & 0xFFFFFFFF)];
                        if (ctx.A[ia].Match >= 0 || ctx.B[ib].Match >= 0) continue;
                        ctx.A[ia].Match = ib;
                        ctx.B[ib].Match = ia;
                        pairs.Add(new KeyValuePair<int, int>(ia, ib));
                        any = true;
                    }
                }
                else
                {
                    // Very large same-name group: nearest still-free partner per entity, in
                    // file order. Slightly less even than the sorted version, no pair list.
                    foreach (var ia in la)
                    {
                        int best = -1;
                        double bestD = double.MaxValue;
                        foreach (var ib in lb)
                        {
                            if (ctx.B[ib].Match >= 0) continue;
                            var d = DistSq(ctx.A[ia], ctx.B[ib]);
                            if (d < bestD) { bestD = d; best = ib; }
                        }
                        if (best < 0) break;
                        ctx.A[ia].Match = best;
                        ctx.B[best].Match = ia;
                        pairs.Add(new KeyValuePair<int, int>(ia, best));
                        any = true;
                    }
                }
            }

            if (any)
            {
                ua.RemoveAll(i => ctx.A[i].Match >= 0);
                ub.RemoveAll(i => ctx.B[i].Match >= 0);
            }
        }

        // ---- text helpers. InvariantCulture throughout: the results window finds a row's
        // position with a regex that expects "." decimals, whatever Windows is set to. ----

        private static string F2(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
        private static string F3(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

        // "(x.xx, y.yy, z.zz)" is the exact shape DiffLineVM turns into a Go button, and it
        // takes the FIRST one on the line - so this is always the position worth jumping to,
        // and every other coordinate on the line is printed in a shape that cannot match.
        private static string GoPos(Ent e) => "(" + F2(e.X) + ", " + F2(e.Y) + ", " + F2(e.Z) + ")";
        private static string PlainPos(Ent e) => e.HasPos ? F3(e.X) + " / " + F3(e.Y) + " / " + F3(e.Z) : "no position";

        private static string Where(List<Ent> list, int index)
        {
            var e = list[index];
            if (e.Depth == 0) return e.HasPos ? GoPos(e) : "(no position)";
            var root = list[e.Root];
            return "inside " + root.Display + " " + (root.HasPos ? GoPos(root) : "(no position)") + ", local " + PlainPos(e);
        }

        private static void ReportUnmatched(Ctx ctx, List<Ent> list, int index, Kind kind)
        {
            var e = list[index];
            var text = (kind == Kind.Removed ? "REMOVED  " : "ADDED  ") + e.Display + "  " + Where(list, index);
            if (e.Descendants > 0) text += "  (+" + e.Descendants + " child entit" + (e.Descendants == 1 ? "y" : "ies") + ")";
            ctx.R.Changes.Add(new Change { Primary = kind, Text = text });
            if (kind == Kind.Removed) { ctx.R.Removed++; ctx.R.RemovedInside += e.Descendants; }
            else { ctx.R.Added++; ctx.R.AddedInside += e.Descendants; }
        }

        private static bool NumbersEqual(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > Epsilon) return false;
            return true;
        }

        // The file's euler triple is NOT unique. At a pitch of +-90 degrees (1.571 - every
        // upright plank and log in a scene) the other two angles collapse into one, and the
        // editor re-derives them on save: the same entity reads (1.571, -2.995, -2.788) in one
        // backup and (1.571, 0.147, 0.354) in the next. Comparing the numbers reported 91 false
        // "rotated" rows between two real backups seven minutes apart. So eulers are turned
        // into a rotation matrix first and the MATRICES are compared.
        //
        // Order Y * X * Z, worked out from those same pairs: at x = +pi/2 they agree on (y - z),
        // at x = -pi/2 on (y + z), which is what that order collapses to. Whether the engine
        // means this matrix or its transpose makes no difference to an equality test.
        private static double[] EulerToMatrix(double[] e)
        {
            double cx = Math.Cos(e[0]), sx = Math.Sin(e[0]);
            double cy = Math.Cos(e[1]), sy = Math.Sin(e[1]);
            double cz = Math.Cos(e[2]), sz = Math.Sin(e[2]);
            // Ry(y) * Rx(x) * Rz(z)
            return new[]
            {
                cy * cz + sy * sx * sz,   -cy * sz + sy * sx * cz,   sy * cx,
                cx * sz,                  cx * cz,                   -sx,
                -sy * cz + cy * sx * sz,  sy * sz + cy * sx * cz,    cy * cx,
            };
        }

        // Three-decimal eulers put up to ~0.002 of rounding into a matrix element, so the
        // tolerance is wider than the position one. 0.004 is about a quarter of a degree.
        private const double RotationEpsilon = 0.004;

        private static bool MatricesEqual(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > RotationEpsilon) return false;
            return true;
        }

        // null = equal. Otherwise the word to print.
        private static string CompareRotation(Ent a, Ent b)
        {
            if (string.Equals(a.RotRaw, b.RotRaw, StringComparison.Ordinal) && a.RotIsMatrix == b.RotIsMatrix) return null;
            var ra = ParseNumbers(a.RotRaw);
            var rb = ParseNumbers(b.RotRaw);

            bool aMatrix = a.RotRaw != null && a.RotIsMatrix;
            bool bMatrix = b.RotRaw != null && b.RotIsMatrix;
            if (aMatrix != bMatrix)
            {
                // The matrix form carries scale and shear as well, so it cannot be compared
                // with an euler triple without the scale convention. Say so instead of guessing.
                return "rotation rewritten (euler <-> matrix, may be the same orientation)";
            }
            if (aMatrix) return MatricesEqual(ra, rb) ? null : "rotated";

            // Both euler (a missing attribute is the identity rotation).
            if (ra == null || ra.Length < 3) ra = new[] { 0.0, 0.0, 0.0 };
            if (rb == null || rb.Length < 3) rb = new[] { 0.0, 0.0, 0.0 };
            return MatricesEqual(EulerToMatrix(ra), EulerToMatrix(rb)) ? null : "rotated";
        }

        private static string CompareScale(Ent a, Ent b)
        {
            if (string.Equals(a.ScaleRaw, b.ScaleRaw, StringComparison.Ordinal)) return null;
            var sa = ParseNumbers(a.ScaleRaw) ?? new[] { 1.0, 1.0, 1.0 };
            var sb = ParseNumbers(b.ScaleRaw) ?? new[] { 1.0, 1.0, 1.0 };
            if (NumbersEqual(sa, sb)) return null;
            return "scaled " + string.Join(" ", sa.Select(F3)) + " -> " + string.Join(" ", sb.Select(F3));
        }

        private static string FactorText(string factor)
        {
            if (string.IsNullOrEmpty(factor)) return "(default)";
            return uint.TryParse(factor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                ? "#" + v.ToString("X8", CultureInfo.InvariantCulture)
                : factor;
        }

        // Distinct "old -> new" transitions across the meshes both sides have, with how many
        // meshes each covers. A recolor or material swap normally hits every LOD of a part the
        // same way; one line per transition says that without five identical rows.
        private static List<string> MeshTransitions(List<MeshRec> a, List<MeshRec> b, Func<MeshRec, string> pick, Func<string, string> show)
        {
            var result = new List<string>();
            if (a == null || b == null) return result;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();

            bool sameShape = a.Count == b.Count;
            if (sameShape)
                for (int i = 0; i < a.Count; i++)
                    if (!string.Equals(a[i].Name, b[i].Name, StringComparison.Ordinal)) { sameShape = false; break; }

            Action<MeshRec, MeshRec> compare = (ma, mb) =>
            {
                var va = pick(ma); var vb = pick(mb);
                if (string.Equals(va ?? "", vb ?? "", StringComparison.Ordinal)) return;
                var k = show(va) + " -> " + show(vb);
                if (counts.TryGetValue(k, out var n)) counts[k] = n + 1;
                else { counts[k] = 1; order.Add(k); }
            };

            if (sameShape)
            {
                for (int i = 0; i < a.Count; i++) compare(a[i], b[i]);
            }
            else
            {
                var byName = new Dictionary<string, MeshRec>(StringComparer.Ordinal);
                foreach (var m in b) if (m.Name != null && !byName.ContainsKey(m.Name)) byName[m.Name] = m;
                foreach (var m in a)
                    if (m.Name != null && byName.TryGetValue(m.Name, out var other)) compare(m, other);
            }

            foreach (var k in order.Take(3))
                result.Add(counts[k] > 1 ? k + " (" + counts[k] + " meshes)" : k);
            if (order.Count > 3) result.Add("+" + (order.Count - 3) + " more");
            return result;
        }

        private static bool MeshListsDiffer(List<MeshRec> a, List<MeshRec> b)
        {
            int na = a?.Count ?? 0, nb = b?.Count ?? 0;
            if (na != nb) return true;
            for (int i = 0; i < na; i++)
                if (!string.Equals(a[i].Name, b[i].Name, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void ReportPair(Ctx ctx, int ia, int ib, bool differentOrigin)
        {
            var a = ctx.A[ia];
            var b = ctx.B[ib];
            var r = ctx.R;

            var notes = new List<string>();
            bool prefabRef = false, renamed = false, material = false, color = false, other = false;

            if (differentOrigin)
            {
                bool bothFromPrefabs = (a.Prefab ?? a.OldPrefab) != null && (b.Prefab ?? b.OldPrefab) != null;
                if (bothFromPrefabs) { prefabRef = true; notes.Add("prefab " + a.Origin + " -> " + b.Origin); }
                else { renamed = true; notes.Add("was " + a.Display); }
            }
            else
            {
                if ((a.Prefab != null) != (b.Prefab != null))
                {
                    prefabRef = true;
                    notes.Add(a.Prefab != null ? "prefab link broken (now baked into the scene)" : "prefab link restored");
                }
                else if (!string.Equals(a.Prefab ?? a.OldPrefab, b.Prefab ?? b.OldPrefab, StringComparison.Ordinal))
                {
                    prefabRef = true;
                    notes.Add("prefab name " + (a.Prefab ?? a.OldPrefab ?? "(none)") + " -> " + (b.Prefab ?? b.OldPrefab ?? "(none)"));
                }
                if (!string.Equals(a.Name ?? "", b.Name ?? "", StringComparison.Ordinal))
                {
                    renamed = true;
                    notes.Add("renamed from '" + (a.Name ?? "(no name)") + "'");
                }
            }

            double dist = Math.Sqrt(DistSq(a, b));
            bool moved = dist > Epsilon;
            var rot = CompareRotation(a, b);
            var scale = CompareScale(a, b);
            if (rot != null) notes.Add(rot);
            if (scale != null) notes.Add(scale);

            // Everything below compares CONTENT, which is meaningless across a prefab swap -
            // the two prefabs differ in every mesh by definition.
            if (!differentOrigin)
            {
                var mats = MeshTransitions(a.Meshes, b.Meshes, m => m.Material, s => string.IsNullOrEmpty(s) ? "(default)" : s);
                if (mats.Count > 0) { material = true; notes.Add("material " + string.Join(", ", mats)); }

                var colors = MeshTransitions(a.Meshes, b.Meshes, m => m.Factor, FactorText);
                var colors2 = MeshTransitions(a.Meshes, b.Meshes, m => m.Factor2, FactorText);
                if (colors.Count > 0) { color = true; notes.Add("color " + string.Join(", ", colors)); }
                if (colors2.Count > 0) { color = true; notes.Add("color 2 " + string.Join(", ", colors2)); }

                var what = new List<string>();
                var tagsA = a.Tags ?? new List<string>();
                var tagsB = b.Tags ?? new List<string>();
                var addedTags = tagsB.Except(tagsA).ToList();
                var removedTags = tagsA.Except(tagsB).ToList();
                if (addedTags.Count > 0 || removedTags.Count > 0)
                    what.Add("tags " + string.Join(" ", addedTags.Take(4).Select(t => "+" + t).Concat(removedTags.Take(4).Select(t => "-" + t)))
                             + (addedTags.Count + removedTags.Count > 8 ? " ..." : ""));
                if (MeshListsDiffer(a.Meshes, b.Meshes)) what.Add("mesh list (" + (a.Meshes?.Count ?? 0) + " -> " + (b.Meshes?.Count ?? 0) + ")");
                if (a.ScriptSig != b.ScriptSig) what.Add("scripts");
                if (a.PhysSig != b.PhysSig) what.Add("physics");
                if (a.LightSig != b.LightSig) what.Add("light");
                if (a.ComponentSig != b.ComponentSig) what.Add("components");
                if (a.MeshArgSig != b.MeshArgSig) what.Add("mesh arguments");
                if (a.FlagSig != b.FlagSig) what.Add("flags/visibility");
                if (a.OverrideSig != b.OverrideSig) what.Add("prefab child overrides");
                if (a.RestSig != b.RestSig) what.Add("other data");
                if (what.Count > 0) { other = true; notes.Add("changed: " + string.Join(", ", what)); }
            }

            bool rotScale = rot != null || scale != null;
            if (!(prefabRef || renamed || moved || rotScale || material || color || other)) return;

            if (prefabRef) r.PrefabRef++;
            if (renamed) r.Renamed++;
            if (moved) r.Moved++;
            else if (rotScale) r.RotScaled++;
            if (material) r.Material++;
            if (color) r.Color++;
            if (other) r.Other++;

            Kind primary = prefabRef ? Kind.PrefabRef
                         : renamed ? Kind.Renamed
                         : moved ? Kind.Moved
                         : rotScale ? Kind.RotScale
                         : material ? Kind.Material
                         : color ? Kind.Color
                         : Kind.Other;

            var sb = new StringBuilder();
            if (moved) sb.Append("MOVED ").Append(F2(dist)).Append(" m  ");
            sb.Append(b.Display).Append("  ").Append(Where(ctx.B, ib));
            if (moved) sb.Append("  was ").Append(PlainPos(a));
            if (notes.Count > 0) sb.Append("  - ").Append(string.Join("; ", notes));

            r.Changes.Add(new Change { Primary = primary, Text = sb.ToString(), SortKey = moved ? -dist : 0 });
        }

        // ============================================================
        // REPORT
        // ============================================================

        // maxRows <= 0 means everything (the file report). Otherwise the row budget is split
        // evenly across the sections that have anything in them, so one enormous section (a
        // mass recolor) cannot push every other kind of change off the list.
        public static List<string> BuildLines(Result r, int maxRows)
        {
            var lines = new List<string>
            {
                r.Summary,
                "FROM: " + r.FromLabel,
                "TO:   " + r.ToLabel,
                "Compares SAVED files only - unsaved editor changes are in neither. Go jumps to the position in the TO file (FROM for removed rows).",
            };
            if (r.AddedInside > 0 || r.RemovedInside > 0)
                lines.Add("Added/removed rows are top-most only: " + r.AddedInside + " more entit(y/ies) arrived inside added parents, "
                          + r.RemovedInside + " left inside removed ones.");
            if (r.SceneSettingsChanged)
                lines.Add("[i] Scene-level data outside the entity list (atmosphere, water, terrain layers, paths...) also differs - not itemized.");
            if (r.Changes.Count == 0)
            {
                lines.Add("");
                lines.Add("No entity was added, removed, moved or changed between these two files.");
                return lines;
            }

            int kindCount = SectionTitles.Length;
            var sections = new List<Change>[kindCount];
            foreach (var c in r.Changes)
                (sections[(int)c.Primary] ?? (sections[(int)c.Primary] = new List<Change>())).Add(c);

            int nonEmpty = sections.Count(s => s != null);
            int perSection = maxRows <= 0 ? int.MaxValue : Math.Max(5, maxRows / Math.Max(1, nonEmpty));

            for (int k = 0; k < kindCount; k++)
            {
                var section = sections[k];
                if (section == null) continue;
                if (k == (int)Kind.Moved)
                    section = section.OrderBy(c => c.SortKey).ToList();   // farthest first; stable for ties

                lines.Add("");
                lines.Add("=== " + SectionTitles[k] + " (" + section.Count + ") ===");
                int shown = Math.Min(section.Count, perSection);
                for (int i = 0; i < shown; i++) lines.Add(section[i].Text);
                if (shown < section.Count)
                    lines.Add("... " + (section.Count - shown) + " more not shown here - raise 'Show first' below, or Write Report on the Scene Diff panel for the full list.");
            }
            return lines;
        }
    }
}

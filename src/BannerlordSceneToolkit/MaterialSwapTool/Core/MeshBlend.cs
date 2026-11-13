using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using TaleWorlds.Engine;
using TaleWorlds.Engine.Options;
using TaleWorlds.Library;

namespace MaterialSwapTool.Core
{
    // Terrain mesh blend: the terrain layer's color bleeding onto the base of an entity that
    // touches the ground. This class answers "why is it not showing on this entity" and writes
    // the per-mesh tuning values. Everything here runs on a button press of the Mesh Blend panel
    // (F8 -> Mesh Blend); nothing is called from a tick.
    //
    // WHERE THE RULES COME FROM (game 1.5.7, Shaders\Sources, read 2026-11-12):
    //   terrain_mesh_blend_functions.rsh:4-7    the compile-time gate TERRAIN_MESH_BLEND_CAN_BE_USED
    //   terrain_mesh_blend_functions.rsh:26-33  what the mesh vector arguments mean
    //   terrain_mesh_blend_functions.rsh:56-75  blend layers at the pixel, summed weight
    //   terrain_mesh_blend_functions.rsh:88     height window: -0.1 m .. 1.0 + argument.x + noise
    //   terrain_mesh_blend_functions.rsh:113/129 layer index 0 never blends
    //   pbr_standart_pixel_functions.rsh:673-677 the call, inside if (g_is_stationary)
    //
    // WHAT IS NOT KNOWN and is reported as UNKNOWN rather than guessed:
    //   - g_is_stationary is a per-mesh value the engine fills in (definitions_shader_structs.rsh:95).
    //     Nothing in the managed API reads it. GameEntity.GetMobility() is the closest hint.
    //   - The paint weight of a terrain layer at a point. The managed API has a setter
    //     (SetNodeLayerWeightData) and no getter.
    //   - Which shader-quality option value turns SYSTEM_LOW_QUALITY_SHADERS on. 0 is assumed.
    public static class MeshBlend
    {
        public enum Verdict { Ok, Fail, Unknown, Info, Summary }

        public class Line
        {
            public Verdict Verdict;
            public string Text;
            public Line(Verdict v, string t) { Verdict = v; Text = t; }
        }

        // How many selected entities one Check reports on. The list is one row per condition per
        // entity, and Gauntlet builds a real widget for every row.
        public const int MaxCheckedEntities = 4;
        public const int MaxRows = 60;

        // Shaders whose gbuffer variant sets my_material_id to MATERIAL_ID_STANDART or
        // MATERIAL_ID_METALLIC, calls calculate_diffuse_ao_factor_standart_deferred, and does not
        // define one of the excluding symbols. Built by scanning Shaders\Sources\*_gbuffer.rs.
        // There is no shader called "pbr_standart": the standard one is pbr_shading.
        private static readonly HashSet<string> BlendCapableShaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pbr_shading", "pbr_metallic", "terrain_mesh_blend_shader", "pbr_noshadow", "pbr_unified_lod",
            "pbr_shading_multi", "pbr_shading_with_additive_lightmap", "pbr_decalled_shader",
            "metal_roughness", "triplanar_prototype", "vertex_color", "bridge_plank", "flora_bark",
            "fresnel_shader", "matte_shadow", "default_outline", "prt_probe", "ripple_animated",
            "standart_crowd", "sail", "ship_hull", "weapon_trail", "map_circle",
            "animated_flag_shader", "animated_simple_flag_shader", "banner_background_color",
            "arrow_deformer", "bow_deformer", "crossbow_deformer", "longbow_deformer",
            "pistol_crossbow_deformer", "quiver_deformer", "strap_deformer", "stone_pile_deformer",
            "rope_pile", "rope_pile_auto_curve", "rope_pile_baked",
            "gui_circular_progress_realtime", "gui_circular_progress_shader", "gui_color_and_stroke",
            "gui_horizontal_progress_shader", "gui_progess_shader",
        };

        // Same material ids, but the shader source rules the blend out by a define.
        private static readonly Dictionary<string, string> ExcludedShaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "pbr_dithered_color", "PBR_DITHERED_COLOR" },
            { "pbr_main_map", "USE_MAIN_MAP_SNOW (world map)" },
            { "outer_mesh", "OUTER_MESH_RENDERING" },
            { "horse", "STANDART_FOR_HORSE" },
        };

        // The only shaders tuning is offered on. The other blend-capable shaders read the same
        // two vector arguments for their own purposes (flag waving, bow bending, lightmap
        // strength, rope curves, progress bars), so writing blend values there would break them.
        private static readonly HashSet<string> TunableShaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pbr_shading", "pbr_metallic", "terrain_mesh_blend_shader",
        };

        // Material shader flags that make the standard shaders read the vector arguments for
        // something else (pbr_standart_vertex_functions.rsh:34-43, standart.rsh:897-902), or that
        // switch the blend off (use_decal_rendering, terrain_mesh_blend_functions.rsh:5).
        // "use_tableau_blending" is the spelling the engine itself passes to
        // GetMaterialShaderFlagMask; the other three are the same convention applied to the
        // macro names and are NOT confirmed. An unknown name is expected to return mask 0, which
        // reads as "flag not set".
        private static readonly string[] ArgumentConflictFlags = { "use_texture_scalers", "use_animated_texture_coords", "use_tableau_blending" };
        private const string DecalRenderingFlag = "use_decal_rendering";

        private static readonly Dictionary<string, ulong> FlagMaskCache = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);

        // ---------------------------------------------------------------- numbers

        private static string F(float v, string format = "0.###") => v.ToString(format, CultureInfo.InvariantCulture);

        // Invariant culture, comma accepted as the decimal separator. Returns false with a reason
        // on anything else - callers show the reason, they do not fall back to a default.
        public static bool TryParseNumber(string text, out float value, out string problem)
        {
            value = 0f;
            problem = null;
            var t = (text ?? "").Trim();
            if (t.Length == 0) { problem = "is empty"; return false; }
            t = t.Replace(',', '.');
            if (!float.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
                || float.IsNaN(value) || float.IsInfinity(value))
            {
                problem = "'" + text.Trim() + "' is not a number";
                value = 0f;
                return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- mesh walk

        public class MeshInfo
        {
            public GameEntity Root;        // the selected entity this mesh was reached from
            public GameEntity Owner;       // the entity that carries it (Root or a descendant)
            public Mesh Mesh;
            public string MeshName = "";
            public string MaterialName = "";
            public string ShaderName = "";  // normalized, "" when unreadable
            public bool BlendCapable;
            public bool Tunable;
            public string WhyNot = "";      // set when BlendCapable or Tunable is false
        }

        private static string NormalizeShaderName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var s = raw.Trim().Replace('\\', '/');
            int slash = s.LastIndexOf('/');
            if (slash >= 0) s = s.Substring(slash + 1);
            foreach (var ext in new[] { ".rs", ".fx", ".hlsl" })
                if (s.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - ext.Length);
            return s.ToLowerInvariant();
        }

        private static bool HasShaderFlag(Material material, Shader shader, string shaderName, string flag)
        {
            try
            {
                var key = shaderName + "|" + flag;
                if (!FlagMaskCache.TryGetValue(key, out var mask))
                {
                    mask = shader.GetMaterialShaderFlagMask(flag, false);
                    FlagMaskCache[key] = mask;
                    Log.Info($"[MeshBlend] shader '{shaderName}' flag '{flag}' mask=0x{mask:X}");
                }
                return mask != 0 && (material.GetShaderFlags() & mask) != 0;
            }
            catch (Exception ex)
            {
                Log.Warn($"[MeshBlend] reading shader flag '{flag}' on '{shaderName}' failed: {ex.Message}");
                return false;
            }
        }

        private static MeshInfo Describe(GameEntity root, GameEntity owner, Mesh mesh)
        {
            var info = new MeshInfo { Root = root, Owner = owner, Mesh = mesh };
            try { info.MeshName = mesh.Name ?? ""; } catch { }

            Material material = null;
            try { material = mesh.GetMaterial(); } catch (Exception ex) { Log.Warn("[MeshBlend] GetMaterial failed: " + ex.Message); }
            if (material == null) { info.WhyNot = "no material"; return info; }
            try { info.MaterialName = material.Name ?? ""; } catch { }

            Shader shader = null;
            try { shader = material.GetShader(); } catch (Exception ex) { Log.Warn("[MeshBlend] GetShader failed: " + ex.Message); }
            if (shader == null) { info.WhyNot = "shader could not be read"; return info; }
            try { info.ShaderName = NormalizeShaderName(shader.Name); } catch { }
            if (info.ShaderName.Length == 0) { info.WhyNot = "shader name could not be read"; return info; }

            if (ExcludedShaders.TryGetValue(info.ShaderName, out var define))
            {
                info.WhyNot = "shader " + info.ShaderName + " rules the blend out (" + define + ")";
                return info;
            }
            if (!BlendCapableShaders.Contains(info.ShaderName))
            {
                info.WhyNot = "shader " + info.ShaderName + " is not a standard or metallic shader";
                return info;
            }
            if (HasShaderFlag(material, shader, info.ShaderName, DecalRenderingFlag))
            {
                info.WhyNot = "material has the " + DecalRenderingFlag + " shader flag";
                return info;
            }
            info.BlendCapable = true;

            if (!TunableShaders.Contains(info.ShaderName))
            {
                info.WhyNot = "shader " + info.ShaderName + " uses the same values for its own effect";
                return info;
            }
            foreach (var flag in ArgumentConflictFlags)
            {
                if (HasShaderFlag(material, shader, info.ShaderName, flag))
                {
                    info.WhyNot = "material flag " + flag + " uses the same values";
                    return info;
                }
            }
            info.Tunable = true;
            return info;
        }

        // Every mesh (all LODs - a MetaMesh lists its LOD meshes as ordinary entries) on the
        // given entities and their descendants.
        public static List<MeshInfo> CollectMeshes(IEnumerable<GameEntity> roots)
        {
            var result = new List<MeshInfo>();
            var seen = new HashSet<UIntPtr>();
            foreach (var root in roots)
            {
                if (!EntitySelector.IsValidEntity(root)) continue;
                foreach (var entity in EntitySelector.EnumerateSelfAndDescendants(root))
                {
                    if (!EntitySelector.IsValidEntity(entity)) continue;
                    int count;
                    try { count = entity.MultiMeshComponentCount; } catch { continue; }
                    for (int m = 0; m < count; m++)
                    {
                        MetaMesh meta;
                        try { meta = entity.GetMetaMesh(m); } catch { continue; }
                        if (meta == null || !meta.IsValid) continue;
                        int meshCount;
                        try { meshCount = meta.MeshCount; } catch { continue; }
                        for (int i = 0; i < meshCount; i++)
                        {
                            Mesh mesh;
                            try { mesh = meta.GetMeshAtIndex(i); } catch { continue; }
                            if (mesh == null || mesh.Pointer == UIntPtr.Zero) continue;
                            if (!seen.Add(mesh.Pointer)) continue;
                            result.Add(Describe(root, entity, mesh));
                        }
                    }
                }
            }
            return result;
        }

        // ---------------------------------------------------------------- saved terrain layers

        public class LayerInfo
        {
            public int Index;
            public string Name = "";
            public bool Blend;
            public string BlendSeasons = "";   // "" when every season agrees
            public string Diffuse = "";
        }

        // Reads <layers><layer name=..><summer is_mesh_blend_layer=.. /> ... from the SAVED
        // scene.xscene. Each layer holds one element per season; a layer counts as a blend layer
        // when any season says so, and the seasons are named when they disagree.
        public static List<LayerInfo> ReadSavedLayers(out string path, out DateTime savedAt, out string problem)
        {
            problem = null;
            savedAt = DateTime.MinValue;
            path = null;
            var layers = new List<LayerInfo>();
            try
            {
                path = Backup.BackupManager.TryFindSceneFile("scene.xscene");
                if (path == null || !File.Exists(path)) { problem = "the saved scene.xscene was not found"; return layers; }
                savedAt = File.GetLastWriteTime(path);

                var settings = new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore };
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = XmlReader.Create(stream, settings))
                {
                    // Only the first <layers> block that directly holds <layer name=..> elements is
                    // read; the entity list before it is skipped node by node without building a DOM.
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element || reader.Name != "layers") continue;
                        if (reader.IsEmptyElement) continue;
                        using (var sub = reader.ReadSubtree())
                        {
                            var doc = new XmlDocument();
                            doc.Load(sub);
                            var nodes = doc.DocumentElement?.SelectNodes("layer");
                            if (nodes == null || nodes.Count == 0) continue;
                            // Some other block could also be called <layers>; the terrain one is
                            // the one whose layers carry the flag at all.
                            if (doc.DocumentElement.SelectSingleNode("layer/*[@is_mesh_blend_layer]") == null) continue;
                            int index = 0;
                            foreach (XmlNode node in nodes)
                            {
                                var info = new LayerInfo { Index = index++, Name = node.Attributes?["name"]?.Value ?? "" };
                                var on = new List<string>();
                                int seasons = 0;
                                foreach (XmlNode season in node.ChildNodes)
                                {
                                    if (season.NodeType != XmlNodeType.Element) continue;
                                    var attr = season.Attributes?["is_mesh_blend_layer"];
                                    if (attr == null) continue;
                                    seasons++;
                                    if (string.Equals(attr.Value, "true", StringComparison.OrdinalIgnoreCase))
                                    {
                                        on.Add(season.Name);
                                        if (info.Diffuse.Length == 0)
                                        {
                                            var tex = season.SelectSingleNode("textures/texture[@type='diffusemap']");
                                            info.Diffuse = tex?.Attributes?["name"]?.Value ?? "";
                                        }
                                    }
                                }
                                info.Blend = on.Count > 0;
                                if (on.Count > 0 && on.Count < seasons) info.BlendSeasons = string.Join("/", on);
                                layers.Add(info);
                            }
                            break;
                        }
                    }
                }
                if (layers.Count == 0) problem = "no terrain layers were found in the saved scene.xscene";
            }
            catch (Exception ex)
            {
                problem = "reading the saved scene.xscene failed: " + ex.Message;
                Log.Warn("[MeshBlend] " + problem);
            }
            return layers;
        }

        // ---------------------------------------------------------------- check

        public static List<Line> Check(List<GameEntity> selection)
        {
            var lines = new List<Line>();
            var scene = EntitySelector.CurrentScene;
            var failures = new List<string>();   // most likely cause first, in the order found
            var unknowns = new List<string>();

            // ---- scene-wide conditions, reported once

            // Shader quality. NativeOptions.GetConfig returns the option index; the options list
            // has three entries for ShaderQuality. That index 0 is "Low" and that Low is what
            // sets SYSTEM_LOW_QUALITY_SHADERS is an assumption.
            try
            {
                float quality = NativeOptions.GetConfig(NativeOptions.NativeOptionsType.ShaderQuality);
                if (quality < 0.5f)
                {
                    lines.Add(new Line(Verdict.Fail, $"Shader quality: option value {F(quality)} (assumed to be Low) - FAIL. Low shader quality switches mesh blend off everywhere. Raise it in the graphics options."));
                    failures.Add("shader quality looks like Low, which switches mesh blend off everywhere");
                }
                else
                    lines.Add(new Line(Verdict.Ok, $"Shader quality: option value {F(quality)} (0 is assumed to be Low) - OK"));
            }
            catch (Exception ex)
            {
                lines.Add(new Line(Verdict.Unknown, "Shader quality: could not be read - UNKNOWN. Check that Shader Quality in the graphics options is not Low."));
                unknowns.Add("shader quality");
                Log.Warn("[MeshBlend] shader quality read failed: " + ex.Message);
            }

            bool hasTerrain = false;
            try { hasTerrain = scene != null && scene.ContainsTerrain; } catch { }
            if (!hasTerrain)
            {
                lines.Add(new Line(Verdict.Fail, "Terrain: this scene reports no terrain - FAIL. Mesh blend takes its color from a terrain layer."));
                failures.Add("the scene has no terrain");
            }

            // Terrain layer flags from the saved file.
            var layers = ReadSavedLayers(out var xscenePath, out var savedAt, out var layerProblem);
            var blendLayers = layers.Where(l => l.Blend).ToList();
            var usable = blendLayers.Where(l => l.Index != 0).ToList();
            string savedNote = savedAt == DateTime.MinValue ? "" : " (from the last save, " + savedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "; unsaved layer edits are not seen)";
            if (layerProblem != null)
            {
                lines.Add(new Line(Verdict.Unknown, "Terrain layers: " + layerProblem + " - UNKNOWN. In the terrain layer settings, check that the layer under the entity has Mesh Blend ticked."));
                unknowns.Add("terrain layer flags");
            }
            else if (blendLayers.Count == 0)
            {
                lines.Add(new Line(Verdict.Fail, $"Terrain layers: none of the {layers.Count} layers is a mesh blend layer{savedNote} - FAIL. Tick Mesh Blend on the layer painted under the entity, then save."));
                failures.Add("no terrain layer is flagged as a mesh blend layer (in the saved scene)");
            }
            else if (usable.Count == 0)
            {
                lines.Add(new Line(Verdict.Fail, $"Terrain layers: only the first layer '{blendLayers[0].Name}' is a mesh blend layer{savedNote} - FAIL. The first (base) layer never blends; flag another layer and paint it under the entity."));
                failures.Add("only the first terrain layer is a mesh blend layer, and the first layer never blends");
            }
            else
            {
                var names = string.Join(", ", usable.Take(8).Select(l => $"{l.Index} '{l.Name}'" + (l.BlendSeasons.Length > 0 ? " (" + l.BlendSeasons + " only)" : "")));
                if (usable.Count > 8) names += $", +{usable.Count - 8} more";
                lines.Add(new Line(Verdict.Ok, $"Terrain layers: {usable.Count} mesh blend layer(s){savedNote}: {names} - OK"));
                lines.Add(new Line(Verdict.Unknown, "Paint under the entity: the paint weight at a point cannot be read from here - UNKNOWN. Check by hand that one of the layers above is painted where the entity meets the ground. Full weight gives the strongest blend."));
                unknowns.Add("whether a blend layer is painted under the entity");
                var textures = usable.Where(l => l.Diffuse.Length > 0).Select(l => l.Diffuse).Distinct().Take(6).ToList();
                if (textures.Count > 0)
                    lines.Add(new Line(Verdict.Info, "Blend layer diffuse textures: " + string.Join(", ", textures) + ". Not checked here; editor strings suggest (unconfirmed) 2048x2048 DXT1, and the same resolution on every blend layer."));
            }
            Log.Info($"[MeshBlend] check: xscene='{xscenePath}', layers={layers.Count}, blend={blendLayers.Count}, usable={usable.Count}, problem='{layerProblem}'");

            // ---- per entity

            var valid = selection.Where(EntitySelector.IsValidEntity).ToList();
            int shown = 0;
            foreach (var entity in valid)
            {
                if (shown >= MaxCheckedEntities) break;
                shown++;
                try { CheckEntity(scene, entity, hasTerrain, lines, failures, unknowns, valid.Count > 1); }
                catch (Exception ex)
                {
                    lines.Add(new Line(Verdict.Unknown, "Entity check failed: " + ex.Message));
                    Log.Error("[MeshBlend] entity check failed: " + ex);
                }
            }
            if (valid.Count > shown)
                lines.Add(new Line(Verdict.Info, $"{valid.Count - shown} more selected entit{(valid.Count - shown == 1 ? "y was" : "ies were")} not checked (limit {MaxCheckedEntities} per press)."));

            // ---- summary
            string summary;
            if (failures.Count > 0)
                summary = "Most likely cause: " + failures[0] + "." + (failures.Count > 1 ? $" ({failures.Count - 1} more FAIL line(s) above.)" : "");
            else if (unknowns.Count > 0)
                summary = "Nothing failed, but these could not be checked from here: " + string.Join("; ", unknowns.Distinct()) + ". If those are fine and it still does not blend, try Recalculate, then save and reload.";
            else
                summary = "All checks passed - if it still does not blend, try Recalculate, then save and reload.";
            lines.Add(new Line(Verdict.Summary, summary));

            if (lines.Count > MaxRows)
            {
                // Keep the summary; drop from the middle.
                var last = lines[lines.Count - 1];
                lines = lines.Take(MaxRows - 2).ToList();
                lines.Add(new Line(Verdict.Info, "(more lines were cut - the full list is in tool.log under [MeshBlend])"));
                lines.Add(last);
            }
            return lines;
        }

        private static string EntityLabel(GameEntity entity)
        {
            string name = "";
            try { name = entity.Name ?? ""; } catch { }
            if (name.Length == 0)
            {
                try { name = EntitySelector.TryGetFirstMetaMesh(entity)?.GetName() ?? ""; } catch { }
            }
            return name.Length == 0 ? "(unnamed)" : name;
        }

        private static void CheckEntity(Scene scene, GameEntity entity, bool hasTerrain, List<Line> lines,
                                        List<string> failures, List<string> unknowns, bool multi)
        {
            var label = EntityLabel(entity);
            lines.Add(new Line(Verdict.Info, "--- " + label + " ---"));
            string who = multi ? "'" + label + "': " : "";

            // 1. Materials / shaders.
            var meshes = CollectMeshes(new[] { entity });
            var capable = meshes.Where(m => m.BlendCapable).ToList();
            if (meshes.Count == 0)
            {
                lines.Add(new Line(Verdict.Fail, "Material: the entity and its children have no meshes - FAIL"));
                failures.Add(who + "the entity has no meshes");
            }
            else if (capable.Count == 0)
            {
                var sample = meshes[0];
                lines.Add(new Line(Verdict.Fail, $"Material: none of the {meshes.Count} mesh(es) can blend, e.g. {sample.MaterialName}: {sample.WhyNot} - FAIL. Only standard (pbr_shading) and metallic (pbr_metallic) style shaders blend."));
                failures.Add(who + "its materials do not use a shader that supports mesh blend");
            }
            else
            {
                var byMaterial = capable.GroupBy(m => m.MaterialName + " uses " + m.ShaderName).Select(g => g.Key).Take(3).ToList();
                var text = $"Material: {capable.Count} of {meshes.Count} mesh(es) can blend ({string.Join("; ", byMaterial)}{(byMaterial.Count < capable.GroupBy(m => m.MaterialName).Count() ? "; ..." : "")}) - OK";
                lines.Add(new Line(Verdict.Ok, text));
                var others = meshes.Where(m => !m.BlendCapable).GroupBy(m => m.MaterialName + ": " + m.WhyNot).Select(g => g.Key).Take(3).ToList();
                if (others.Count > 0)
                    lines.Add(new Line(Verdict.Info, "Meshes that will not blend: " + string.Join("; ", others)));
            }
            foreach (var m in meshes)
                Log.Info($"[MeshBlend] '{label}' mesh='{m.MeshName}' material='{m.MaterialName}' shader='{m.ShaderName}' capable={m.BlendCapable} tunable={m.Tunable} why='{m.WhyNot}'");

            // Skinned meshes are compiled out (VDECL_HAS_SKIN_DATA). A skeleton on the entity is
            // the only hint available.
            try
            {
                if (entity.Skeleton != null)
                    lines.Add(new Line(Verdict.Unknown, "Skinned: the entity has a skeleton - UNKNOWN. Skinned meshes never blend."));
            }
            catch { }

            // 2. Stationary.
            try
            {
                var mobility = entity.GetMobility();
                var root = EntitySelector.TopMostParent(entity);
                string rootNote = "";
                if (EntitySelector.IsValidEntity(root) && root.Pointer != entity.Pointer)
                {
                    try { rootNote = ", top parent is " + root.GetMobility(); } catch { }
                }
                string bodyNote = "";
                try
                {
                    var body = entity.BodyFlag;
                    if ((body & (BodyFlags.Dynamic | BodyFlags.Moveable)) != 0) bodyNote = ", body flags include " + (body & (BodyFlags.Dynamic | BodyFlags.Moveable));
                }
                catch { }
                if (mobility == GameEntity.Mobility.Stationary)
                {
                    lines.Add(new Line(Verdict.Unknown, $"Stationary: mobility is Stationary{rootNote}{bodyNote} - UNKNOWN (probably fine). The engine's own per-mesh stationary flag cannot be read; mobility is the nearest hint."));
                    unknowns.Add("the engine's stationary flag");
                }
                else
                {
                    lines.Add(new Line(Verdict.Fail, $"Stationary: mobility is {mobility}{rootNote}{bodyNote} - FAIL (likely). Only stationary meshes blend. Scripted or moving objects (doors, siege engines) are usually dynamic."));
                    failures.Add(who + "the entity is not stationary (mobility " + mobility + ")");
                }
            }
            catch (Exception ex)
            {
                lines.Add(new Line(Verdict.Unknown, "Stationary: mobility could not be read - UNKNOWN"));
                unknowns.Add("the engine's stationary flag");
                Log.Warn("[MeshBlend] mobility read failed: " + ex.Message);
            }

            // 3. Current values, and the blend distance they give.
            float maxDistance = 1.0f;
            var firstTunable = meshes.FirstOrDefault(m => m.Tunable);
            if (firstTunable != null)
            {
                try
                {
                    var a = firstTunable.Mesh.GetVectorArgument();
                    var b = firstTunable.Mesh.GetVectorArgument2();
                    maxDistance = meshes.Where(m => m.Tunable).Select(m => { try { return 1.0f + m.Mesh.GetVectorArgument().x; } catch { return 1.0f; } }).DefaultIfEmpty(1.0f).Max();
                    var text = $"Blend values on '{firstTunable.MeshName}': height {F(1.0f + a.x)} m, softness {F(a.y)}, noise amount {F(a.z)}, noise scale {F(a.w)}; advanced {F(b.x)}, {F(b.y)}, {F(b.z)}";
                    if (1.0f + a.x <= 0.0f)
                    {
                        lines.Add(new Line(Verdict.Fail, text + " - FAIL. A blend height of 0 or less leaves no room to blend."));
                        failures.Add(who + "the blend height on the mesh is 0 or less");
                    }
                    else
                        lines.Add(new Line(Verdict.Info, text));
                }
                catch (Exception ex)
                {
                    lines.Add(new Line(Verdict.Unknown, "Blend values: could not be read - UNKNOWN"));
                    Log.Warn("[MeshBlend] argument read failed: " + ex.Message);
                }
            }
            else if (capable.Count > 0)
                lines.Add(new Line(Verdict.Info, "Blend values: not read. The shader(s) here use the same values for their own effect, so tuning is not offered; the blend uses whatever is set."));

            // 4. Height above terrain.
            if (hasTerrain)
            {
                try { CheckHeight(scene, entity, maxDistance, lines, failures, unknowns, who); }
                catch (Exception ex)
                {
                    lines.Add(new Line(Verdict.Unknown, "Height: could not be measured (" + ex.Message + ") - UNKNOWN"));
                    unknowns.Add("height above the terrain");
                    Log.Warn("[MeshBlend] height check failed: " + ex);
                }
            }
        }

        // The shader blends a pixel whose height above the terrain is between -0.1 m and the
        // blend height. This measures the entity's world bounding box against the terrain at the
        // box center, its four corners and the entity origin, so it is a box test, not a per-
        // pixel one: a part that overhangs can pass here and still be too high where it matters.
        private static void CheckHeight(Scene scene, GameEntity entity, float maxDistance, List<Line> lines,
                                        List<string> failures, List<string> unknowns, string who)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            int boxes = 0;
            foreach (var e in EntitySelector.EnumerateSelfAndDescendants(entity))
            {
                if (!EntitySelector.IsValidEntity(e)) continue;
                Vec3 lo, hi;
                try { lo = e.GlobalBoxMin; hi = e.GlobalBoxMax; } catch { continue; }
                if (float.IsNaN(lo.z) || float.IsNaN(hi.z) || float.IsInfinity(lo.z) || float.IsInfinity(hi.z)) continue;
                if (hi.x < lo.x || hi.y < lo.y || hi.z < lo.z) continue;
                if (hi.x - lo.x > 100000f || hi.y - lo.y > 100000f || hi.z - lo.z > 100000f) continue;   // uninitialized box
                if (hi.x == lo.x && hi.y == lo.y && hi.z == lo.z) continue;                              // empty helper
                boxes++;
                minX = Math.Min(minX, lo.x); minY = Math.Min(minY, lo.y); minZ = Math.Min(minZ, lo.z);
                maxX = Math.Max(maxX, hi.x); maxY = Math.Max(maxY, hi.y); maxZ = Math.Max(maxZ, hi.z);
            }
            if (boxes == 0)
            {
                lines.Add(new Line(Verdict.Unknown, "Height: the entity reports no usable bounding box - UNKNOWN. Check by eye that its base is within about 1 m of the ground."));
                unknowns.Add("height above the terrain");
                return;
            }

            var points = new List<Vec2>
            {
                new Vec2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f),
                new Vec2(minX, minY), new Vec2(minX, maxY), new Vec2(maxX, minY), new Vec2(maxX, maxY),
            };
            try { var o = entity.GlobalPosition; points.Add(new Vec2(o.x, o.y)); } catch { }

            float bestGap = float.MaxValue;      // lowest point minus terrain, closest to the window
            float highestTerrain = float.MinValue, lowestTerrain = float.MaxValue;
            bool anyInWindow = false;
            int sampled = 0;
            foreach (var p in points)
            {
                float h;
                try { h = scene.GetTerrainHeight(p, true); } catch { continue; }
                if (float.IsNaN(h) || float.IsInfinity(h)) continue;
                sampled++;
                highestTerrain = Math.Max(highestTerrain, h);
                lowestTerrain = Math.Min(lowestTerrain, h);
                float gap = minZ - h;
                if (Math.Abs(gap) < Math.Abs(bestGap)) bestGap = gap;
                // Some part of the box is inside [h - 0.1, h + maxDistance] here.
                if (minZ < h + maxDistance && maxZ > h - 0.1f) anyInWindow = true;
            }
            if (sampled == 0)
            {
                lines.Add(new Line(Verdict.Unknown, "Height: the terrain height under the entity could not be read - UNKNOWN"));
                unknowns.Add("height above the terrain");
                return;
            }

            string measured = $"lowest point {F(minZ, "0.00")}, terrain under it {F(lowestTerrain, "0.00")} to {F(highestTerrain, "0.00")} (lowest point is {F(Math.Abs(bestGap), "0.00")} m {(bestGap >= 0 ? "above" : "below")} the nearest sample)";
            if (anyInWindow)
                lines.Add(new Line(Verdict.Ok, $"Height: {measured} - OK. Part of the bounding box is inside the blend band (0.1 m below to {F(maxDistance, "0.##")} m above the terrain). This is a box test; an overhanging part can still be too high."));
            else if (maxZ <= lowestTerrain - 0.1f)
            {
                lines.Add(new Line(Verdict.Fail, $"Height: {measured} - FAIL. The whole entity is more than 0.1 m under the terrain."));
                failures.Add(who + "the entity is completely under the terrain");
            }
            else
            {
                lines.Add(new Line(Verdict.Fail, $"Height: {measured} - FAIL. The base must be within about {F(maxDistance, "0.##")} m above the terrain. Lower the entity or raise Blend height."));
                failures.Add(who + "the entity is too high above the terrain (" + F(Math.Abs(bestGap), "0.00") + " m; the blend reaches " + F(maxDistance, "0.##") + " m)");
            }
        }

        // ---------------------------------------------------------------- recalculate

        private const string RecalcCommandTail = "recalculate_mesh_blend";
        private static string _recalcCommand;   // full name once found, kept for the session

        // Asks the engine's own command list for the full name ("<category>.recalculate_mesh_blend")
        // instead of guessing the category. The exact behavior of the query (what it returns past
        // the last match, whether it matches on the full name or on any part) is not documented,
        // so this probes a few ways, stops on empty or repeated answers, and logs what it saw.
        private static string FindRecalcCommand(out string detail)
        {
            if (_recalcCommand != null) { detail = "found earlier this session"; return _recalcCommand; }

            var prefixes = new List<string> { RecalcCommandTail, "recalculate", "" };
            prefixes.AddRange(new[] { "editor.", "terrain.", "scene.", "mesh.", "rendering.", "resource.", "debug.", "edit_mode.", "engine." });
            for (char c = 'a'; c <= 'z'; c++) prefixes.Add(c.ToString());

            const int maxPerPrefix = 400;
            int calls = 0;
            var sampleLog = new List<string>();
            foreach (var prefix in prefixes)
            {
                string previous = null;
                for (int index = 0; index < maxPerPrefix; index++)
                {
                    string answer;
                    try { answer = Utilities.GetPossibleCommandLineStartingWith(prefix, index); }
                    catch (Exception ex)
                    {
                        Log.Warn($"[MeshBlend] command query '{prefix}'[{index}] threw: {ex.Message}");
                        break;
                    }
                    calls++;
                    if (string.IsNullOrWhiteSpace(answer) || answer == previous) break;
                    previous = answer;
                    if (sampleLog.Count < 12) sampleLog.Add($"'{prefix}'[{index}] -> '{(answer.Length > 80 ? answer.Substring(0, 80) + "..." : answer)}'");

                    // One command per answer is expected; split anyway in case a list comes back.
                    foreach (var token in answer.Split(new[] { ' ', '\t', '\r', '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (token.IndexOf(RecalcCommandTail, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        _recalcCommand = token.Trim();
                        Log.Info($"[MeshBlend] console command found: '{_recalcCommand}' (query '{prefix}', index {index}, {calls} call(s)).");
                        detail = "found with query '" + prefix + "'";
                        return _recalcCommand;
                    }
                }
            }
            Log.Warn($"[MeshBlend] no console command containing '{RecalcCommandTail}' was found in {calls} query call(s). First answers: {string.Join(" | ", sampleLog)}");
            detail = calls + " query call(s) returned no match";
            return null;
        }

        public static string Recalculate()
        {
            var command = FindRecalcCommand(out var detail);
            if (command == null)
                return "Recalculate: the editor did not list a '" + RecalcCommandTail + "' console command (" + detail + "). Nothing was run. The first answers of the command list are in tool.log under [MeshBlend].";

            Log.Info("[MeshBlend] running console command '" + command + "'.");
            string answer;
            try { answer = Utilities.ExecuteCommandLineCommand(command); }
            catch (Exception ex)
            {
                Log.Error("[MeshBlend] console command threw: " + ex);
                return "Recalculate: '" + command + "' failed: " + ex.Message;
            }
            Log.Info("[MeshBlend] console command returned: '" + answer + "'");
            answer = (answer ?? "").Trim();
            if (answer.Length > 300) answer = answer.Substring(0, 300) + "...";
            return "Recalculate: ran '" + command + "'. The editor answered: " + (answer.Length == 0 ? "(nothing)" : answer);
        }

        // ---------------------------------------------------------------- tuning

        // A null component means "leave this one as it is on each mesh".
        public class Values
        {
            public float? X, Y, Z, W;        // g_mesh_vector_argument (X is the offset, not the height)
            public float? X2, Y2, Z2;        // g_mesh_vector_argument_2 (its w is never written)
            public bool TouchesFirst => X.HasValue || Y.HasValue || Z.HasValue || W.HasValue;
            public bool TouchesSecond => X2.HasValue || Y2.HasValue || Z2.HasValue;
        }

        private class UndoEntry
        {
            public Mesh Mesh;
            public Vec3 First, Second;
            public bool WroteFirst, WroteSecond;
        }

        private class UndoStep
        {
            public string Label;
            public List<UndoEntry> Entries = new List<UndoEntry>();
        }

        private const int MaxUndoSteps = 20;
        private const int MaxMeshesPerApply = 20000;
        private static readonly List<UndoStep> UndoStack = new List<UndoStep>();

        // null = not established this session. Vec3 carries a w and the native getter returns the
        // struct by value, so w is expected to come through, but that is only proven once a
        // written w reads back.
        private static bool? _fourthComponentReadable;

        public static int UndoDepth => UndoStack.Count;

        public static void OnSceneChanged(string reason)
        {
            if (UndoStack.Count > 0) Log.Info($"[MeshBlend] dropping {UndoStack.Count} undo step(s): {reason}");
            UndoStack.Clear();
        }

        // First tunable mesh of the selection, for "Read from selection".
        public static bool TryRead(List<GameEntity> selection, out Vec3 first, out Vec3 second, out string message)
        {
            first = Vec3.Zero; second = Vec3.Zero;
            var meshes = CollectMeshes(selection);
            var mesh = meshes.FirstOrDefault(m => m.Tunable);
            if (mesh == null)
            {
                message = meshes.Count == 0
                    ? "Nothing to read - the selection has no meshes."
                    : "Nothing to read - none of the " + meshes.Count + " mesh(es) can be tuned (" + Reasons(meshes) + ").";
                return false;
            }
            try
            {
                first = mesh.Mesh.GetVectorArgument();
                second = mesh.Mesh.GetVectorArgument2();
            }
            catch (Exception ex)
            {
                message = "Reading the values failed: " + ex.Message;
                Log.Error("[MeshBlend] read failed: " + ex);
                return false;
            }
            if (first.w != 0f && _fourthComponentReadable == null) _fourthComponentReadable = true;
            Log.Info($"[MeshBlend] read '{mesh.MeshName}' ({mesh.MaterialName}): arg=({F(first.x)}, {F(first.y)}, {F(first.z)}, {F(first.w)}) arg2=({F(second.x)}, {F(second.y)}, {F(second.z)}, {F(second.w)})");
            int tunable = meshes.Count(m => m.Tunable);
            message = $"Read from mesh '{mesh.MeshName}' ({mesh.MaterialName}) on '{EntityLabel(mesh.Owner)}'. {tunable} of {meshes.Count} mesh(es) in the selection can be tuned."
                      + (_fourthComponentReadable == null ? " Noise scale read as " + F(first.w) + "; whether the editor reports that value correctly is not confirmed yet." : "");
            return true;
        }

        private static string Reasons(List<MeshInfo> meshes) =>
            string.Join("; ", meshes.Where(m => !m.Tunable).GroupBy(m => m.WhyNot).Select(g => g.Key).Take(2));

        public static string Apply(List<GameEntity> selection, Values values, string label)
        {
            if (!values.TouchesFirst && !values.TouchesSecond)
                return "Nothing to apply - every field is empty. An empty field means: keep the current value.";

            var meshes = CollectMeshes(selection);
            var targets = meshes.Where(m => m.Tunable).ToList();
            if (targets.Count == 0)
                return meshes.Count == 0
                    ? "Nothing changed - the selection has no meshes."
                    : "Nothing changed - none of the " + meshes.Count + " mesh(es) can be tuned (" + Reasons(meshes) + ").";
            if (targets.Count > MaxMeshesPerApply)
                return $"Nothing changed - {targets.Count} meshes is over the limit of {MaxMeshesPerApply} per press. Select fewer entities.";

            var step = new UndoStep { Label = label };
            int failed = 0;
            bool fourthMismatch = false, otherMismatch = false;
            foreach (var target in targets)
            {
                try
                {
                    var a = target.Mesh.GetVectorArgument();
                    var b = target.Mesh.GetVectorArgument2();
                    var entry = new UndoEntry { Mesh = target.Mesh, First = a, Second = b };

                    if (values.TouchesFirst)
                    {
                        float x = values.X ?? a.x, y = values.Y ?? a.y, z = values.Z ?? a.z, w = values.W ?? a.w;
                        Log.Info($"[MeshBlend] {label}: '{target.MeshName}' on '{EntityLabel(target.Owner)}' arg ({F(a.x)}, {F(a.y)}, {F(a.z)}, {F(a.w)}) -> ({F(x)}, {F(y)}, {F(z)}, {F(w)})");
                        target.Mesh.SetVectorArgument(x, y, z, w);
                        entry.WroteFirst = true;

                        var back = target.Mesh.GetVectorArgument();
                        if (Math.Abs(back.x - x) > 0.0005f || Math.Abs(back.y - y) > 0.0005f || Math.Abs(back.z - z) > 0.0005f) otherMismatch = true;
                        if (Math.Abs(back.w - w) > 0.0005f) fourthMismatch = true;
                        else if (w != 0f) _fourthComponentReadable = true;
                    }
                    if (values.TouchesSecond)
                    {
                        float x = values.X2 ?? b.x, y = values.Y2 ?? b.y, z = values.Z2 ?? b.z;
                        Log.Info($"[MeshBlend] {label}: '{target.MeshName}' arg2 ({F(b.x)}, {F(b.y)}, {F(b.z)}, {F(b.w)}) -> ({F(x)}, {F(y)}, {F(z)}, {F(b.w)} kept)");
                        target.Mesh.SetVectorArgument2(x, y, z, b.w);
                        entry.WroteSecond = true;

                        var back = target.Mesh.GetVectorArgument2();
                        if (Math.Abs(back.x - x) > 0.0005f || Math.Abs(back.y - y) > 0.0005f || Math.Abs(back.z - z) > 0.0005f) otherMismatch = true;
                    }
                    step.Entries.Add(entry);
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Warn($"[MeshBlend] {label}: mesh '{target.MeshName}' failed: {ex.Message}");
                }
            }

            if (step.Entries.Count > 0)
            {
                UndoStack.Add(step);
                while (UndoStack.Count > MaxUndoSteps) UndoStack.RemoveAt(0);
            }
            if (fourthMismatch) _fourthComponentReadable = false;

            int entityCount = step.Entries.Count == 0 ? 0 : targets.Select(t => t.Root.Pointer).Distinct().Count();
            int skipped = meshes.Count - targets.Count;
            var text = $"{label}: changed {step.Entries.Count} mesh(es) on {entityCount} entit{(entityCount == 1 ? "y" : "ies")} (all LODs)."
                       + (skipped > 0 ? $" {skipped} mesh(es) skipped: " + Reasons(meshes) + "." : "")
                       + (failed > 0 ? $" {failed} failed, see tool.log." : "")
                       + " Save the scene to keep it; Undo last change puts the old values back.";
            if (otherMismatch)
                text += " WARNING: the values did not read back as written, so the editor may not have taken them.";
            if (fourthMismatch)
                text += " WARNING: noise scale did not read back as written. The editor may not report that value, in which case an empty Noise scale field writes what was read (possibly 0) and Undo cannot restore it.";
            else if (values.TouchesFirst && !values.W.HasValue && _fourthComponentReadable == null)
                text += " Noise scale was kept as read; that read is not confirmed yet this session.";
            Log.Info("[MeshBlend] " + text);
            return text;
        }

        public static string UndoLast()
        {
            if (UndoStack.Count == 0) return "Nothing to undo - no mesh blend change has been made from this panel since the scene was opened.";
            var step = UndoStack[UndoStack.Count - 1];
            UndoStack.RemoveAt(UndoStack.Count - 1);

            int restored = 0, gone = 0;
            foreach (var entry in step.Entries)
            {
                try
                {
                    if (entry.Mesh == null || entry.Mesh.Pointer == UIntPtr.Zero) { gone++; continue; }
                    if (entry.WroteFirst) entry.Mesh.SetVectorArgument(entry.First.x, entry.First.y, entry.First.z, entry.First.w);
                    if (entry.WroteSecond) entry.Mesh.SetVectorArgument2(entry.Second.x, entry.Second.y, entry.Second.z, entry.Second.w);
                    restored++;
                }
                catch (Exception ex)
                {
                    gone++;
                    Log.Warn("[MeshBlend] undo: a mesh could not be restored: " + ex.Message);
                }
            }
            var text = $"Undid '{step.Label}': {restored} mesh(es) restored" + (gone > 0 ? $", {gone} could not be restored" : "")
                       + $". {UndoStack.Count} earlier change(s) left to undo."
                       + (_fourthComponentReadable == false ? " Noise scale may not have been restored (the editor does not report it)." : "");
            Log.Info("[MeshBlend] " + text);
            return text;
        }
    }
}

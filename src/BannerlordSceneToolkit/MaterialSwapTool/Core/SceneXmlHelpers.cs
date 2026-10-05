using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using MaterialSwapTool.Backup;

namespace MaterialSwapTool.Core
{
    // One placed <game_entity> node, wrapping the raw XElement plus parsed transform data - ported
    // from the PowerShell BSA tool's per-entity hashtable ($e.Type/.Name/.Prefab/.Node/.Pos/.Rot).
    public class SceneXmlEntity
    {
        public XElement Node;
        public string Prefab;
        public string Name;
        public string Label => Prefab ?? Name ?? "(anon)";
        public double[] Pos;
        public double[] Rot;
        public string PosStr;
    }

    public class Finding
    {
        public string Severity; // "ERROR", "WARNING", "INFO"
        public string Message;
    }

    // Shared helpers for every Scene Analyzer check that operates on the OFFLINE scene.xscene file
    // rather than the live GameEntity API. This is deliberate, not a style inconsistency with the
    // rest of this tool: several of the source checks (LOD substitution, duplicate detection,
    // bugged physics) specifically need to reach entities baked INSIDE other prefabs' saved XML,
    // which the live editor API doesn't expose the same way a raw scene.xscene descendant search
    // does. Ported from BannerlordSceneAnalyzer's Analyze-BannerlordScene.ps1 (local reference
    // project) - reusing its verified detection logic and curated reference data rather than
    // re-deriving from scratch.
    public static class SceneXmlHelpers
    {
        public static string FindScenePath()
        {
            var sceneName = EntitySelector.CurrentScene?.GetName();
            if (string.IsNullOrEmpty(sceneName)) return null;
            var dir = BackupManager.TryFindSceneDir(sceneName);
            if (dir == null) return null;
            var path = System.IO.Path.Combine(dir, "scene.xscene");
            return System.IO.File.Exists(path) ? path : null;
        }

        public static XDocument LoadScene(string path) => XDocument.Load(path);

        private static double[] ParseVec3(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(',');
            if (parts.Length < 3) return null;
            try
            {
                return new[] { double.Parse(parts[0].Trim()), double.Parse(parts[1].Trim()), double.Parse(parts[2].Trim()) };
            }
            catch { return null; }
        }

        private static SceneXmlEntity Wrap(XElement node)
        {
            var transform = node.Element("transform");
            string posStr = null;
            double[] pos = null, rot = null;
            if (transform != null)
            {
                posStr = (string)transform.Attribute("position");
                pos = ParseVec3(posStr);
                rot = ParseVec3((string)transform.Attribute("rotation_euler"));
            }
            return new SceneXmlEntity
            {
                Node = node,
                Prefab = (string)node.Attribute("prefab"),
                Name = (string)node.Attribute("name"),
                Pos = pos,
                Rot = rot,
                PosStr = posStr,
            };
        }

        // Direct children of <entities> only - these are the only entities with a world-space
        // transform in the same coordinate frame, so this is the correct (and only correct) set
        // for position/rotation-based duplicate comparison. A baked child's position is relative
        // to its parent prefab's frame, not world space - comparing those directly would be
        // comparing apples to oranges.
        public static List<SceneXmlEntity> CollectTopLevelEntities(XDocument doc)
        {
            var entitiesRoot = doc.Root?.Element("entities");
            if (entitiesRoot == null) return new List<SceneXmlEntity>();
            return entitiesRoot.Elements("game_entity").Select(Wrap).ToList();
        }

        // EVERY <game_entity> anywhere in the tree, including ones baked inside another prefab's
        // saved XML. Correct for tag/script/name existence checks, where coordinate frame doesn't
        // matter - a bugged-physics prefab or a required MP tag still causes the same problem
        // whether it's a top-level placed entity or baked three levels deep inside another one.
        public static List<SceneXmlEntity> CollectAllEntitiesRecursive(XDocument doc)
        {
            var entitiesRoot = doc.Root?.Element("entities");
            if (entitiesRoot == null) return new List<SceneXmlEntity>();
            return entitiesRoot.Descendants("game_entity").Select(Wrap).ToList();
        }

        public static bool HasTag(XElement entityNode, string tagName) =>
            entityNode.Element("tags")?.Elements("tag").Any(t => (string)t.Attribute("name") == tagName) ?? false;

        // Anywhere in this entity's own subtree (baked children included) - mirrors the source's
        // ".//tag[@name='X']" / ".//body_flag[@name='X']" style descendant lookups.
        public static bool HasDescendantTag(XElement entityNode, string tagName) =>
            entityNode.Descendants("tag").Any(t => (string)t.Attribute("name") == tagName);

        public static bool HasDescendantElementWithAttr(XElement entityNode, string elementName, string attrName, string attrValue) =>
            entityNode.Descendants(elementName).Any(e => (string)e.Attribute(attrName) == attrValue);

        public static HashSet<string> GetTagNames(XElement entityNode) =>
            new HashSet<string>(
                entityNode.Element("tags")?.Elements("tag").Select(t => (string)t.Attribute("name")) ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);

        // Adds a <tag name="X"/> to an entity (creating the <tags> element if needed), same
        // BSA_-prefixed marking convention as the source tool - lets you find flagged entities
        // later in the editor's own search-by-tag. Returns false if the tag already existed.
        public static bool AddTag(XElement entityNode, string tagName)
        {
            var tagsEl = entityNode.Element("tags");
            if (tagsEl == null)
            {
                tagsEl = new XElement("tags");
                entityNode.Add(tagsEl);
            }
            if (tagsEl.Elements("tag").Any(t => (string)t.Attribute("name") == tagName)) return false;
            tagsEl.Add(new XElement("tag", new XAttribute("name", tagName)));
            return true;
        }

        public static double Vec3Distance(double[] a, double[] b)
        {
            double dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // Smallest positive angular difference between two angles (radians), handling wraparound.
        public static double AngleDiff(double a, double b)
        {
            double diff = Math.Abs(a - b) % (2 * Math.PI);
            return diff > Math.PI ? (2 * Math.PI - diff) : diff;
        }
    }
}

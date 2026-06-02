using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Uice.VisualBinder.Editor
{
    /// <summary>Serializable description of one manual node group.</summary>
    [Serializable]
    internal class GroupData
    {
        public string id;
        public string title = "Group";
        public bool collapsed;
        public Rect proxyRect; // position of the collapsed proxy node (groups auto-wrap members when expanded)
        public List<string> memberKeys = new List<string>();
    }

    /// <summary>
    /// Per-root store for the graph layout (node positions + manual groups), persisted as a
    /// committable JSON file under the project so a whole team shares the same arrangement.
    /// One file per hierarchy root at <c>Assets/Editor/UiceVisualBinder/GraphLayouts/&lt;hash&gt;.json</c>,
    /// where the hash is derived from the root's <see cref="GlobalObjectId"/> (stable across machines
    /// and rename-proof). Existing per-user layouts in <see cref="EditorPrefs"/> are auto-migrated
    /// into a file the first time a root is opened.
    /// </summary>
    internal sealed class VisualBinderLayoutStore
    {
        [Serializable]
        private struct PositionEntry
        {
            public string key;
            public Rect rect;
        }

        [Serializable]
        private class LayoutFile
        {
            public string rootName;
            public string rootId;
            public List<PositionEntry> positions = new List<PositionEntry>();
            public List<GroupData> groups = new List<GroupData>();
        }

        // Legacy EditorPrefs shapes, kept only for one-time migration.
        [Serializable]
        private class LegacyLayoutTable
        {
            public List<PositionEntry> entries = new List<PositionEntry>();
        }

        [Serializable]
        private class LegacyGroupTable
        {
            public List<GroupData> groups = new List<GroupData>();
        }

        private const string FolderPath = "Assets/Editor/UiceVisualBinder/GraphLayouts";
        private const string LegacyLayoutPrefix = "UiceVisualBinder.Layout.";
        private const string LegacyGroupsPrefix = "UiceVisualBinder.Groups.";

        private readonly string relativePath;
        private readonly string rootName;
        private readonly string rootId;
        private readonly Dictionary<string, Rect> positions = new Dictionary<string, Rect>();
        private readonly List<GroupData> groups = new List<GroupData>();

        private VisualBinderLayoutStore(GameObject root, string rootKey)
        {
            rootId = rootKey;
            rootName = root != null ? root.name : string.Empty;
            relativePath = $"{FolderPath}/{Hash128.Compute(rootKey)}.json";
            Load();
        }

        /// <summary>Creates a store scoped to the given graph root, or null if no stable key exists.</summary>
        public static VisualBinderLayoutStore ForRoot(GameObject root)
        {
            string rootKey = GetKey(root);
            return string.IsNullOrEmpty(rootKey) ? null : new VisualBinderLayoutStore(root, rootKey);
        }

        /// <summary>A stable, machine-independent identity for a component or GameObject.</summary>
        public static string GetKey(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return null;
            }

            return GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
        }

        // ---- Node positions ------------------------------------------------------------------

        /// <summary>True when nothing has been stored for this root yet (fresh hierarchy).</summary>
        public bool IsEmpty => positions.Count == 0 && groups.Count == 0;

        public bool TryGet(string key, out Rect rect)
        {
            return positions.TryGetValue(key, out rect);
        }

        public void Set(string key, Rect rect)
        {
            if (!string.IsNullOrEmpty(key))
            {
                positions[key] = rect;
            }
        }

        // ---- Groups --------------------------------------------------------------------------

        public IReadOnlyList<GroupData> Groups => groups;

        public void Upsert(GroupData data)
        {
            int index = groups.FindIndex(g => g.id == data.id);
            if (index >= 0)
            {
                groups[index] = data;
            }
            else
            {
                groups.Add(data);
            }
        }

        public void Remove(string id)
        {
            groups.RemoveAll(g => g.id == id);
        }

        // ---- Persistence ---------------------------------------------------------------------

        public void Save()
        {
            var model = new LayoutFile { rootName = rootName, rootId = rootId };
            foreach (KeyValuePair<string, Rect> pair in positions)
            {
                model.positions.Add(new PositionEntry { key = pair.Key, rect = pair.Value });
            }
            model.groups.AddRange(groups);

            EnsureFolder(FolderPath);
            File.WriteAllText(ToAbsolute(relativePath), JsonUtility.ToJson(model, true));
            AssetDatabase.ImportAsset(relativePath);
        }

        private void Load()
        {
            positions.Clear();
            groups.Clear();

            string absolute = ToAbsolute(relativePath);
            if (File.Exists(absolute))
            {
                Apply(JsonUtility.FromJson<LayoutFile>(File.ReadAllText(absolute)));
                return;
            }

            MigrateFromEditorPrefs();
        }

        private void Apply(LayoutFile model)
        {
            if (model == null)
            {
                return;
            }

            if (model.positions != null)
            {
                foreach (PositionEntry entry in model.positions)
                {
                    if (!string.IsNullOrEmpty(entry.key))
                    {
                        positions[entry.key] = entry.rect;
                    }
                }
            }

            if (model.groups != null)
            {
                groups.AddRange(model.groups.Where(g => g != null && !string.IsNullOrEmpty(g.id)));
            }
        }

        private void MigrateFromEditorPrefs()
        {
            bool migrated = false;

            string layoutJson = EditorPrefs.GetString(LegacyLayoutPrefix + rootId, string.Empty);
            if (!string.IsNullOrEmpty(layoutJson))
            {
                var table = JsonUtility.FromJson<LegacyLayoutTable>(layoutJson);
                if (table?.entries != null)
                {
                    foreach (PositionEntry entry in table.entries)
                    {
                        if (!string.IsNullOrEmpty(entry.key))
                        {
                            positions[entry.key] = entry.rect;
                        }
                    }
                    migrated = true;
                }
            }

            string groupsJson = EditorPrefs.GetString(LegacyGroupsPrefix + rootId, string.Empty);
            if (!string.IsNullOrEmpty(groupsJson))
            {
                var table = JsonUtility.FromJson<LegacyGroupTable>(groupsJson);
                if (table?.groups != null)
                {
                    groups.AddRange(table.groups.Where(g => g != null && !string.IsNullOrEmpty(g.id)));
                    migrated = true;
                }
            }

            if (migrated)
            {
                Save(); // write the committable file so existing arrangements aren't lost
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] parts = folder.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static string ToAbsolute(string assetPath)
        {
            return Application.dataPath + assetPath.Substring("Assets".Length);
        }
    }
}

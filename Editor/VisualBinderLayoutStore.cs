using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Uice.VisualBinder.Editor
{
    /// <summary>
    /// Persists node positions per graph root so manual arrangement survives refreshes, domain
    /// reloads and editor restarts. Positions are keyed by each component's <see cref="GlobalObjectId"/>
    /// and stored as JSON in <see cref="EditorPrefs"/> under the root's id.
    /// </summary>
    internal sealed class VisualBinderLayoutStore
    {
        [Serializable]
        private struct Entry
        {
            public string key;
            public Rect rect;
        }

        [Serializable]
        private class Table
        {
            public List<Entry> entries = new List<Entry>();
        }

        private const string PrefPrefix = "UiceVisualBinder.Layout.";

        private readonly string prefKey;
        private readonly Dictionary<string, Rect> positions = new Dictionary<string, Rect>();

        private VisualBinderLayoutStore(string prefKey)
        {
            this.prefKey = prefKey;
            Load();
        }

        /// <summary>Creates a store scoped to the given graph root, or null if no stable key exists.</summary>
        public static VisualBinderLayoutStore ForRoot(GameObject root)
        {
            string rootKey = GetKey(root);
            return string.IsNullOrEmpty(rootKey) ? null : new VisualBinderLayoutStore(PrefPrefix + rootKey);
        }

        /// <summary>A stable, session-and-restart persistent identity for a component or GameObject.</summary>
        public static string GetKey(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return null;
            }

            return GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
        }

        /// <summary>True when no node positions have been stored for this root yet (fresh hierarchy).</summary>
        public bool IsEmpty => positions.Count == 0;

        public bool TryGet(string key, out Rect rect)
        {
            return positions.TryGetValue(key, out rect);
        }

        public void Set(string key, Rect rect)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            positions[key] = rect;
        }

        public void Save()
        {
            var table = new Table();
            foreach (KeyValuePair<string, Rect> pair in positions)
            {
                table.entries.Add(new Entry { key = pair.Key, rect = pair.Value });
            }

            EditorPrefs.SetString(prefKey, JsonUtility.ToJson(table));
        }

        private void Load()
        {
            positions.Clear();

            string json = EditorPrefs.GetString(prefKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var table = JsonUtility.FromJson<Table>(json);
            if (table?.entries == null)
            {
                return;
            }

            foreach (Entry entry in table.entries)
            {
                positions[entry.key] = entry.rect;
            }
        }
    }
}

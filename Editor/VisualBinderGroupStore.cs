using System;
using System.Collections.Generic;
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
    /// Persists manual node groups per graph root (title, membership, collapsed state, collapsed
    /// position) as JSON in <see cref="EditorPrefs"/>, mirroring <see cref="VisualBinderLayoutStore"/>.
    /// Member keys are component <see cref="GlobalObjectId"/>s (same keys the layout store uses).
    /// </summary>
    internal sealed class VisualBinderGroupStore
    {
        [Serializable]
        private class Table
        {
            public List<GroupData> groups = new List<GroupData>();
        }

        private const string PrefPrefix = "UiceVisualBinder.Groups.";

        private readonly string prefKey;
        private readonly Table table = new Table();

        private VisualBinderGroupStore(string prefKey)
        {
            this.prefKey = prefKey;
            Load();
        }

        public static VisualBinderGroupStore ForRoot(GameObject root)
        {
            string rootKey = VisualBinderLayoutStore.GetKey(root);
            return string.IsNullOrEmpty(rootKey) ? null : new VisualBinderGroupStore(PrefPrefix + rootKey);
        }

        public IReadOnlyList<GroupData> Groups => table.groups;

        public void Upsert(GroupData data)
        {
            int index = table.groups.FindIndex(g => g.id == data.id);
            if (index >= 0)
            {
                table.groups[index] = data;
            }
            else
            {
                table.groups.Add(data);
            }
        }

        public void Remove(string id)
        {
            table.groups.RemoveAll(g => g.id == id);
        }

        public void Save()
        {
            EditorPrefs.SetString(prefKey, JsonUtility.ToJson(table));
        }

        private void Load()
        {
            table.groups.Clear();

            string json = EditorPrefs.GetString(prefKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var loaded = JsonUtility.FromJson<Table>(json);
            if (loaded?.groups != null)
            {
                table.groups.AddRange(loaded.groups.Where(g => g != null && !string.IsNullOrEmpty(g.id)));
            }
        }
    }
}

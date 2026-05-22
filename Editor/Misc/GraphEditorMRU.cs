using System.Collections.Generic;
using UnityEditor;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Tiny EditorPrefs-backed MRU (most-recently-used) list of graph asset
    /// paths. Tracked by <see cref="GraphEditorWindow.SetAsset"/>; consumed
    /// by the toolbar dropdown so users can switch between recently-opened
    /// graphs without going back to the Project window.
    /// </summary>
    public static class GraphEditorMRU
    {
        const string PrefsKey = "CupkekGames.Graphs.MRU";
        const int MaxEntries = 12;
        const char Separator = '|';

        public static List<string> GetPaths()
        {
            var raw = EditorPrefs.GetString(PrefsKey, "");
            var result = new List<string>();
            if (string.IsNullOrEmpty(raw)) return result;

            foreach (var p in raw.Split(Separator))
                if (!string.IsNullOrEmpty(p)) result.Add(p);
            return result;
        }

        public static void Push(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            var list = GetPaths();
            list.Remove(assetPath);
            list.Insert(0, assetPath);
            if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);

            EditorPrefs.SetString(PrefsKey, string.Join(Separator.ToString(), list));
        }
    }
}

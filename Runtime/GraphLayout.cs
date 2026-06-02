using System.Collections.Generic;
using UnityEngine;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Sidecar layout for a <see cref="GraphAssetSO"/>: node canvas positions keyed by
    /// node GUID, stored in a SEPARATE asset (<c>Foo.layout.asset</c> next to
    /// <c>Foo.asset</c>) so position churn never collides in source control with
    /// structural edits to the graph. This is the single source of truth for positions —
    /// <see cref="GraphNodeSO.Position"/> is not serialized into the graph asset.
    ///
    /// <para>
    /// Parallel lists (not a <c>Dictionary</c>) so the YAML diffs one line per node and a
    /// guid's position is an isolated change.
    /// </para>
    /// </summary>
    public class GraphLayout : ScriptableObject
    {
        [SerializeField] private List<string> _guids = new List<string>();
        [SerializeField] private List<Vector2> _positions = new List<Vector2>();

        /// <summary>Read the stored position for <paramref name="guid"/>, if any.</summary>
        public bool TryGet(string guid, out Vector2 position)
        {
            int i = _guids.IndexOf(guid);
            if (i >= 0)
            {
                position = _positions[i];
                return true;
            }
            position = default;
            return false;
        }

        /// <summary>Store (or update) the position for <paramref name="guid"/>.</summary>
        public void Set(string guid, Vector2 position)
        {
            int i = _guids.IndexOf(guid);
            if (i >= 0) _positions[i] = position;
            else
            {
                _guids.Add(guid);
                _positions.Add(position);
            }
        }

        /// <summary>Drop entries whose guid is not in <paramref name="keep"/> (deleted nodes).</summary>
        public void PruneExcept(ICollection<string> keep)
        {
            for (int i = _guids.Count - 1; i >= 0; i--)
            {
                if (!keep.Contains(_guids[i]))
                {
                    _guids.RemoveAt(i);
                    _positions.RemoveAt(i);
                }
            }
        }
    }
}

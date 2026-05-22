using System.Collections.Generic;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Untyped global state shared across one execution of a graph. Plain
    /// dictionary wrapper — consumer graph runtimes (behaviour trees,
    /// dialogue runners, etc.) instantiate one per execution instance and
    /// thread it through node updates.
    ///
    /// Pair with <see cref="GraphFrame"/> when you also want branch-local
    /// scopes; the frame's <c>Globals</c> property points back here.
    /// </summary>
    public class GraphBlackboard
    {
        readonly Dictionary<string, object> _dict = new Dictionary<string, object>();

        /// <summary>Read by key. Returns <c>null</c> when absent.</summary>
        public object Get(string key)
        {
            return key != null && _dict.TryGetValue(key, out var v) ? v : null;
        }

        /// <summary>
        /// Typed read. Returns <c>true</c> only when the key is present
        /// AND the stored value is assignable to <typeparamref name="T"/>.
        /// </summary>
        public bool TryGet<T>(string key, out T value)
        {
            if (key != null && _dict.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public bool ContainsKey(string key) => key != null && _dict.ContainsKey(key);

        public void Set(string key, object value)
        {
            if (key == null) return;
            _dict[key] = value;
        }

        public bool Remove(string key) => key != null && _dict.Remove(key);

        public void Clear() => _dict.Clear();

        /// <summary>Indexer convenience — set is global.</summary>
        public object this[string key]
        {
            get => Get(key);
            set => Set(key, value);
        }
    }
}

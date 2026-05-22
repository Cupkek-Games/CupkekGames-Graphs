using System.Collections.Generic;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Tree-shaped scoped state. Each frame holds its own local dictionary
    /// plus a parent pointer; reads walk up the chain and fall back to
    /// <see cref="Globals"/>.
    ///
    /// <para>A composite or decorator that wants its children to see a
    /// scoped value (e.g. a target-selection node setting <c>"target"</c>
    /// for the branch below it) calls <see cref="Push"/> to get a child
    /// frame, writes via <see cref="SetLocal"/>, and passes the child
    /// frame down to <c>OnUpdate</c>. Sibling branches don't see those
    /// locals because they sit in different sub-trees of the frame
    /// hierarchy — solves the parallel-branch clobbering problem a flat
    /// push/pop stack would have.</para>
    ///
    /// <para>Reads via <see cref="TryGet{T}"/> /
    /// <see cref="ContainsKey"/> see the deepest match: locals on this
    /// frame, then locals on the parent chain, then <see cref="Globals"/>.</para>
    ///
    /// <para>Writes are explicit by intent: <see cref="SetLocal"/> for
    /// branch-only, <see cref="SetGlobal"/> for everyone. There is no
    /// ambiguous indexer setter.</para>
    /// </summary>
    public class GraphFrame
    {
        readonly Dictionary<string, object> _locals = new Dictionary<string, object>();
        readonly GraphFrame _parent;

        /// <summary>The execution-instance-wide blackboard this frame chains down to.</summary>
        public GraphBlackboard Globals { get; }

        public GraphFrame Parent => _parent;

        /// <summary>Construct a root frame backed by <paramref name="globals"/>.</summary>
        public GraphFrame(GraphBlackboard globals)
            : this(globals, null) { }

        GraphFrame(GraphBlackboard globals, GraphFrame parent)
        {
            Globals = globals ?? new GraphBlackboard();
            _parent = parent;
        }

        /// <summary>
        /// Create a child frame whose locals shadow this frame's view. Use
        /// when a node wants its descendants to see a scoped value without
        /// affecting sibling branches.
        /// </summary>
        public GraphFrame Push() => new GraphFrame(Globals, this);

        /// <summary>
        /// Walk locals up the parent chain, then fall back to
        /// <see cref="Globals"/>. Returns <c>true</c> only when the key is
        /// found AND the stored value is assignable to
        /// <typeparamref name="T"/>.
        /// </summary>
        public bool TryGet<T>(string key, out T value)
        {
            if (TryGetRaw(key, out object raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>True if any visible scope (locals, ancestor, or globals) contains the key.</summary>
        public bool ContainsKey(string key) => TryGetRaw(key, out _);

        /// <summary>Untyped read — null when absent in every visible scope.</summary>
        public object Get(string key)
        {
            return TryGetRaw(key, out var v) ? v : null;
        }

        /// <summary>Indexer convenience — read only (writes are explicit via SetLocal / SetGlobal).</summary>
        public object this[string key] => Get(key);

        /// <summary>Write to this frame's locals. Visible to descendants only.</summary>
        public void SetLocal(string key, object value)
        {
            if (key == null) return;
            _locals[key] = value;
        }

        /// <summary>Write to <see cref="Globals"/>. Visible to everything.</summary>
        public void SetGlobal(string key, object value)
        {
            Globals.Set(key, value);
        }

        /// <summary>Remove a key from this frame's locals (does not touch ancestors / globals).</summary>
        public bool RemoveLocal(string key) => key != null && _locals.Remove(key);

        bool TryGetRaw(string key, out object value)
        {
            if (key == null) { value = null; return false; }
            if (_locals.TryGetValue(key, out value)) return true;
            if (_parent != null) return _parent.TryGetRaw(key, out value);
            return Globals.TryGet<object>(key, out value);
        }
    }
}

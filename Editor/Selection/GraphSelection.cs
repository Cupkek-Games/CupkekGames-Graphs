using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Tracks which canvas elements are currently selected — nodes, edges,
    /// and groups. Each kind is tracked independently so consumers (delete,
    /// multi-drag, property panel) can act on the relevant subset without
    /// caring about the others.
    /// </summary>
    public class GraphSelection
    {
        readonly HashSet<NodeElement> _nodes = new HashSet<NodeElement>();
        readonly HashSet<EdgeElement> _edges = new HashSet<EdgeElement>();
        readonly HashSet<GroupElement> _groups = new HashSet<GroupElement>();

        public IReadOnlyCollection<NodeElement> Nodes => _nodes;
        public IReadOnlyCollection<EdgeElement> Edges => _edges;
        public IReadOnlyCollection<GroupElement> Groups => _groups;

        public int Count => _nodes.Count + _edges.Count + _groups.Count;
        public bool IsEmpty => Count == 0;

        public event Action Changed;

        // ─── Nodes ───

        public bool Contains(NodeElement node) => node != null && _nodes.Contains(node);

        public void Add(NodeElement node)
        {
            if (node == null) return;
            if (_nodes.Add(node)) { node.SetSelected(true); Raise(); }
        }

        public void Remove(NodeElement node)
        {
            if (node == null) return;
            if (_nodes.Remove(node)) { node.SetSelected(false); Raise(); }
        }

        public void Toggle(NodeElement node)
        {
            if (node == null) return;
            if (_nodes.Contains(node)) Remove(node);
            else Add(node);
        }

        public void SetTo(NodeElement node)
        {
            if (node == null) { Clear(); return; }
            if (_nodes.Count == 1 && _nodes.Contains(node) && _edges.Count == 0 && _groups.Count == 0) return;
            ClearInternal();
            _nodes.Add(node);
            node.SetSelected(true);
            Raise();
        }

        // ─── Edges ───

        public bool Contains(EdgeElement edge) => edge != null && _edges.Contains(edge);

        public void Add(EdgeElement edge)
        {
            if (edge == null) return;
            if (_edges.Add(edge)) { edge.SetSelected(true); Raise(); }
        }

        public void Remove(EdgeElement edge)
        {
            if (edge == null) return;
            if (_edges.Remove(edge)) { edge.SetSelected(false); Raise(); }
        }

        public void Toggle(EdgeElement edge)
        {
            if (edge == null) return;
            if (_edges.Contains(edge)) Remove(edge);
            else Add(edge);
        }

        public void SetTo(EdgeElement edge)
        {
            if (edge == null) { Clear(); return; }
            if (_edges.Count == 1 && _edges.Contains(edge) && _nodes.Count == 0 && _groups.Count == 0) return;
            ClearInternal();
            _edges.Add(edge);
            edge.SetSelected(true);
            Raise();
        }

        // ─── Groups ───

        public bool Contains(GroupElement group) => group != null && _groups.Contains(group);

        public void Add(GroupElement group)
        {
            if (group == null) return;
            if (_groups.Add(group)) { group.SetSelected(true); Raise(); }
        }

        public void Remove(GroupElement group)
        {
            if (group == null) return;
            if (_groups.Remove(group)) { group.SetSelected(false); Raise(); }
        }

        public void Toggle(GroupElement group)
        {
            if (group == null) return;
            if (_groups.Contains(group)) Remove(group);
            else Add(group);
        }

        public void SetTo(GroupElement group)
        {
            if (group == null) { Clear(); return; }
            if (_groups.Count == 1 && _groups.Contains(group) && _nodes.Count == 0 && _edges.Count == 0) return;
            ClearInternal();
            _groups.Add(group);
            group.SetSelected(true);
            Raise();
        }

        // ─── Clear ───

        public void Clear()
        {
            if (IsEmpty) return;
            ClearInternal();
            Raise();
        }

        void ClearInternal()
        {
            foreach (var n in _nodes) n.SetSelected(false);
            foreach (var e in _edges) e.SetSelected(false);
            foreach (var g in _groups) g.SetSelected(false);
            _nodes.Clear();
            _edges.Clear();
            _groups.Clear();
        }

        void Raise()
        {
            SyncUnitySelection();
            Changed?.Invoke();
        }

        /// <summary>
        /// Push the currently-selected node SOs to Unity's global Selection
        /// so the Inspector window shows them. Edges and groups aren't
        /// ScriptableObjects and have no inspector representation, so they
        /// don't participate — when only edges/groups are selected we
        /// leave Unity selection alone rather than clobbering whatever
        /// the user had open.
        /// </summary>
        void SyncUnitySelection()
        {
            if (_nodes.Count == 0)
            {
                // Only clear Unity selection when our nodes were what was
                // showing — otherwise we'd nuke an unrelated inspector
                // (e.g. user clicked away to inspect a project asset).
                if (UnityEditor.Selection.activeObject is GraphNodeSO)
                    UnityEditor.Selection.activeObject = null;
                return;
            }

            var objs = new UnityEngine.Object[_nodes.Count];
            int i = 0;
            foreach (var ne in _nodes)
                objs[i++] = ne?.Node;
            UnityEditor.Selection.objects = objs;
        }
    }
}

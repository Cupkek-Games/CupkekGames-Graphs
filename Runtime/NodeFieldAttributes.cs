using System;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Assigns a node's serialized field to a named, ordered section inside the
    /// canvas card body. Fields sharing a <see cref="Title"/> render together
    /// under one collapsible header; sections sort by <see cref="Order"/>
    /// (ascending, ties broken by first appearance). Fields with no
    /// <see cref="NodeGroupAttribute"/> render ungrouped at the top of the body,
    /// in declaration order.
    ///
    /// <para>
    /// Central + generic: any <see cref="GraphNodeSO"/> subclass can apply it;
    /// the body renderer (<c>NodeElement.BuildBody</c>) reads it via reflection.
    /// A node that uses no <see cref="NodeGroupAttribute"/> renders exactly as
    /// before — a flat field list — so this is purely opt-in.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class NodeGroupAttribute : Attribute
    {
        /// <summary>Section header text. Fields with the same title group together.</summary>
        public string Title { get; }

        /// <summary>Sort key across sections (ascending). Ties keep first-seen order.</summary>
        public int Order { get; }

        public NodeGroupAttribute(string title, int order = 0)
        {
            Title = title;
            Order = order;
        }
    }

    /// <summary>
    /// Optional fine ordering WITHIN a <see cref="NodeGroupAttribute"/> section
    /// (or within the ungrouped top block). Lower sorts first; the default is
    /// the field's declaration order. Use only when declaration order isn't the
    /// order you want to show.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class NodeFieldOrderAttribute : Attribute
    {
        /// <summary>Sort key within the field's section (ascending).</summary>
        public int Order { get; }

        public NodeFieldOrderAttribute(int order) { Order = order; }
    }
}

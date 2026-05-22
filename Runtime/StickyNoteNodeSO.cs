using System.Collections.Generic;
using UnityEngine;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Built-in node type for designer comments. No ports — renders as a
    /// repositionable multi-line text card on the canvas. Ships with the
    /// foundation since every consumer benefits from in-graph annotation.
    /// </summary>
    public class StickyNoteNodeSO : GraphNodeSO
    {
        [TextArea(3, 10)]
        public string Text = "Note...";

        public Color BackgroundColor = new Color(0.95f, 0.85f, 0.40f);

        public override string DisplayTitle => "Note";
        public override IReadOnlyList<GraphPortDef> InputPorts => GraphPortDef.None;
        public override IReadOnlyList<GraphPortDef> OutputPorts => GraphPortDef.None;

        // StickyNoteElement renders its own multi-line text field; skip the
        // base auto-inspector so we don't get a redundant Text field below
        // the textarea.
        public override bool ShowInlineProperties => false;
    }
}

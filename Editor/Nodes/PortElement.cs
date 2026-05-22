using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Small visual stub representing one input or output port on a node.
    /// Anchored to the node's edge (half-outside the card). For now, ports
    /// are purely visual; <see cref="PortDragManipulator"/> in a later piece
    /// makes them drag-source for new connections.
    /// </summary>
    public class PortElement : VisualElement
    {
        public const float PortSize = 12f;

        static readonly Color InputColor  = GraphTheme.PortInput;
        static readonly Color OutputColor = GraphTheme.PortOutput;
        static readonly Color BorderColor = GraphTheme.Separator;

        public NodeElement OwnerNode { get; }
        public bool IsInput { get; }
        public GraphPortDef PortDef { get; }
        public int PortIndex { get; }

        public string PortId => PortDef?.Id;

        public PortElement(NodeElement node, bool isInput, GraphPortDef def, int index)
        {
            OwnerNode = node;
            IsInput = isInput;
            PortDef = def;
            PortIndex = index;

            AddToClassList("cgg-graph-port");
            AddToClassList(isInput ? "cgg-graph-port--input" : "cgg-graph-port--output");

            // Pickable: PortDragManipulator starts new-connection drags from
            // output ports; the canvas hit-tests input ports as drop targets
            // on drag release.
            pickingMode = PickingMode.Position;

            style.position = Position.Absolute;
            style.width = PortSize;
            style.height = PortSize;
            style.backgroundColor = isInput ? InputColor : OutputColor;

            float r = PortSize * 0.5f;
            style.borderTopLeftRadius = r;
            style.borderTopRightRadius = r;
            style.borderBottomLeftRadius = r;
            style.borderBottomRightRadius = r;

            style.borderTopWidth = 1f;
            style.borderBottomWidth = 1f;
            style.borderLeftWidth = 1f;
            style.borderRightWidth = 1f;
            style.borderTopColor = BorderColor;
            style.borderBottomColor = BorderColor;
            style.borderLeftColor = BorderColor;
            style.borderRightColor = BorderColor;

            this.AddManipulator(new PortDragManipulator());
        }
    }
}

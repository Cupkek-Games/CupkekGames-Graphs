using System;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// Categorises a <see cref="GraphNodeSO"/> subclass in the editor's
    /// node-creation search popup. Use slashes for nesting:
    /// <c>[GraphNodeCategory("Composites/Sequencer")]</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public class GraphNodeCategoryAttribute : Attribute
    {
        public string Path { get; }

        public GraphNodeCategoryAttribute(string path)
        {
            Path = path;
        }
    }
}

using CupkekGames.Data.Primitives;

namespace CupkekGames.Graphs
{
    /// <summary>
    /// One issue surfaced by <see cref="GraphAssetSO.Validate"/>; rendered in
    /// the editor's bottom footer with click-to-jump to the offending node
    /// or connection.
    /// </summary>
    public class GraphValidationIssue
    {
        public enum SeverityLevel
        {
            Info,
            Warning,
            Error,
        }

        public SeverityLevel Severity = SeverityLevel.Warning;
        public string Message;

        /// <summary>Optional — highlights this specific node in the canvas.</summary>
        public SerializedGuid TargetNodeGuid;

        /// <summary>Optional — highlights this specific connection in the canvas.</summary>
        public SerializedGuid TargetConnectionGuid;
    }
}

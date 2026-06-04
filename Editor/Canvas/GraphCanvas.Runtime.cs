using UnityEditor;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Play-mode runtime-overlay half of <see cref="GraphCanvas"/> — paints each
    /// node's live <see cref="GraphNodeRuntimeState"/> (glow + pill) from the bound
    /// asset's <see cref="GraphAssetSO.CreateRuntimeStateSource"/>. Event-driven (no
    /// poll): subscribes to the source's <c>Changed</c> and re-applies. Active only
    /// while <see cref="EditorApplication.isPlaying"/>; clears on exit-play / detach.
    /// </summary>
    public partial class GraphCanvas
    {
        IGraphRuntimeStateSource _runtimeSource;

        // Called once from the ctor.
        void HookRuntimeOverlay()
        {
            EditorApplication.playModeStateChanged += OnRuntimePlayModeChanged;
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                EditorApplication.playModeStateChanged -= OnRuntimePlayModeChanged;
                TeardownRuntimeSource();
            });
        }

        void OnRuntimePlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode) SetupRuntimeSource();
            else if (change == PlayModeStateChange.ExitingPlayMode) TeardownRuntimeSource();
        }

        // (Re)bind a source to the current asset. Also called after BindToAsset, so
        // switching graphs or opening the window mid-play picks up the overlay.
        void SetupRuntimeSource()
        {
            TeardownRuntimeSource();
            if (!EditorApplication.isPlaying || Asset == null) return;

            _runtimeSource = Asset.CreateRuntimeStateSource();
            if (_runtimeSource == null) return;

            _runtimeSource.Changed += ApplyRuntimeStates;
            ApplyRuntimeStates(); // initial paint
        }

        void TeardownRuntimeSource()
        {
            if (_runtimeSource != null)
            {
                _runtimeSource.Changed -= ApplyRuntimeStates;
                _runtimeSource.Dispose();
                _runtimeSource = null;
            }
            foreach (var ne in NodeElements) ne?.SetRuntimeState(null);
        }

        void ApplyRuntimeStates()
        {
            bool live = _runtimeSource != null && _runtimeSource.IsLive;
            foreach (var ne in NodeElements)
            {
                if (ne?.Node == null) continue;
                GraphNodeRuntimeState? s =
                    live && _runtimeSource.TryGetState(ne.Node, out var st) ? st : (GraphNodeRuntimeState?)null;
                ne.SetRuntimeState(s);
            }
        }
    }
}

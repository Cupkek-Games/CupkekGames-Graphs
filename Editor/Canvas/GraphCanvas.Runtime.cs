using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Play-mode runtime-overlay half of <see cref="GraphCanvas"/> — paints each
    /// node's live <see cref="GraphNodeRuntimeState"/> (glow + pill) from the bound
    /// asset's <see cref="GraphAssetSO.CreateRuntimeStateSource"/>. Event-driven:
    /// re-applies on the source's <c>Changed</c>. A continuous source
    /// (<see cref="IGraphRuntimePollable"/>) is ticked here so it needn't depend on
    /// <c>EditorApplication</c> itself; a multi-instance source
    /// (<see cref="IGraphRuntimeInstanceSelector"/>) gets a corner dropdown to pick
    /// which instance to inspect. Active only while in play mode; clears on exit.
    /// </summary>
    public partial class GraphCanvas
    {
        IGraphRuntimeStateSource _runtimeSource;
        PopupField<string> _instancePicker;

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
            // Continuous sources are ticked here (no EditorApplication dependency of
            // their own); they coalesce and raise Changed only when state changes.
            if (_runtimeSource is IGraphRuntimePollable)
                EditorApplication.update += PollRuntimeSource;

            ApplyRuntimeStates();
        }

        void TeardownRuntimeSource()
        {
            EditorApplication.update -= PollRuntimeSource;
            if (_runtimeSource != null)
            {
                _runtimeSource.Changed -= ApplyRuntimeStates;
                _runtimeSource.Dispose();
                _runtimeSource = null;
            }
            if (_instancePicker != null) { _instancePicker.RemoveFromHierarchy(); _instancePicker = null; }
            foreach (var ne in NodeElements) ne?.SetRuntimeState(null);
        }

        void PollRuntimeSource() => (_runtimeSource as IGraphRuntimePollable)?.Poll();

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
            RefreshInstancePicker(live);
        }

        // Show a dropdown to pick which live instance to inspect, for sources that
        // expose more than one (e.g. many agents running one behaviour-tree).
        void RefreshInstancePicker(bool live)
        {
            var sel = live ? _runtimeSource as IGraphRuntimeInstanceSelector : null;
            var labels = sel?.InstanceLabels;
            if (sel == null || labels == null || labels.Count <= 1)
            {
                if (_instancePicker != null) _instancePicker.style.display = DisplayStyle.None;
                return;
            }

            var choices = new List<string>(labels);
            int idx = Mathf.Clamp(sel.SelectedInstance, 0, choices.Count - 1);

            if (_instancePicker == null)
            {
                _instancePicker = new PopupField<string>(choices, idx)
                {
                    tooltip = "Which live instance's runtime state to show.",
                    style = { position = Position.Absolute, top = 8, left = 8, minWidth = 120 },
                };
                _instancePicker.RegisterValueChangedCallback(_ =>
                {
                    if (_runtimeSource is IGraphRuntimeInstanceSelector s)
                        s.SelectedInstance = _instancePicker.index; // → Changed → re-apply
                });
                _overlayLayer.Add(_instancePicker);
            }
            else
            {
                _instancePicker.style.display = DisplayStyle.Flex;
                if (!Same(_instancePicker.choices, choices)) _instancePicker.choices = choices;
                if (_instancePicker.index != idx) _instancePicker.SetValueWithoutNotify(choices[idx]);
            }
        }

        static bool Same(List<string> a, List<string> b)
        {
            if (a == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}

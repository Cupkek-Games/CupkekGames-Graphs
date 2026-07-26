using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Dockable editor window that hosts the graph canvas. Opened by
    /// double-clicking any <see cref="GraphAssetSO"/> in the Project window,
    /// from the Window menu, or by clicking the "Open in Graph Editor"
    /// button on the asset's Inspector.
    ///
    /// Layout (top → bottom):
    /// <list type="bullet">
    ///   <item>Toolbar — asset name, ping / fit / save buttons.</item>
    ///   <item>Main row — canvas (flex-grow) + right-side property panel.</item>
    ///   <item>Validation footer — issues from GraphAssetSO.Validate, hidden when empty.</item>
    ///   <item>Status bar — zoom %, node count, selection count.</item>
    /// </list>
    ///
    /// Domain graphs can specify a <see cref="GraphAssetSO.EditorCanvasType"/>
    /// to host themselves in a <see cref="GraphCanvas"/> subclass. The
    /// window rebuilds the layout when the canvas type changes.
    /// </summary>
    public class GraphEditorWindow : EditorWindow
    {
        // The on-disk asset the user opened. Never mutated during editing —
        // edits go to _workingAsset; Save flushes back here.
        [SerializeField] GraphAssetSO _currentAsset;

        // Transient deep clone of _currentAsset that the canvas actually
        // edits. Unity won't auto-flush this because it has no asset path
        // and is HideAndDontSave. Survives domain reload via Find on
        // _currentAsset (cloned fresh after reload). Destroyed on close.
        GraphAssetSO _workingAsset;

        // The descend path: [root, …, current]. The top is the bound asset and
        // _currentAsset mirrors it. Transient — rebuilt from _currentAsset on
        // domain reload (deeper descend history is not restored).
        readonly List<GraphAssetSO> _navStack = new List<GraphAssetSO>();

        GraphCanvas _canvas;
        GraphToolbar _toolbar;
        ValidationFooter _validationFooter;
        GraphStatusBar _statusBar;
        GraphInspectorPanel _inspectorPanel;

        // Persisted width of the drag-resizable docked inspector pane.
        const string InspectorWidthPrefKey = "CupkekGames.Graphs.GraphEditor.InspectorWidth";

        /// <summary>The on-disk asset the window is bound to.</summary>
        public GraphAssetSO CurrentAsset => _currentAsset;

        /// <summary>
        /// The asset the canvas is actually editing — a transient clone
        /// of <see cref="CurrentAsset"/>. Most consumers don't care;
        /// exposed for cases that need to identify the live editing
        /// surface (e.g. validators that need to find nodes by guid).
        /// </summary>
        public GraphAssetSO WorkingAsset => _workingAsset;

        public GraphCanvas Canvas => _canvas;

        /// <summary>
        /// Fires when the window's <c>hasUnsavedChanges</c> state flips.
        /// Toolbar buttons (Save / Discard) subscribe to update their
        /// enabled state without per-frame polling.
        /// </summary>
        public event Action DirtyStateChanged;

        void SetDirty(bool dirty)
        {
            if (hasUnsavedChanges == dirty) return;
            hasUnsavedChanges = dirty;
            DirtyStateChanged?.Invoke();
        }

        // ---------------------------------------------------------------
        // Entry points
        // ---------------------------------------------------------------

        [OnOpenAsset(1)]
        public static bool OnOpenAsset(EntityId instanceId, int line)
        {
            // Unity 6.6 alpha promoted the [OnOpenAsset] signature: int
            // is no longer accepted, must be EntityId. With an EntityId
            // in hand we can call EditorUtility.EntityIdToObject directly
            // — no reflection workaround needed (the deprecated path
            // was the implicit int->EntityId conversion, which we now
            // sidestep entirely).
            var obj = EditorUtility.EntityIdToObject(instanceId);
            if (obj is GraphAssetSO graph)
            {
                Open(graph);
                return true;
            }
            return false;
        }

        // Priority 200 — graph windows band, grouped with the Graph Runtime
        // Debugger (201). Sits between Package Manager (100) and the utility
        // windows (400+); the >10 gaps give Unity's auto-separators on both sides.
        [MenuItem("Tools/CupkekGames/Graphs/Graph Editor", false, 300)]
        public static void OpenEmpty() => Open(null);

        /// <summary>
        /// Focus an existing Graph Editor window (or create the default one)
        /// and bind it to <paramref name="asset"/>.
        /// </summary>
        public static GraphEditorWindow Open(GraphAssetSO asset)
        {
            var window = GetWindow<GraphEditorWindow>();
            window.SetAsset(asset);
            window.Show();
            window.Focus();
            return window;
        }

        /// <summary>
        /// Create a fresh Graph Editor window (does not reuse an existing
        /// one). Useful when the consumer wants two graphs visible at once.
        /// </summary>
        public static GraphEditorWindow OpenInNewWindow(GraphAssetSO asset)
        {
            var window = CreateInstance<GraphEditorWindow>();
            window.SetAsset(asset);
            window.Show();
            window.Focus();
            return window;
        }

        // ---------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------

        void OnEnable()
        {
            rootVisualElement.style.flexGrow = 1f;
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            // Build with whatever canvas type the serialised asset wants,
            // or the generic GraphCanvas if no asset is bound yet.
            BuildLayout(ResolveCanvasType(_currentAsset));

            // The serialized _currentAsset survives domain reloads — clone
            // a fresh working copy so the user doesn't lose their graph
            // when scripts recompile. Any unsaved working-copy edits from
            // before the reload are lost (working copy was transient).
            if (_currentAsset != null)
            {
                // Domain reload drops the transient nav stack; rebuild it as a
                // single level from the surviving on-disk asset.
                _navStack.Clear();
                _navStack.Add(_currentAsset);
                BindWorkingCopy();
            }

            UpdateTitle();
        }

        void OnDisable()
        {
            // Remember the inspector width before the window goes away.
            PersistInspectorWidth();

            // Working copy is transient and tied to this window — clean
            // up so its undo entries don't dangle and the next editor
            // session starts fresh.
            DestroyWorkingCopy();
        }

        // Save the current docked-inspector width (set by dragging the splitter) so it
        // restores next open. Reads the resolved width; guards the pre-layout NaN case.
        void PersistInspectorWidth()
        {
            if (_inspectorPanel == null) return;
            float w = _inspectorPanel.resolvedStyle.width;
            if (!float.IsNaN(w) && w > 50f)
                EditorPrefs.SetFloat(InspectorWidthPrefKey, w);
        }

        public void SetAsset(GraphAssetSO asset)
        {
            // Binding a (different) asset resets the whole descend stack.
            if (asset != _currentAsset && !ConfirmLeaveCurrentLevel()) return;

            _navStack.Clear();
            if (asset != null) _navStack.Add(asset);
            RebindTop();
        }

        /// <summary>
        /// Descend into a referenced sub-graph — pushes <paramref name="child"/> onto
        /// the nav stack and binds it. Wired to <see cref="GraphCanvas.DescendRequested"/>.
        /// </summary>
        public void Descend(GraphAssetSO child)
        {
            if (child == null || child == _currentAsset) return;
            if (!ConfirmLeaveCurrentLevel()) return;
            _navStack.Add(child);
            RebindTop();
        }

        /// <summary>Pop back to the parent graph. No-op at the root.</summary>
        public void Ascend()
        {
            if (_navStack.Count <= 1) return;
            if (!ConfirmLeaveCurrentLevel()) return;
            _navStack.RemoveAt(_navStack.Count - 1);
            RebindTop();
        }

        /// <summary>True when there is a parent graph to go back to (depth &gt; 1).</summary>
        public bool CanAscend => _navStack.Count > 1;

        /// <summary>
        /// Bind the canvas / working copy to the current top-of-stack asset. Factors
        /// the shared body of <see cref="SetAsset"/> / <see cref="Descend"/> /
        /// <see cref="Ascend"/>.
        /// </summary>
        void RebindTop()
        {
            GraphAssetSO top = _navStack.Count > 0 ? _navStack[_navStack.Count - 1] : null;

            Type desired = ResolveCanvasType(top);
            if (_canvas == null || _canvas.GetType() != desired)
            {
                // Canvas type changed (or first build) — rebuild the whole layout so
                // toolbar / property panel / footer / status bar wire up to the new canvas.
                BuildLayout(desired);
            }

            DestroyWorkingCopy();
            _currentAsset = top;
            BindWorkingCopy();
            _toolbar?.Refresh();
            _statusBar?.Refresh();
            UpdateTitle();

            // Track in the MRU list so the toolbar dropdown surfaces the graphs the
            // user actually works with. Persists across editor restarts via EditorPrefs.
            if (top != null)
            {
                string path = AssetDatabase.GetAssetPath(top);
                if (!string.IsNullOrEmpty(path)) GraphEditorMRU.Push(path);
            }

            SetDirty(false);
            saveChangesMessage = "Save changes to this graph?";
        }

        /// <summary>
        /// Prompt to save / discard unsaved work before leaving the current level
        /// (switch, descend, or ascend). Returns false only if the user cancels.
        /// </summary>
        bool ConfirmLeaveCurrentLevel()
        {
            if (!hasUnsavedChanges || _workingAsset == null) return true;

            int choice = EditorUtility.DisplayDialogComplex(
                "Unsaved changes",
                $"'{_currentAsset.name}' has unsaved changes. Save before leaving?",
                "Save", "Cancel", "Discard");
            switch (choice)
            {
                case 0: SaveChanges(); return true;   // Save
                case 1: return false;                 // Cancel
                default: return true;                 // Discard
            }
        }

        /// <summary>
        /// Clone <see cref="_currentAsset"/> into a fresh transient
        /// <see cref="_workingAsset"/> and bind the canvas to it. The
        /// canvas now operates on the clone — mutations don't touch
        /// the on-disk asset until <see cref="SaveChanges"/>.
        /// </summary>
        void BindWorkingCopy()
        {
            if (_currentAsset == null)
            {
                _canvas?.BindToAsset(null);
                return;
            }

            _workingAsset = _currentAsset.CloneForEditing();
            // Positions live in the sidecar, not the asset — load them onto the freshly
            // cloned (position-less) nodes before the canvas lays them out.
            GraphLayoutIO.Apply(_workingAsset, _currentAsset);
            _canvas.BindToAsset(_workingAsset);
        }

        void DestroyWorkingCopy()
        {
            if (_workingAsset == null) return;

            // Clear any Inspector selection that pointed at one of our
            // working-copy nodes — otherwise the Inspector would show a
            // destroyed SO until the user clicks something else.
            if (Selection.activeObject is GraphNodeSO sel)
            {
                foreach (var n in _workingAsset.Nodes)
                {
                    if (ReferenceEquals(n, sel))
                    {
                        Selection.activeObject = null;
                        break;
                    }
                }
            }

            // Drop the working copy's nodes first so they don't dangle
            // as orphaned objects after the parent's destroyed.
            foreach (var node in _workingAsset.Nodes)
                if (node != null) DestroyImmediate(node);

            Undo.ClearUndo(_workingAsset);
            DestroyImmediate(_workingAsset);
            _workingAsset = null;
        }

        /// <summary>
        /// Throw away the working copy's unsaved edits and re-clone from
        /// the on-disk asset so the user keeps editing against the saved
        /// state. Wired to the toolbar's Discard button. Distinct from
        /// the inherited <see cref="EditorWindow.DiscardChanges"/> which
        /// Unity calls when the close prompt's "Discard" button is hit
        /// (we don't need to re-clone there — the window's about to
        /// close and OnDisable will destroy the working copy anyway).
        /// </summary>
        public void Revert()
        {
            if (!hasUnsavedChanges || _currentAsset == null) return;
            if (!EditorUtility.DisplayDialog(
                    "Discard changes?",
                    $"Discard all unsaved changes to '{_currentAsset.name}'?",
                    "Discard", "Cancel"))
            {
                return;
            }

            DestroyWorkingCopy();
            BindWorkingCopy();
            SetDirty(false);
            _toolbar?.Refresh();
            _statusBar?.Refresh();
        }

        // ---------------------------------------------------------------
        // Layout
        // ---------------------------------------------------------------

        static Type ResolveCanvasType(GraphAssetSO asset)
        {
            if (asset == null) return typeof(GraphCanvas);
            var t = asset.EditorCanvasType;
            if (t == null || !typeof(GraphCanvas).IsAssignableFrom(t))
                return typeof(GraphCanvas);
            return t;
        }

        void BuildLayout(Type canvasType)
        {
            // Remember the inspector width across a layout rebuild (canvas-type change).
            PersistInspectorWidth();

            var root = rootVisualElement;
            root.Clear();

            _toolbar = new GraphToolbar(this);
            root.Add(_toolbar);

            var mainRow = new VisualElement
            {
                style =
                {
                    flexGrow = 1f,
                    flexDirection = FlexDirection.Row,
                },
            };
            root.Add(mainRow);

            _canvas = (GraphCanvas)Activator.CreateInstance(canvasType);
            // Node fields are edited in the docked inspector panel, so the canvas
            // keeps its cards compact. Set before the asset binds (below) so the
            // node elements build without inline bodies.
            _canvas.InlineBodyEnabled = false;
            _canvas.style.minWidth = 200f; // the splitter can't swallow the canvas

            _inspectorPanel = new GraphInspectorPanel(_canvas);

            // Canvas (flexible) + inspector (fixed pane) in a TwoPaneSplitView so the
            // divider between them is a draggable resize handle. The chosen width
            // persists per user (see PersistInspectorWidth).
            float inspectorWidth = EditorPrefs.GetFloat(InspectorWidthPrefKey, 300f);
            var split = new TwoPaneSplitView(1, inspectorWidth, TwoPaneSplitViewOrientation.Horizontal)
            {
                style = { flexGrow = 1f },
            };
            split.Add(_canvas);          // index 0 — flexible pane
            split.Add(_inspectorPanel);  // index 1 — fixed pane (drag the divider to resize)
            mainRow.Add(split);

            _validationFooter = new ValidationFooter(_canvas);
            root.Add(_validationFooter);

            _statusBar = new GraphStatusBar(_canvas);
            root.Add(_statusBar);

            // Mark dirty on any graph mutation so Unity shows the "*"
            // in the window title and prompts for a save on close.
            _canvas.GraphChanged += () => SetDirty(true);

            // Descend into a sub-graph when the canvas requests it (double-click on
            // an ISubGraphNode). The window owns the nav stack; the canvas only signals.
            _canvas.DescendRequested += Descend;
        }

        /// <summary>
        /// Called by Unity when the user opts to save via the close prompt
        /// or via the toolbar Save button. Applies the working copy's
        /// state back to the on-disk asset and flushes via SaveAssets.
        /// </summary>
        public override void SaveChanges()
        {
            if (_currentAsset != null && _workingAsset != null)
            {
                _workingAsset.ApplyToOriginal(_currentAsset);
                SaveLayout(_workingAsset, _currentAsset);
                AssetDatabase.SaveAssets();
            }
            SetDirty(false);
            base.SaveChanges();
        }

        // ---------------------------------------------------------------
        // Sidecar layout (positions keyed by node GUID, stored next to the
        // graph asset as Foo.layout.asset — see GraphLayout). Keeps node
        // positions out of the graph asset so layout churn doesn't conflict
        // with structural edits in source control.
        // ---------------------------------------------------------------

        // Loading the sidecar (positions + collapsed flags) onto a graph's nodes is
        // shared with the runtime debugger — see GraphLayoutIO.Apply / PathFor.

        // Write the working copy's node positions to the sidecar (creating it if needed)
        // and prune entries for deleted nodes. Flushed by the SaveAssets in SaveChanges.
        static void SaveLayout(GraphAssetSO working, GraphAssetSO original)
        {
            string path = GraphLayoutIO.PathFor(original);
            if (string.IsNullOrEmpty(path)) return;

            var layout = AssetDatabase.LoadAssetAtPath<GraphLayout>(path);
            if (layout == null)
            {
                layout = ScriptableObject.CreateInstance<GraphLayout>();
                AssetDatabase.CreateAsset(layout, path);
            }

            var live = new HashSet<string>();
            foreach (var n in working.Nodes)
            {
                if (n == null) continue;
                string g = n.Guid.ValueStr;
                layout.Set(g, n.Position);
                layout.SetCollapsed(g, n.Collapsed);
                live.Add(g);
            }
            layout.PruneExcept(live);
            EditorUtility.SetDirty(layout);
        }

        void UpdateTitle()
        {
            titleContent = new GUIContent(
                _currentAsset != null
                    ? _currentAsset.name
                    : "Graph Editor",
                EditorGUIUtility.IconContent("d_BlendTree Icon").image);
        }
    }
}

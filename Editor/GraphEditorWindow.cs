using System;
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

        GraphCanvas _canvas;
        GraphToolbar _toolbar;
        ValidationFooter _validationFooter;
        GraphStatusBar _statusBar;

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
        public static bool OnOpenAsset(int instanceId, int line)
        {
            // Unity 6.6 alpha promoted InstanceIDToObject(int) deprecation to
            // error-level (CS0619), which #pragma warning disable cannot
            // suppress. Reflection sidesteps the compile-time obsolete check;
            // the underlying method still exists and works at runtime.
            var obj = _instanceIDToObject?.Invoke(null, new object[] { instanceId }) as UnityEngine.Object;
            if (obj is GraphAssetSO graph)
            {
                Open(graph);
                return true;
            }
            return false;
        }

        private static readonly System.Reflection.MethodInfo _instanceIDToObject =
            typeof(EditorUtility).GetMethod(
                nameof(EditorUtility.InstanceIDToObject),
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null, new[] { typeof(int) }, null);

        [MenuItem("Window/CupkekGames/Graphs/Graph Editor")]
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
                BindWorkingCopy();

            UpdateTitle();
        }

        void OnDisable()
        {
            // Working copy is transient and tied to this window — clean
            // up so its undo entries don't dangle and the next editor
            // session starts fresh.
            DestroyWorkingCopy();
        }

        public void SetAsset(GraphAssetSO asset)
        {
            // Prompt before discarding unsaved work when switching to a
            // different asset (or unbinding entirely).
            if (asset != _currentAsset && hasUnsavedChanges && _workingAsset != null)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Unsaved changes",
                    $"'{_currentAsset.name}' has unsaved changes. Save before switching?",
                    "Save", "Cancel", "Discard");
                switch (choice)
                {
                    case 0: SaveChanges(); break;          // Save → continue
                    case 1: return;                        // Cancel → bail
                    case 2: /* Discard → fall through */ break;
                }
            }

            Type desired = ResolveCanvasType(asset);
            if (_canvas == null || _canvas.GetType() != desired)
            {
                // Canvas type changed (or first build) — rebuild the whole
                // layout so toolbar / property panel / footer / status bar
                // all wire up to the new canvas instance.
                BuildLayout(desired);
            }

            DestroyWorkingCopy();
            _currentAsset = asset;
            BindWorkingCopy();
            _toolbar?.Refresh();
            _statusBar?.Refresh();
            UpdateTitle();

            // Track in the MRU list so the toolbar dropdown surfaces the
            // graphs the user actually works with. Persists across
            // editor restarts via EditorPrefs.
            if (asset != null)
            {
                string path = AssetDatabase.GetAssetPath(asset);
                if (!string.IsNullOrEmpty(path)) GraphEditorMRU.Push(path);
            }

            SetDirty(false);
            saveChangesMessage = "Save changes to this graph?";
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
            _canvas.style.flexGrow = 1f;
            mainRow.Add(_canvas);

            _validationFooter = new ValidationFooter(_canvas);
            root.Add(_validationFooter);

            _statusBar = new GraphStatusBar(_canvas);
            root.Add(_statusBar);

            // Mark dirty on any graph mutation so Unity shows the "*"
            // in the window title and prompts for a save on close.
            _canvas.GraphChanged += () => SetDirty(true);
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
                AssetDatabase.SaveAssets();
            }
            SetDirty(false);
            base.SaveChanges();
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

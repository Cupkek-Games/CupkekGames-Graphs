using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Live runtime debugger for <b>every</b> mounted graph at once — pick one from the
    /// dropdown (populated by <see cref="GraphRuntimeRegistry"/>) and watch its read-only
    /// canvas paint each node's live state in play mode (whatever the graph's
    /// <see cref="GraphAssetSO.CreateRuntimeStateSource"/> reports). A Problems panel
    /// aggregates <see cref="GraphProblemRegistry"/> providers plus the selected graph's
    /// own <see cref="GraphAssetSO.Validate"/>, both at edit time and in play.
    ///
    /// <para>
    /// Fully generic: it knows nothing of nav / behaviour-trees / any consumer. A graph
    /// becomes visible here purely by being registered as live and by exposing a runtime
    /// state source; problems appear purely via the provider seam.
    /// </para>
    /// </summary>
    public class GraphRuntimeDebugWindow : EditorWindow
    {
        [MenuItem("Tools/CupkekGames/Graph Runtime Debugger", false, 201)]
        public static void Open()
        {
            var w = GetWindow<GraphRuntimeDebugWindow>();
            w.titleContent = new GUIContent(
                "Graph Debugger",
                EditorGUIUtility.IconContent("d_UnityEditor.AnimationWindow").image);
            w.Show();
        }

        DropdownField _picker;
        VisualElement _canvasHost;
        Label _placeholder;
        Foldout _detailFoldout;
        VisualElement _detailList;
        IGraphRuntimeStateSource _detailBound;
        Foldout _problemsFoldout;
        VisualElement _problemsList;

        GraphCanvas _canvas;
        GraphAssetSO _selectedGraph;
        List<GraphRuntimeRegistry.Entry> _entries = new();

        void OnEnable()
        {
            BuildUI();

            GraphRuntimeRegistry.Changed += OnLiveSetChanged;
            GraphProblemRegistry.Changed += RefreshProblems;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            RebuildPicker();
            RefreshProblems();
        }

        void OnDisable()
        {
            GraphRuntimeRegistry.Changed -= OnLiveSetChanged;
            GraphProblemRegistry.Changed -= RefreshProblems;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            TeardownCanvas();
        }

        void OnPlayModeChanged(PlayModeStateChange _)
        {
            // The live set + the problem set both differ across the play boundary.
            RebuildPicker();
            RefreshProblems();
            RefreshDetail();
        }

        void OnLiveSetChanged() => RebuildPicker();

        // ── UI ──────────────────────────────────────────────────────

        void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;

            // Toolbar: live-graph picker + refresh.
            var toolbar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 6, paddingRight = 6, paddingTop = 3, paddingBottom = 3,
                    borderBottomWidth = 1,
                    borderBottomColor = new Color(0f, 0f, 0f, 0.35f),
                },
            };
            toolbar.Add(new Label("Live graph:") { style = { marginRight = 6, unityTextAlign = TextAnchor.MiddleLeft } });

            _picker = new DropdownField { style = { minWidth = 200, marginRight = 6 } };
            _picker.RegisterValueChangedCallback(_ => OnPickerChanged());
            toolbar.Add(_picker);

            var refresh = new Button(() => { RebuildPicker(); RefreshProblems(); }) { text = "Refresh" };
            toolbar.Add(refresh);
            root.Add(toolbar);

            // Canvas host (fills) + a placeholder when nothing is live.
            _canvasHost = new VisualElement { style = { flexGrow = 1 } };
            root.Add(_canvasHost);

            _placeholder = new Label(
                "No live graphs.\n\nEnter play mode and mount a graph (e.g. a NavHost) to debug it here.")
            {
                style =
                {
                    flexGrow = 1,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    color = new Color(0.6f, 0.6f, 0.6f),
                    whiteSpace = WhiteSpace.Normal,
                },
            };
            _canvasHost.Add(_placeholder);

            // Selected-node detail (only when the live source exposes detail rows).
            _detailFoldout = new Foldout { text = "Selected", value = true };
            _detailFoldout.style.borderTopWidth = 1;
            _detailFoldout.style.borderTopColor = new Color(0f, 0f, 0f, 0.35f);
            _detailFoldout.style.display = DisplayStyle.None;
            _detailList = new ScrollView { style = { maxHeight = 130 } };
            _detailFoldout.Add(_detailList);
            root.Add(_detailFoldout);

            // Problems panel.
            _problemsFoldout = new Foldout { text = "Problems", value = true };
            _problemsFoldout.style.borderTopWidth = 1;
            _problemsFoldout.style.borderTopColor = new Color(0f, 0f, 0f, 0.35f);
            _problemsFoldout.style.maxHeight = 180;
            _problemsList = new ScrollView { style = { maxHeight = 150 } };
            _problemsFoldout.Add(_problemsList);
            root.Add(_problemsFoldout);
        }

        // ── Picker / canvas binding ─────────────────────────────────

        void RebuildPicker()
        {
            if (_picker == null) return;

            _entries = new List<GraphRuntimeRegistry.Entry>(GraphRuntimeRegistry.Live);

            var choices = new List<string>(_entries.Count);
            for (int i = 0; i < _entries.Count; i++)
                choices.Add(string.IsNullOrEmpty(_entries[i].Label) ? "(unnamed graph)" : _entries[i].Label);

            // Preserve the current selection by graph reference; else first; else none.
            int sel = -1;
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Graph == _selectedGraph) { sel = i; break; }
            if (sel < 0) sel = _entries.Count > 0 ? 0 : -1;

            _picker.choices = choices;
            _picker.style.display = _entries.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (sel >= 0)
            {
                _picker.SetValueWithoutNotify(choices[sel]);
                _picker.index = sel;
            }

            var newSelected = sel >= 0 ? _entries[sel].Graph : null;
            if (newSelected != _selectedGraph || (newSelected != null && _canvas == null))
            {
                _selectedGraph = newSelected;
                RebindCanvas(_selectedGraph);
                RefreshProblems();
            }
        }

        void OnPickerChanged()
        {
            int i = _picker.index;
            var g = (i >= 0 && i < _entries.Count) ? _entries[i].Graph : null;
            if (g == _selectedGraph) return;
            _selectedGraph = g;
            RebindCanvas(g);
            RefreshProblems();
        }

        void RebindCanvas(GraphAssetSO asset)
        {
            TeardownCanvas();

            _placeholder.style.display = asset == null ? DisplayStyle.Flex : DisplayStyle.None;
            if (asset == null) return;

            _canvas = CreateCanvasFor(asset);
            _canvas.style.flexGrow = 1;
            _canvas.InlineBodyEnabled = false; // compact cards
            _canvas.ReadOnly = true;           // must be set before BindToAsset
            _canvasHost.Add(_canvas);

            // Position the live nodes from the same sidecar the editor authored.
            GraphLayoutIO.Apply(asset, asset);
            _canvas.BindToAsset(asset);
            _canvas.Selection.Changed += RefreshDetail;
            RefreshDetail();
        }

        void TeardownCanvas()
        {
            if (_detailBound != null)
            {
                _detailBound.Changed -= RefreshDetail;
                _detailBound = null;
            }
            if (_canvas == null) return;
            _canvas.Selection.Changed -= RefreshDetail;
            // DetachFromPanelEvent → Unbind() + the runtime overlay teardown.
            _canvas.RemoveFromHierarchy();
            _canvas = null;
        }

        // ── Selected-node detail ────────────────────────────────────

        void RefreshDetail()
        {
            if (_detailFoldout == null) return;

            // Track the canvas's live source so nav pushes re-render the open rows.
            var source = _canvas != null ? _canvas.RuntimeStateSource : null;
            if (!ReferenceEquals(source, _detailBound))
            {
                if (_detailBound != null) _detailBound.Changed -= RefreshDetail;
                _detailBound = source;
                if (_detailBound != null) _detailBound.Changed += RefreshDetail;
            }

            GraphNodeSO node = null;
            if (_canvas != null)
            {
                foreach (var ne in _canvas.Selection.Nodes)
                {
                    node = ne != null ? ne.Node : null;
                    break;
                }
            }

            var rows = new List<(string label, string value)>();
            bool has = node != null
                && source is IGraphRuntimeDetailSource detail
                && detail.TryGetDetail(node, rows)
                && rows.Count > 0;

            _detailFoldout.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            if (!has) return;

            _detailFoldout.text = $"Selected: {node.DisplayTitle}";
            _detailList.Clear();
            foreach (var (label, value) in rows)
            {
                var row = new VisualElement
                {
                    style = { flexDirection = FlexDirection.Row, paddingLeft = 4, paddingRight = 4 },
                };
                row.Add(new Label(label)
                {
                    style = { minWidth = 90, color = new Color(0.62f, 0.62f, 0.62f) },
                });
                row.Add(new Label(value)
                {
                    style = { whiteSpace = WhiteSpace.Normal, flexShrink = 1 },
                });
                _detailList.Add(row);
            }
        }

        // Resolve the same canvas subclass the editor would use for this asset
        // (e.g. a domain canvas via EditorCanvasType), defaulting to the generic one.
        static GraphCanvas CreateCanvasFor(GraphAssetSO asset)
        {
            Type t = asset.EditorCanvasType;
            if (t == null || !typeof(GraphCanvas).IsAssignableFrom(t)) t = typeof(GraphCanvas);
            try { return (GraphCanvas)Activator.CreateInstance(t); }
            catch { return new GraphCanvas(); }
        }

        // ── Problems panel ──────────────────────────────────────────

        void RefreshProblems()
        {
            if (_problemsList == null) return;
            _problemsList.Clear();

            // Two scopes, rendered as labeled groups so a provider's project/cross-graph
            // problems are never mistaken for something wrong with the selected graph.
            var crossGraph = GraphProblemRegistry.Collect();

            // The selected live graph's own per-graph validation (duplicate/missing
            // id, shape rules) — same generic Validate() the editor footer shows.
            var selected = new List<GraphValidationIssue>();
            if (_selectedGraph != null)
                selected.AddRange(_selectedGraph.Validate());

            if (crossGraph.Count > 0)
            {
                AddGroupHeader("Cross-graph");
                foreach (var p in crossGraph)
                    AddProblemRow(p.Severity, p.Message, p.Graph);
            }

            if (selected.Count > 0)
            {
                AddGroupHeader($"Selected: {_selectedGraph.name}");
                foreach (var issue in selected)
                    AddProblemRow(issue.Severity, issue.Message, _selectedGraph);
            }

            int total = crossGraph.Count + selected.Count;
            _problemsFoldout.text = total == 0
                ? "Problems"
                : crossGraph.Count > 0 && selected.Count > 0
                    ? $"Problems ({crossGraph.Count} cross-graph · {selected.Count} selected)"
                    : $"Problems ({total})";
            if (total == 0)
            {
                _problemsList.Add(new Label("No problems.")
                {
                    style = { color = new Color(0.5f, 0.7f, 0.5f), paddingTop = 2, paddingBottom = 2 },
                });
            }
        }

        void AddGroupHeader(string text)
        {
            _problemsList.Add(new Label(text)
            {
                style =
                {
                    color = new Color(0.62f, 0.62f, 0.62f),
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 10,
                    paddingTop = 4, paddingBottom = 1, paddingLeft = 4,
                },
            });
        }

        void AddProblemRow(GraphValidationIssue.SeverityLevel severity, string message, GraphAssetSO graph)
        {
            bool error = severity == GraphValidationIssue.SeverityLevel.Error;
            var row = new Label((error ? "●  " : "▲  ") + message)
            {
                style =
                {
                    color = error ? new Color(0.92f, 0.45f, 0.45f) : new Color(0.92f, 0.78f, 0.38f),
                    whiteSpace = WhiteSpace.Normal,
                    paddingTop = 1, paddingBottom = 1, paddingLeft = 4, paddingRight = 4,
                },
            };
            if (graph != null)
            {
                row.tooltip = "Click to ping " + graph.name;
                row.RegisterCallback<ClickEvent>(_ => EditorGUIUtility.PingObject(graph));
            }
            _problemsList.Add(row);
        }
    }
}

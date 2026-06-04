using System.Collections.Generic;
using System.Text;
using CupkekGames.Data.Primitives;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Inline footer that surfaces <see cref="GraphAssetSO.Validate"/> issues
    /// for the bound graph. Hides itself when the graph reports zero issues
    /// so it doesn't take screen real-estate on a healthy graph. Click any
    /// row with a <c>TargetNodeGuid</c> to pan the canvas to that node and
    /// select it.
    /// </summary>
    public class ValidationFooter : VisualElement
    {
        const float MaxHeight = 120f;
        const float HeaderHeight = 22f;
        const float RowHeight = 22f;

        static readonly Color BackgroundColor = GraphTheme.ValidationBackdrop;
        static readonly Color SeparatorColor  = GraphTheme.Separator;
        static readonly Color HeaderColor     = GraphTheme.ValidationHeader;
        static readonly Color ErrorColor      = GraphTheme.ValidationError;
        static readonly Color WarningColor    = GraphTheme.ValidationWarning;
        static readonly Color InfoColor       = GraphTheme.ValidationInfo;
        static readonly Color RowHoverColor   = GraphTheme.ValidationRowHover;

        readonly GraphCanvas _canvas;
        readonly Label _summaryLabel;
        readonly Button _copyAllButton;
        readonly ScrollView _scroll;
        readonly VisualElement _list;
        readonly List<GraphValidationIssue> _currentIssues = new List<GraphValidationIssue>();

        public ValidationFooter(GraphCanvas canvas)
        {
            _canvas = canvas;

            AddToClassList("cgg-graph-validation-footer");
            style.flexDirection = FlexDirection.Column;
            style.maxHeight = MaxHeight;
            style.backgroundColor = BackgroundColor;
            style.borderTopWidth = 1f;
            style.borderTopColor = SeparatorColor;

            var header = new VisualElement
            {
                style =
                {
                    height = HeaderHeight,
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 8f,
                    paddingRight = 8f,
                },
                pickingMode = PickingMode.Ignore,
            };
            Add(header);

            _summaryLabel = new Label
            {
                style =
                {
                    color = HeaderColor,
                    fontSize = 11,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    flexGrow = 1f,
                },
            };
            header.Add(_summaryLabel);

            _copyAllButton = new Button(CopyAll)
            {
                text = "Copy All",
                tooltip = "Copy every issue to the system clipboard.",
                style =
                {
                    height = 18f,
                    marginTop = 0,
                    marginBottom = 0,
                    marginLeft = 4f,
                    marginRight = 0,
                    paddingLeft = 6f,
                    paddingRight = 6f,
                    fontSize = 10,
                },
            };
            header.Add(_copyAllButton);

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1f;
            Add(_scroll);

            _list = new VisualElement();
            _scroll.Add(_list);

            canvas.GraphChanged += Refresh;
            // Inspector-driven mutations (DestinationId, Prefab, start guid,
            // etc.) route through GraphAssetSO.EditorAssetMutated rather
            // than the canvas's own GraphChanged event. Subscribe to both.
            GraphAssetSO.EditorAssetMutated += OnAssetMutatedExternally;
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                canvas.GraphChanged -= Refresh;
                GraphAssetSO.EditorAssetMutated -= OnAssetMutatedExternally;
            });

            Refresh();
        }

        void OnAssetMutatedExternally(GraphAssetSO asset)
        {
            if (asset == _canvas.Asset) Refresh();
        }

        public void Refresh()
        {
            _list.Clear();
            _currentIssues.Clear();

            if (_canvas.Asset == null)
            {
                style.display = DisplayStyle.None;
                return;
            }

            _currentIssues.AddRange(_canvas.Asset.Validate());

            // Fan the issue set out to the cards (worst-severity chip + tinted
            // border per node). Done before the early-out so a now-clean graph
            // clears every card's prior state.
            _canvas.ApplyNodeValidation(_currentIssues);

            if (_currentIssues.Count == 0)
            {
                style.display = DisplayStyle.None;
                return;
            }

            style.display = DisplayStyle.Flex;

            int errors = 0, warnings = 0, infos = 0;
            foreach (var iss in _currentIssues)
            {
                switch (iss.Severity)
                {
                    case GraphValidationIssue.SeverityLevel.Error: errors++; break;
                    case GraphValidationIssue.SeverityLevel.Warning: warnings++; break;
                    case GraphValidationIssue.SeverityLevel.Info: infos++; break;
                }
                _list.Add(MakeRow(iss));
            }

            _summaryLabel.text = BuildSummary(errors, warnings, infos);
        }

        // ---------------------------------------------------------------

        VisualElement MakeRow(GraphValidationIssue issue)
        {
            var row = new VisualElement
            {
                style =
                {
                    height = RowHeight,
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 8f,
                    paddingRight = 4f,
                },
            };

            var bullet = new Label("●")
            {
                style =
                {
                    color = ColorFor(issue.Severity),
                    fontSize = 12,
                    marginRight = 6f,
                    unityFontStyleAndWeight = FontStyle.Bold,
                },
            };
            row.Add(bullet);

            var message = new Label(issue.Message ?? "")
            {
                style =
                {
                    color = GraphTheme.TextPrimary,
                    fontSize = 11,
                    flexGrow = 1f,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                    whiteSpace = WhiteSpace.NoWrap,
                },
            };
            row.Add(message);

            // Per-row copy button — hidden until hover so it doesn't add
            // visual noise on a clean footer. Stops propagation so the
            // row's jump-to-node click handler doesn't fire.
            var copyBtn = new Button(() => CopyIssue(issue))
            {
                text = "Copy",
                tooltip = "Copy this issue's text to the system clipboard.",
                style =
                {
                    height = 16f,
                    marginTop = 0,
                    marginBottom = 0,
                    marginLeft = 4f,
                    marginRight = 0,
                    paddingLeft = 5f,
                    paddingRight = 5f,
                    fontSize = 9,
                    display = DisplayStyle.None,
                },
            };
            copyBtn.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            row.Add(copyBtn);

            // Hover affordance + reveal copy button.
            row.RegisterCallback<PointerEnterEvent>(_ =>
            {
                row.style.backgroundColor = RowHoverColor;
                copyBtn.style.display = DisplayStyle.Flex;
            });
            row.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                row.style.backgroundColor = Color.clear;
                copyBtn.style.display = DisplayStyle.None;
            });

            // Click → jump to node (if any)
            row.RegisterCallback<PointerDownEvent>(_ => OnRowClicked(issue));

            return row;
        }

        // ---------------------------------------------------------------
        // Clipboard
        // ---------------------------------------------------------------

        void CopyAll()
        {
            if (_currentIssues.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var iss in _currentIssues)
                AppendFormatted(sb, iss);
            EditorGUIUtility.systemCopyBuffer = sb.ToString();
        }

        static void CopyIssue(GraphValidationIssue issue)
        {
            var sb = new StringBuilder();
            AppendFormatted(sb, issue);
            EditorGUIUtility.systemCopyBuffer = sb.ToString();
        }

        static void AppendFormatted(StringBuilder sb, GraphValidationIssue iss)
        {
            sb.Append('[').Append(iss.Severity).Append("] ")
              .Append(iss.Message ?? "");
            if (!string.IsNullOrEmpty(iss.TargetNodeGuid.ValueStr))
                sb.Append("  (node: ").Append(iss.TargetNodeGuid.ValueStr).Append(')');
            sb.Append('\n');
        }

        void OnRowClicked(GraphValidationIssue issue)
        {
            SerializedGuid guid = issue.TargetNodeGuid;
            if (guid.Value() == System.Guid.Empty) return;

            foreach (var ne in _canvas.NodeElements)
            {
                if (ne?.Node == null) continue;
                if (ne.Node.Guid == guid)
                {
                    _canvas.Selection.SetTo(ne);
                    _canvas.FocusOnNode(ne);
                    return;
                }
            }
        }

        static Color ColorFor(GraphValidationIssue.SeverityLevel s) => s switch
        {
            GraphValidationIssue.SeverityLevel.Error => ErrorColor,
            GraphValidationIssue.SeverityLevel.Warning => WarningColor,
            _ => InfoColor,
        };

        static string BuildSummary(int errors, int warnings, int infos)
        {
            var parts = new List<string>();
            if (errors > 0) parts.Add(errors == 1 ? "1 error" : $"{errors} errors");
            if (warnings > 0) parts.Add(warnings == 1 ? "1 warning" : $"{warnings} warnings");
            if (infos > 0) parts.Add(infos == 1 ? "1 info" : $"{infos} infos");
            return parts.Count == 0 ? "" : string.Join(", ", parts);
        }
    }
}

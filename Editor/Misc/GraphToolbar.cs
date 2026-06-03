using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Top toolbar for the <see cref="GraphEditorWindow"/>. Asset name on
    /// the left; ping / fit / save buttons on the right. Validate /
    /// asset-switcher / theme-toggle land in later polish work.
    /// </summary>
    public class GraphToolbar : VisualElement
    {
        static readonly Color BackgroundColor = GraphTheme.SurfaceElevated;
        static readonly Color SeparatorColor  = GraphTheme.Separator;

        readonly GraphEditorWindow _window;
        readonly Label _assetNameLabel;
        Button _saveButton;
        Button _discardButton;
        Button _backButton;

        public GraphToolbar(GraphEditorWindow window)
        {
            _window = window;

            AddToClassList("cgg-graph-toolbar");
            style.flexDirection = FlexDirection.Row;
            style.height = 28f;
            style.backgroundColor = BackgroundColor;
            style.borderBottomWidth = 1f;
            style.borderBottomColor = SeparatorColor;
            style.paddingLeft = 8f;
            style.paddingRight = 8f;
            style.alignItems = Align.Center;

            // Back button — descends out of a sub-graph. Enabled only when the
            // window's nav stack has a parent to return to (see Refresh).
            _backButton = MakeButton("←", () => _window.Ascend());
            _backButton.tooltip = "Back to the parent graph";
            Add(_backButton);

            _assetNameLabel = new Label
            {
                style =
                {
                    color = Color.white,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 12,
                },
            };
            Add(_assetNameLabel);

            // Tiny dropdown — populated from GraphEditorMRU on click,
            // switches the window to a previously-opened graph.
            var switcher = MakeButton("▾", ShowAssetSwitcherMenu);
            switcher.style.marginLeft = 4f;
            switcher.tooltip = "Switch to a recently-opened graph";
            switcher.style.paddingLeft = 6f;
            switcher.style.paddingRight = 6f;
            Add(switcher);

            var spacer = new VisualElement { style = { flexGrow = 1f } };
            Add(spacer);

            Add(MakeToggle("Grid", () => _window.Canvas?.GridVisible ?? true,
                v => { if (_window.Canvas != null) _window.Canvas.GridVisible = v; }));

            Add(MakeToggle("Snap", () => _window.Canvas?.SnapToGrid ?? false,
                v => { if (_window.Canvas != null) _window.Canvas.SnapToGrid = v; }));

            Add(MakeToggle("Minimap", () => _window.Canvas?.MinimapVisible ?? true,
                v => { if (_window.Canvas != null) _window.Canvas.MinimapVisible = v; }));

            AddSeparator();

            Add(MakeButton("Ping", () =>
            {
                if (_window.CurrentAsset != null)
                    EditorGUIUtility.PingObject(_window.CurrentAsset);
            }));

            Add(MakeButton("Auto Layout", () => _window.Canvas?.AutoLayout()));

            Add(MakeButton("Fit View", () => _window.Canvas?.ResetView()));

            // Route through the window's SaveChanges override so the
            // hasUnsavedChanges flag clears (and the "*" in the title
            // disappears) AND so the working copy's state applies back
            // to the on-disk asset.
            _saveButton = MakeButton("Save", () => _window.SaveChanges());
            Add(_saveButton);

            _discardButton = MakeButton("Discard", () => _window.Revert());
            Add(_discardButton);

            // Enable Save / Discard only when there are unsaved changes.
            // Subscribe to the window's DirtyStateChanged + refresh on
            // mount so initial state is correct.
            _window.DirtyStateChanged += RefreshDirtyButtons;
            RegisterCallback<DetachFromPanelEvent>(_ => _window.DirtyStateChanged -= RefreshDirtyButtons);
            RefreshDirtyButtons();

            Refresh();
        }

        void AddSeparator()
        {
            var sep = new VisualElement
            {
                style =
                {
                    width = 1f,
                    height = 16f,
                    marginLeft = 6f,
                    marginRight = 6f,
                    backgroundColor = GraphTheme.SeparatorLine,
                },
                pickingMode = PickingMode.Ignore,
            };
            Add(sep);
        }

        static readonly Color ToggleCheckedColor   = GraphTheme.ToggleChecked;
        static readonly Color ToggleUncheckedColor = GraphTheme.ToggleUnchecked;
        static readonly Color ToggleBorderColor    = GraphTheme.ToggleBorder;

        /// <summary>
        /// Compact toolbar toggle: a checkbox immediately followed by its
        /// label, so multiple toggles in a row don't suffer from Unity's
        /// default BaseField label flex-grow (which pushes the checkbox far
        /// right and makes each checkbox appear to "belong" to the next
        /// label).
        /// </summary>
        static VisualElement MakeToggle(string label, System.Func<bool> get, System.Action<bool> set)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginLeft = 8f,
                    marginRight = 0f,
                },
            };

            bool value = get();

            var box = new VisualElement
            {
                style =
                {
                    width = 14f,
                    height = 14f,
                    marginRight = 5f,
                    borderTopWidth = 1f,
                    borderBottomWidth = 1f,
                    borderLeftWidth = 1f,
                    borderRightWidth = 1f,
                    borderTopColor = ToggleBorderColor,
                    borderBottomColor = ToggleBorderColor,
                    borderLeftColor = ToggleBorderColor,
                    borderRightColor = ToggleBorderColor,
                    borderTopLeftRadius = 2f,
                    borderTopRightRadius = 2f,
                    borderBottomLeftRadius = 2f,
                    borderBottomRightRadius = 2f,
                    backgroundColor = value ? ToggleCheckedColor : ToggleUncheckedColor,
                },
                pickingMode = PickingMode.Ignore,
            };
            row.Add(box);

            var lbl = new Label(label)
            {
                style =
                {
                    color = Color.white,
                    fontSize = 11,
                },
                pickingMode = PickingMode.Ignore,
            };
            row.Add(lbl);

            row.RegisterCallback<ClickEvent>(_ =>
            {
                value = !value;
                box.style.backgroundColor = value ? ToggleCheckedColor : ToggleUncheckedColor;
                set(value);
            });

            // Hover lift — the row owns the events (box/label are pickingMode
            // Ignore), so this matches the flat buttons. Resting is transparent.
            row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = GraphTheme.HoverOverlay);
            row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = Color.clear);

            return row;
        }

        /// <summary>Update the asset name label — call after the window rebinds.</summary>
        public void Refresh()
        {
            var asset = _window.CurrentAsset;
            _assetNameLabel.text = asset != null ? asset.name : "(no graph)";
            if (_backButton != null)
            {
                _backButton.SetEnabled(_window.CanAscend);
                _backButton.style.opacity = _window.CanAscend ? 1f : 0.5f;
            }
            RefreshDirtyButtons();
        }

        void RefreshDirtyButtons()
        {
            bool dirty = _window.hasUnsavedChanges;
            if (_saveButton != null)
            {
                _saveButton.SetEnabled(dirty);
                _saveButton.style.opacity = dirty ? 1f : 0.5f;
            }
            if (_discardButton != null)
            {
                _discardButton.SetEnabled(dirty);
                _discardButton.style.opacity = dirty ? 1f : 0.5f;
            }
        }

        void ShowAssetSwitcherMenu()
        {
            var menu = new GenericMenu();
            var paths = GraphEditorMRU.GetPaths();

            if (paths.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("(no recent graphs)"));
            }
            else
            {
                foreach (var path in paths)
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(path);
                    var captured = path;
                    bool isCurrent = _window.CurrentAsset != null
                        && AssetDatabase.GetAssetPath(_window.CurrentAsset) == captured;
                    menu.AddItem(new GUIContent(name), isCurrent, () =>
                    {
                        var asset = AssetDatabase.LoadMainAssetAtPath(captured) as GraphAssetSO;
                        if (asset != null) _window.SetAsset(asset);
                    });
                }
            }
            menu.ShowAsContext();
        }

        // Flat toolbar button palette — sits on the SurfaceElevated toolbar
        // plane. Rest is a hair lighter than the chrome so the button reads
        // as a distinct hit-target; hover lifts it further. No hard border
        // (the stock IMGUI button look is what we're replacing).
        static readonly Color ButtonRest    = new Color(0.24f, 0.25f, 0.29f);
        static readonly Color ButtonHover    = new Color(0.30f, 0.31f, 0.36f);
        static readonly Color ButtonText     = GraphTheme.TextSecondary;

        /// <summary>
        /// Flat-styled toolbar button. Replaces the stock grey IMGUI button
        /// chrome with a flat fill + hover state + a radius that matches the
        /// node cards / toggles, so the toolbar reads as part of the custom
        /// editor rather than a default Unity row. Behavior (onClick) is
        /// unchanged.
        /// </summary>
        static Button MakeButton(string text, System.Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.marginLeft = 4f;
            b.style.marginRight = 0f;
            b.style.marginTop = 0f;
            b.style.marginBottom = 0f;
            b.style.paddingLeft = 10f;
            b.style.paddingRight = 10f;
            b.style.height = 22f;

            // Flat fill, no IMGUI border, matching radius + text color.
            b.style.backgroundColor = ButtonRest;
            b.style.color = ButtonText;
            b.style.borderTopWidth = 0f;
            b.style.borderBottomWidth = 0f;
            b.style.borderLeftWidth = 0f;
            b.style.borderRightWidth = 0f;
            b.style.borderTopLeftRadius = 4f;
            b.style.borderTopRightRadius = 4f;
            b.style.borderBottomLeftRadius = 4f;
            b.style.borderBottomRightRadius = 4f;

            // Hover lift. Skipped while the button is disabled so a greyed-out
            // Save/Discard doesn't appear interactive.
            b.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (b.enabledSelf) b.style.backgroundColor = ButtonHover;
            });
            b.RegisterCallback<PointerLeaveEvent>(_ => b.style.backgroundColor = ButtonRest);

            return b;
        }
    }
}

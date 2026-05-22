using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Popup floating window that lists every concrete <see cref="GraphNodeSO"/>
    /// subclass derivable from a given <c>baseType</c>, filtered by a search
    /// field. Replaces the reflection-driven context menu the canvas used
    /// in pieces 2-3 with something that scales past a handful of types and
    /// gives the user keyboard-driven node creation.
    ///
    /// Keyboard:
    /// <list type="bullet">
    ///   <item>Typing filters the list (case-insensitive, substring against the full category/name path).</item>
    ///   <item>Down arrow moves focus to the list; Enter commits the highlighted entry.</item>
    ///   <item>Escape closes the popup without creating anything.</item>
    /// </list>
    /// </summary>
    public class NodeSearchPopup : EditorWindow
    {
        static NodeSearchPopup s_open;

        const float PopupWidth = 280f;
        const float PopupHeight = 340f;

        Type _baseType;
        bool _allowStickyNote;
        Action<Type> _onSelected;

        TextField _searchField;
        ListView _listView;
        List<Entry> _allEntries = new List<Entry>();
        List<Entry> _filteredEntries = new List<Entry>();

        class Entry
        {
            public Type Type;
            public string Label;
            public string Category;
            public string FullPath;
        }

        /// <summary>
        /// Show the popup at a given screen position. Closes any previously
        /// open instance first so two never overlap.
        /// </summary>
        public static void Show(Vector2 screenPos, Type baseType, bool allowStickyNote, Action<Type> onSelected)
        {
            if (s_open != null)
            {
                s_open.Close();
                s_open = null;
            }

            var popup = CreateInstance<NodeSearchPopup>();
            popup._baseType = baseType ?? typeof(GraphNodeSO);
            popup._allowStickyNote = allowStickyNote;
            popup._onSelected = onSelected;

            var activatorRect = new Rect(screenPos.x, screenPos.y, 1f, 1f);
            popup.ShowAsDropDown(activatorRect, new Vector2(PopupWidth, PopupHeight));
            s_open = popup;
        }

        void OnEnable()
        {
            rootVisualElement.style.flexGrow = 1f;

            _searchField = new TextField();
            _searchField.style.marginTop = 4f;
            _searchField.style.marginLeft = 4f;
            _searchField.style.marginRight = 4f;
            _searchField.RegisterValueChangedCallback(_ => RefreshFilter());
            _searchField.RegisterCallback<KeyDownEvent>(OnSearchKey);
            rootVisualElement.Add(_searchField);

            _listView = new ListView(_filteredEntries, 22, MakeItem, BindItem)
            {
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
            };
            _listView.style.flexGrow = 1f;
            _listView.style.marginTop = 4f;
            _listView.itemsChosen += OnItemsChosen;
            _listView.RegisterCallback<KeyDownEvent>(OnListKey);
            rootVisualElement.Add(_listView);
        }

        void OnDisable()
        {
            if (s_open == this) s_open = null;
        }

        void OnFocus()
        {
            // Delay so the panel is fully attached before grabbing focus.
            if (_searchField != null && _allEntries.Count == 0)
            {
                BuildEntries();
                RefreshFilter();
            }
            _searchField?.Focus();
        }

        // ---------------------------------------------------------------
        // List items
        // ---------------------------------------------------------------

        VisualElement MakeItem()
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    paddingLeft = 8f,
                    paddingRight = 8f,
                    paddingTop = 2f,
                    paddingBottom = 2f,
                    alignItems = Align.Center,
                },
            };
            var label = new Label
            {
                style = { color = Color.white, fontSize = 12 },
            };
            row.Add(label);
            return row;
        }

        void BindItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _filteredEntries.Count) return;
            var label = (Label)element[0];
            label.text = _filteredEntries[index].FullPath;
        }

        void OnItemsChosen(IEnumerable<object> items)
        {
            foreach (var obj in items)
            {
                if (obj is Entry entry)
                {
                    Commit(entry);
                    return;
                }
            }
        }

        // ---------------------------------------------------------------
        // Keyboard
        // ---------------------------------------------------------------

        void OnSearchKey(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    Close();
                    evt.StopPropagation();
                    break;
                case KeyCode.DownArrow:
                    if (_filteredEntries.Count > 0)
                    {
                        _listView.selectedIndex = Mathf.Max(0, _listView.selectedIndex);
                        _listView.Focus();
                    }
                    evt.StopPropagation();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_filteredEntries.Count > 0)
                    {
                        int idx = Mathf.Clamp(_listView.selectedIndex, 0, _filteredEntries.Count - 1);
                        Commit(_filteredEntries[idx]);
                    }
                    evt.StopPropagation();
                    break;
            }
        }

        void OnListKey(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    Close();
                    evt.StopPropagation();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_filteredEntries.Count > 0 && _listView.selectedIndex >= 0)
                        Commit(_filteredEntries[_listView.selectedIndex]);
                    evt.StopPropagation();
                    break;
            }
        }

        void Commit(Entry entry)
        {
            var cb = _onSelected;
            _onSelected = null;
            Close();
            cb?.Invoke(entry.Type);
        }

        // ---------------------------------------------------------------
        // Entry building + filtering
        // ---------------------------------------------------------------

        void BuildEntries()
        {
            _allEntries.Clear();

            if (_allowStickyNote)
            {
                _allEntries.Add(new Entry
                {
                    Type = typeof(StickyNoteNodeSO),
                    Label = "Sticky Note",
                    Category = "",
                    FullPath = "Sticky Note",
                });
            }

            // TypeCache.GetTypesDerivedFrom returns subclasses only — it
            // excludes _baseType itself. For graphs whose base IS the one
            // concrete shape (e.g. LunaNavDestinationSO), include the base
            // so the search has something to offer.
            if (!_baseType.IsAbstract
                && typeof(GraphNodeSO).IsAssignableFrom(_baseType)
                && _baseType != typeof(StickyNoteNodeSO))
            {
                AddEntry(_baseType);
            }

            foreach (var t in TypeCache.GetTypesDerivedFrom(_baseType))
            {
                if (t.IsAbstract) continue;
                if (!typeof(GraphNodeSO).IsAssignableFrom(t)) continue;
                if (t == typeof(StickyNoteNodeSO)) continue;
                AddEntry(t);
            }

            _allEntries.Sort((a, b) => string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase));
        }

        void AddEntry(Type t)
        {
            var attr = t.GetCustomAttribute<GraphNodeCategoryAttribute>();
            string label = StripSoSuffix(t.Name);
            string category = attr?.Path ?? "";
            string fullPath = string.IsNullOrEmpty(category) ? label : category + "/" + label;

            _allEntries.Add(new Entry
            {
                Type = t,
                Label = label,
                Category = category,
                FullPath = fullPath,
            });
        }

        void RefreshFilter()
        {
            string filter = _searchField.value?.Trim() ?? "";

            _filteredEntries.Clear();
            if (string.IsNullOrEmpty(filter))
            {
                _filteredEntries.AddRange(_allEntries);
            }
            else
            {
                foreach (var e in _allEntries)
                {
                    if (e.FullPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        _filteredEntries.Add(e);
                }
            }

            _listView.itemsSource = _filteredEntries;
            _listView.Rebuild();
            if (_filteredEntries.Count > 0)
                _listView.selectedIndex = 0;
        }

        static string StripSoSuffix(string name)
        {
            return name.EndsWith("SO") ? name.Substring(0, name.Length - 2) : name;
        }
    }
}

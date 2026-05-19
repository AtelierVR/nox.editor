#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Language;
using Nox.CCK.Mods.Cores;
using Nox.CCK.Mods.Initializers;
using Nox.Editor.Panel;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Nox.Editor {
    public class TranslationsPanel : IEditorModInitializer, Nox.Editor.Panel.IPanel {
        internal IEditorModCoreAPI API;

        public void OnInitializeEditor(IEditorModCoreAPI api) => API = api;
        public void OnDisposeEditor() { Instance?.OnDestroy(); API = null; }

        public string[] GetPath()  => new[] { "editor", "translations" };
        public string   GetLabel() => "Editor/Translations";

        internal TranslationsInstance Instance;

        public IInstance[] GetInstances()
            => Instance != null ? new IInstance[] { Instance } : Array.Empty<IInstance>();

        public IInstance Instantiate(IWindow window, Dictionary<string, object> data) {
            if (Instance != null)
                throw new InvalidOperationException($"{nameof(TranslationsPanel)} only supports a single instance.");
            return Instance = new TranslationsInstance(this, window);
        }

        public void OnUpdateEditor() => Instance?.OnUpdate();
    }

    public class TranslationsInstance : IInstance {
        private readonly TranslationsPanel _panel;
        private readonly IWindow           _window;

        private VisualElement      _content;
        private VisualElement      _packList;
        private Label              _statsLabel;
        private DateTime           _lastUpdate   = DateTime.MinValue;
        private string             _selectedLang = LanguageManager.FallbackLanguage;
        private string             _searchText   = string.Empty;
        private DropdownToolOption _langDropdown;
        private InputToolOption    _searchOption;

        public TranslationsInstance(TranslationsPanel panel, IWindow window) {
            _panel  = panel;
            _window = window;
            LanguageManager.OnPackListUpdated.AddListener(OnPackListChanged);
            LanguageManager.OnLanguageChanged.AddListener(OnLanguageSystemChanged);
        }

        public Nox.Editor.Panel.IPanel GetPanel()  => _panel;
        public IWindow                 GetWindow() => _window;
        public string                  GetTitle()  => "Translations";

        public void OnDestroy() {
            LanguageManager.OnPackListUpdated.RemoveListener(OnPackListChanged);
            LanguageManager.OnLanguageChanged.RemoveListener(OnLanguageSystemChanged);
            _panel.Instance = null;
        }

        public IToolOption[] GetOptions() {
            var languages = GetAllLanguages();

            if (_langDropdown == null) {
                _langDropdown = new DropdownToolOption(
                    "Language",
                    languages.ToList(),
                    languages.Contains(_selectedLang) ? _selectedLang : (languages.FirstOrDefault() ?? LanguageManager.FallbackLanguage),
                    OnLanguageSelected
                );
            } else {
                _langDropdown.Choices.Clear();
                _langDropdown.Choices.AddRange(languages);
                if (!languages.Contains(_langDropdown.Value) && languages.Length > 0)
                    _langDropdown.Select(languages[0]);
            }

            _searchOption ??= new InputToolOption("Search", OnSearchChanged, "Filter by key or value");
            return new IToolOption[] { _langDropdown, _searchOption };
        }

        private string[] GetAllLanguages() {
            var langs = LanguageManager.GetAvailableLanguages();
            return langs.Length > 0 ? langs : new[] { LanguageManager.FallbackLanguage };
        }

        private void OnLanguageSelected(string lang) { _selectedLang = lang; RefreshContent(); }
        private void OnSearchChanged(string search)  { _searchText = search; RefreshContent(); }
        private void OnLanguageSystemChanged(string _) { }

        private void OnPackListChanged() {
            if (_packList == null) return;
            GetOptions();
            RefreshContent();
        }

        public void OnUpdate() {
            if (DateTime.Now - _lastUpdate < TimeSpan.FromSeconds(3)) return;
            if (_packList == null) return;
            _lastUpdate = DateTime.Now;
            RefreshContent();
        }

        public VisualElement GetContent() {
            if (_content != null) return _content;

            _content = _panel.API.AssetAPI.GetAsset<VisualTreeAsset>("translations.uxml").CloneTree();
            _content.style.flexGrow = 1;

            _statsLabel = _content.Q<Label>("stats-label");
            _packList   = _content.Q<VisualElement>("pack-list");

            RefreshContent();
            _lastUpdate = DateTime.Now;
            return _content;
        }

        private void RefreshContent() {
            if (_packList == null) return;
            _packList.Clear();

            var packs        = LanguageManager.GetPacks();
            var filter       = _searchText?.Trim().ToLowerInvariant() ?? string.Empty;
            var totalEntries = 0;
            var totalPacks   = 0;

            foreach (var pack in packs) {
                if (pack == null || pack.languages == null) continue;

                var allKeys = pack.languages
                    .Where(l => l?.entries != null)
                    .SelectMany(l => l.entries)
                    .Where(e => e?.key != null)
                    .Select(e => e.key)
                    .Distinct()
                    .ToList();

                if (allKeys.Count == 0) continue;

                var resolved = allKeys
                    .Select(k => {
                        var r = LanguageManager.GetInPack(pack, k, _selectedLang);
                        return r.HasValue
                            ? ((string key, string value, string sourceLang)?)(k, r.Value.value, r.Value.resolvedLang)
                            : null;
                    })
                    .Where(r => r.HasValue)
                    .Select(r => r.Value)
                    .ToList();

                if (!string.IsNullOrEmpty(filter))
                    resolved = resolved
                        .Where(r => r.key.ToLowerInvariant().Contains(filter) ||
                                    r.value.ToLowerInvariant().Contains(filter))
                        .ToList();

                if (resolved.Count == 0) continue;

                totalPacks++;
                totalEntries += resolved.Count;
                _packList.Add(BuildPackSection(pack, resolved));
            }

            if (_statsLabel != null)
                _statsLabel.text = $"{totalEntries} entries in {totalPacks}/{packs.Length} packs  ·  {_selectedLang}";

            if (_packList.childCount == 0) {
                var empty = new Label(string.IsNullOrEmpty(filter)
                    ? $"No translations found for language '{_selectedLang}'."
                    : $"No results for \"{_searchText}\" in '{_selectedLang}'.");
                empty.AddToClassList("text-center");
                empty.AddToClassList("mt-16");
                empty.AddToClassList("opacity-75");
                _packList.Add(empty);
            }
        }

        private VisualElement BuildPackSection(
                LanguagePack pack,
                List<(string key, string value, string sourceLang)> entries) {
            var section = _panel.API.AssetAPI.GetAsset<VisualTreeAsset>("translation-pack.uxml").CloneTree();
            var header  = section.Q<VisualElement>("pack-header");
            var objField = new ObjectField { objectType = typeof(LanguagePack), value = pack };
            objField.RegisterValueChangedCallback(e => objField.SetValueWithoutNotify(pack));
            objField.AddToClassList("flex-grow");
            header.Add(objField);
            var entriesContainer = section.Q<VisualElement>("entries");
            foreach (var entry in entries)
                entriesContainer.Add(BuildEntryRow(entry));
            return section;
        }

        private VisualElement BuildEntryRow((string key, string value, string sourceLang) entry) {
            var row = _panel.API.AssetAPI.GetAsset<VisualTreeAsset>("translation-entry.uxml").CloneTree();

            var keyField = row.Q<TextField>("entry-key");
            keyField.value      = entry.key;
            keyField.isReadOnly = true;

            var valField = row.Q<TextField>("entry-value");
            valField.value      = entry.value;
            valField.isReadOnly = true;

            var badge = row.Q<Label>("entry-badge");
            if (entry.sourceLang != _selectedLang) {
                badge.text    = entry.sourceLang;
                badge.tooltip = $"Fallback from '{entry.sourceLang}' (not available in '{_selectedLang}')";
                badge.RemoveFromClassList("hidden");
            }

            return row;
        }
    }
}
#endif

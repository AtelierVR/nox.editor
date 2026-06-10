#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nox.CCK.Mods;
using Nox.CCK.Mods.Cores;
using Nox.CCK.Mods.Initializers;
using Nox.CCK.Mods.Metadata;
using Nox.Editor.Panel;
using UnityEditor;
using UnityEngine.UIElements;

namespace Nox.Editor {
    public class ModDetails : IEditorModInitializer, Nox.Editor.Panel.IPanel {
        internal IEditorModCoreAPI API;

        public void OnInitializeEditor(IEditorModCoreAPI api) => API = api;
        public void OnDisposeEditor() { Instance?.OnDestroy(); API = null; }

        public string[] GetPath()  => new[] { "editor", "mods" };
        public string   GetLabel() => "Editor/Mods";

        internal ModDetailsInstance Instance;

        public IInstance[] GetInstances()
            => Instance != null ? new IInstance[] { Instance } : Array.Empty<IInstance>();

        public IInstance Instantiate(IWindow window, Dictionary<string, object> data) {
            if (Instance != null)
                throw new InvalidOperationException($"{nameof(ModDetails)} only supports a single instance.");
            return Instance = new ModDetailsInstance(this, window);
        }

        public void OnUpdateEditor() => Instance?.OnUpdate();
    }

    public class ModDetailsInstance : IInstance {
        private readonly ModDetails   _panel;
        private readonly IWindow      _window;
        private          VisualElement _content;
        private          bool          _autoRefresh  = true;
        private          bool          _showDisabled = true;
        private          DateTime      _lastUpdate   = DateTime.MinValue;
        private          VisualElement _modsList;
        private          Label         _modCountLabel;

        private class ModUserData {
            internal readonly string ModId;
            internal          DateTime LastModified = DateTime.MinValue;
            internal ModUserData(string id) => ModId = id;
            public   bool Equals(string id) => ModId == id;
        }

        public ModDetailsInstance(ModDetails panel, IWindow window) {
            _panel  = panel;
            _window = window;
        }

        public Nox.Editor.Panel.IPanel  GetPanel()  => _panel;
        public IWindow GetWindow() => _window;
        public string  GetTitle()  => "Mod Details";

        public void OnDestroy() => _panel.Instance = null;

        public IToolOption[] GetOptions()
            => new IToolOption[] { new DefaultToolOption("Refresh", RefreshModsList) };

        public void OnUpdate() {
            if (!_autoRefresh) return;
            if (DateTime.Now - _lastUpdate < TimeSpan.FromSeconds(2)) return;
            if (_modsList == null) return;

            var mods         = _panel.API.ModAPI.GetMods();
            var filteredMods = _showDisabled ? mods : mods.Where(m => m.IsLoaded()).ToArray();

            foreach (var mod in filteredMods) {
                var meta  = mod.GetMetadata();
                var child = GetModElement(meta.GetId());
                if (child != null) {
                    var ud = child.userData as ModUserData;
                    if (ud != null && DateTime.Now - ud.LastModified > TimeSpan.FromSeconds(5)) {
                        UpdateModItem(child, mod);
                        ud.LastModified = DateTime.Now;
                    }
                } else {
                    AddModItem(mod);
                }
            }

            var toRemove = new List<VisualElement>();
            foreach (var child in _modsList.Children()) {
                if (child.userData is ModUserData ud && !filteredMods.Any(m => ud.Equals(m.GetMetadata().GetId())))
                    toRemove.Add(child);
            }
            toRemove.ForEach(c => _modsList.Remove(c));

            _modCountLabel.text = $"Mods: {mods.Count(m => m.IsLoaded())}/{mods.Length} enabled";
            _lastUpdate         = DateTime.Now;
        }

        public VisualElement GetContent() {
            if (_content != null) return _content;

            _content = _panel.API.AssetAPI.GetAsset<VisualTreeAsset>("mod_details.uxml").CloneTree();
            _content.AddToClassList("flex-fill");

            _modsList           = _content.Q<VisualElement>("mods-list");
            _modCountLabel      = _content.Q<Label>("mod-count");

            var autoRefresh     = _content.Q<Toggle>("auto-refresh");
            autoRefresh.SetValueWithoutNotify(_autoRefresh);
            autoRefresh.RegisterCallback<ChangeEvent<bool>>(e => _autoRefresh = e.newValue);

            var showDisabled    = _content.Q<Toggle>("show-disabled");
            showDisabled.SetValueWithoutNotify(_showDisabled);
            showDisabled.RegisterCallback<ChangeEvent<bool>>(e => { _showDisabled = e.newValue; RefreshModsList(); });

            _content.Q<Button>("refresh-button")?.RegisterCallback<ClickEvent>(_ => RefreshModsList());

            RefreshModsList();
            return _content;
        }

        private void RefreshModsList() {
            _modsList.Clear();
            var mods         = _panel.API.ModAPI.GetMods();
            var filteredMods = _showDisabled ? mods : mods.Where(m => m.IsLoaded()).ToArray();
            _modCountLabel.text = $"Mods: {mods.Count(m => m.IsLoaded())}/{mods.Length} enabled";
            foreach (var mod in filteredMods) AddModItem(mod);
            _lastUpdate = DateTime.Now;
        }

        private VisualElement GetModElement(string modId)
            => _modsList.Children().FirstOrDefault(c => c.userData is ModUserData ud && ud.Equals(modId));

        private void AddModItem(IMod mod) {
            var meta  = mod.GetMetadata();
            var child = GetModElement(meta.GetId());
            if (child != null) { UpdateModItem(child, mod); return; }

            child          = _panel.API.AssetAPI.GetAsset<VisualTreeAsset>("mod_item.uxml").CloneTree();
            child.userData = new ModUserData(meta.GetId());
            UpdateModItem(child, mod);
            _modsList.Add(child);
        }

        private void UpdateModItem(VisualElement item, IMod mod) {
            var meta = mod.GetMetadata();

            item.Q<Label>("mod-id").text      = meta.GetId();
            item.Q<Label>("mod-version").text = "v" + meta.GetVersion();
            item.Q<Label>("mod-name").text    = meta.GetName() ?? "";
            item.Q<Label>("mod-license").text = meta.GetLicense() ?? "";

            var statusLoaded = item.Q("status-loaded");
            if (statusLoaded != null) {
                statusLoaded.EnableInClassList("is-loaded",   mod.IsLoaded());
                statusLoaded.EnableInClassList("is-unloaded", !mod.IsLoaded());
            }

            SetDotVisible(item, "status-main",   HasEntries(mod, "main"));
            SetDotVisible(item, "status-client", HasEntries(mod, "client"));
            SetDotVisible(item, "status-server", HasEntries(mod, "server"));
            SetDotVisible(item, "status-editor", HasEntries(mod, "editor"));
            SetDotVisible(item, "status-custom", HasEntries(mod, "custom"));

            var providesList = item.Q<VisualElement>("provides-list");
            providesList.Clear();
            foreach (var p in meta.GetProvides()) {
                var el = _panel.API.AssetAPI.GetAsset<VisualTreeAsset>("provide-item.uxml").CloneTree();
                el.Q<Label>("provide-text").text = "• " + p;
                providesList.Add(el);
            }

            UpdateEntrySection(item, mod, "main");
            UpdateEntrySection(item, mod, "client");
            UpdateEntrySection(item, mod, "server");
            UpdateEntrySection(item, mod, "editor");
            UpdateEntrySection(item, mod, "custom");

            var selectBtn = item.Q<Button>("select-button");
            if (selectBtn != null && selectBtn.userData == null) {
                selectBtn.userData = "init";
                selectBtn.RegisterCallback<ClickEvent>(_ => {
                    var path = mod.GetData("manifest", "");
                    if (File.Exists(path)) EditorUtility.RevealInFinder(path);
                    else EditorUtility.DisplayDialog("Error", "Manifest file not found!", "Ok");
                });
            }
        }

        private void UpdateEntrySection(VisualElement item, IMod mod, string section) {
            var container = item.Q<VisualElement>($"{section}-entries");
            var list      = item.Q<VisualElement>($"{section}-list");
            if (container == null || list == null) return;

            var entries = GetEntries(mod, section);
            container.EnableInClassList("hidden", entries.Length == 0);
            list.Clear();
            foreach (var e in entries) {
                var el = _panel.API.AssetAPI.GetAsset<VisualTreeAsset>("entry-item.uxml").CloneTree();
                el.Q<Label>("entry-text").text = "• " + e;
                list.Add(el);
            }
        }

        private static void SetDotVisible(VisualElement item, string name, bool visible) {
            item.Q(name)?.EnableInClassList("hidden", !visible);
        }

        private static bool HasEntries(IMod mod, string section) => GetEntries(mod, section).Length > 0;

        private static string[] GetEntries(IMod mod, string section) {
            try {
                var ep = mod.GetMetadata().GetEntryPoints();
                return ep?.Has(section) == true
                    ? ep.Get(section).Select(e => e.FullName).ToArray()
                    : Array.Empty<string>();
            } catch { return Array.Empty<string>(); }
        }
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nox.CCK.Mods;
using Nox.CCK.Mods.Cores;
using Nox.CCK.Mods.Initializers;
using Nox.CCK.Mods.Metadata;
using Nox.CCK.Mods.Panels;
using Nox.CCK.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nox.Editor {
	public class ModDetails : IEditorModInitializer {
		internal static IEditorModCoreAPI CoreAPI;
		private         IEditorPanel      _buildPanel;

		public void OnInitializeEditor(IEditorModCoreAPI api) {
			CoreAPI = api;
			var panel = new ModDetailsPanel();
			_buildPanel = api.PanelAPI.AddLocalPanel(panel);
		}

		public void OnDispose() {
			CoreAPI.PanelAPI.RemoveLocalPanel(_buildPanel);
			CoreAPI = null;
		}
	}

	public class ModDetailsPanel : IEditorPanelBuilder {
		public string GetId()
			=> "mod_details";

		public string GetName()
			=> "Dev/Mod Details";

		public bool IsHidden()
			=> false;

		private readonly VisualElement _root         = new();
		private          DateTime      _lastUpdate   = DateTime.MinValue;
		private          bool          _autoRefresh  = true;
		private          bool          _showDisabled = true;

		private class ModUserData {
			private readonly string   _modId;
			private readonly string   _version;
			internal         DateTime LastModified { get; set; } = DateTime.MinValue;

			internal ModUserData(IMod mod) {
				var meta = mod.GetMetadata();
				_modId   = meta.GetId();
				_version = meta.GetVersion().ToString(); // Convert Version to string
			}

			public bool Equals(string modId)
				=> _modId == modId;

			public bool NeedsUpdate(IMod mod) {
				var meta = mod.GetMetadata();
				return _version != meta.GetVersion().ToString() || DateTime.Now - LastModified > TimeSpan.FromSeconds(5);
			}

			public override string ToString()
				=> _modId;
		}

		public VisualElement Make(Dictionary<string, object> data) {
			_root.ClearBindings();
			_root.Clear();

			// Load the main UXML template
			_root.Add(ModDetails.CoreAPI.AssetAPI.GetAsset<VisualTreeAsset>("mod_details.uxml").CloneTree());

			// Set version
			_root.Q<Label>("version").text = "v" + EventLogger.CoreAPI.ModMetadata.GetVersion();

			// Setup event handlers
			SetupEventHandlers();

			// Initial refresh
			_lastUpdate = DateTime.MinValue;
			RefreshModsList();

			return _root;
		}

		private void SetupEventHandlers() {
			_root.Q<Button>("refresh-button").clicked += RefreshModsList;

			var autoRefreshToggle = _root.Q<Toggle>("auto-refresh");
			autoRefreshToggle.value = _autoRefresh;
			autoRefreshToggle.RegisterValueChangedCallback(evt => _autoRefresh = evt.newValue);

			var showDisabledToggle = _root.Q<Toggle>("show-disabled");
			showDisabledToggle.value = _showDisabled;
			showDisabledToggle.RegisterValueChangedCallback(
				evt => {
					_showDisabled = evt.newValue;
					RefreshModsList();
				}
			);
		}

		private VisualElement GetModElement(string modId)
			=> _root.Q<VisualElement>("mods-list")
				?.Children()
				.FirstOrDefault(c => c.userData is ModUserData data && data.Equals(modId));

		public void OnUpdate() {
			if (!_autoRefresh) return;
			if (DateTime.Now - _lastUpdate < TimeSpan.FromSeconds(2)) return;

			var mods         = ModDetails.CoreAPI.ModAPI.GetMods();
			var filteredMods = _showDisabled ? mods : mods.Where(m => m.IsLoaded()).ToArray();

			// Update existing items or add new ones
			foreach (var mod in filteredMods) {
				var meta  = mod.GetMetadata();
				var child = GetModElement(meta.GetId());
				if (child != null) {
					var userData = child.userData as ModUserData;
					if (userData?.NeedsUpdate(mod) == true) {
						UpdateModItem(child, mod);
						userData.LastModified = DateTime.Now;
					}
				} else {
					OnModAdded(mod);
				}
			}

			// Remove items for mods that no longer exist or are filtered out
			var modsList = _root.Q<VisualElement>("mods-list");
			var toRemove = new List<VisualElement>();
			foreach (var child in modsList?.Children() ?? Enumerable.Empty<VisualElement>()) {
				if (child.userData is ModUserData userData) {
					var modExists = filteredMods.Any(
						m => {
							var meta = m.GetMetadata();
							return userData.Equals(meta.GetId());
						}
					);
					if (!modExists) toRemove.Add(child);
				}
			}

			toRemove.ForEach(OnModRemoved);

			// Update mod count
			var enabledCount = mods.Count(m => m.IsLoaded());
			_root.Q<Label>("mod-count").text = $"Mods: {enabledCount}/{mods.Length} enabled";

			_lastUpdate = DateTime.Now;
		}

		private void RefreshModsList() {
			var mods     = ModDetails.CoreAPI.ModAPI.GetMods();
			var modsList = _root.Q<VisualElement>("mods-list");

			// Clear existing items
			modsList.Clear();

			// Update mod count
			var enabledCount = mods.Count(m => m.IsLoaded());
			_root.Q<Label>("mod-count").text = $"Mods: {enabledCount}/{mods.Length} enabled";

			// Filter mods if needed
			var filteredMods = _showDisabled ? mods : mods.Where(m => m.IsLoaded()).ToArray();

			foreach (var mod in filteredMods) {
				OnModAdded(mod);
			}

			_lastUpdate = DateTime.Now;
		}

		private void OnModAdded(IMod mod) {
			var modsList = _root.Q<VisualElement>("mods-list");
			if (modsList == null) return;

			var meta  = mod.GetMetadata();
			var child = GetModElement(meta.GetId());
			if (child != null) {
				UpdateModItem(child, mod);
				return;
			}

			var modItemTemplate = ModDetails.CoreAPI.AssetAPI.GetAsset<VisualTreeAsset>("mod_item.uxml");
			child = modItemTemplate.CloneTree();

			var userData = new ModUserData(mod);
			child.userData = userData;

			UpdateModItem(child, mod);
			modsList.Add(child);
			userData.LastModified = DateTime.Now;
		}

		private void OnModRemoved(VisualElement child) {
			_root.Q<VisualElement>("mods-list")?.Remove(child);
			child.RemoveFromHierarchy();
		}

		private void UpdateModItem(VisualElement item, IMod mod) {
			if (item == null || mod == null) return;

			var meta = mod.GetMetadata();

			// Set basic info
			item.Q<Label>("mod-id").text      = meta.GetId();
			item.Q<Label>("mod-version").text = "v" + meta.GetVersion();
			item.Q<Label>("mod-name").text    = meta.GetName();
			item.Q<Label>("mod-license").text = meta.GetLicense();

			// Setup status indicators
			SetupStatusIndicators(item, mod);

			// Setup provides list
			SetupProvidesList(item, meta);

			// Setup entry points
			SetupEntryPoints(item, mod);

			// Setup select button (only once)
			var selectButton = item.Q<Button>("select-button");
			if (selectButton != null && selectButton.userData == null) {
				selectButton.userData = "initialized";
				selectButton.clicked += () => {
					var manifestPath = mod.GetData("manifest", "");
					if (File.Exists(manifestPath))
						EditorUtility.RevealInFinder(manifestPath);
					else
						EditorUtility.DisplayDialog("Error", "Manifest file not found!", "Ok");
				};
			}
		}

		private void SetupStatusIndicators(VisualElement item, object mod) {
			if (item == null || mod == null) return;

			try {
				var modType = mod.GetType();

				// Safe method invocation with null checks
				var isLoadedMethod = modType.GetMethod("IsLoaded");
				var isLoaded       = isLoadedMethod != null && (bool)isLoadedMethod.Invoke(mod, null);

				var getMainsMethod = modType.GetMethod("GetMains");
				var mains          = getMainsMethod?.Invoke(mod, null) as Array;
				var mainCount      = mains?.Length ?? 0;

				var getClientsMethod = modType.GetMethod("GetClients");
				var clients          = getClientsMethod?.Invoke(mod, null) as Array;
				var clientCount      = clients?.Length ?? 0;

				var getServersMethod = modType.GetMethod("GetServers");
				var servers          = getServersMethod?.Invoke(mod, null) as Array;
				var serverCount      = servers?.Length ?? 0;

				var getEditorsMethod = modType.GetMethod("GetEditors");
				var editors          = getEditorsMethod?.Invoke(mod, null) as Array;
				var editorCount      = editors?.Length ?? 0;

				var getCustomEntriesMethod = modType.GetMethod("GetCustomEntries");
				var customEntries          = getCustomEntriesMethod?.Invoke(mod, null) as Array;
				var customCount            = customEntries?.Length ?? 0;

				// Update status indicators visibility and colors with null checks
				var statusLoaded = item.Q("status-loaded");
				if (statusLoaded != null) {
					statusLoaded.style.backgroundColor = isLoaded ? new Color(0, 1, 0) : new Color(0.5f, 0.5f, 0.5f);
				}

				var statusMain = item.Q("status-main");
				if (statusMain != null) {
					statusMain.style.display = mainCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
				}

				var statusClient = item.Q("status-client");
				if (statusClient != null) {
					statusClient.style.display = clientCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
				}

				var statusServer = item.Q("status-server");
				if (statusServer != null) {
					statusServer.style.display = serverCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
				}

				var statusEditor = item.Q("status-editor");
				if (statusEditor != null) {
					statusEditor.style.display = editorCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
				}

				var statusCustom = item.Q("status-custom");
				if (statusCustom != null) {
					statusCustom.style.display = customCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
				}
			} catch (System.Exception ex) {
				UnityEngine.Debug.LogError($"Error in SetupStatusIndicators: {ex.Message}");
			}
		}

		private void SetupProvidesList(VisualElement item, IModMetadata meta) {
			var providesList = item.Q<VisualElement>("provides-list");
			var provides     = meta.GetProvides();

			providesList.Clear();
			if (provides.Length == 0) {
				providesList.Add(new Label("None") { style = { color = new Color(0.7f, 0.7f, 0.7f) } });
			} else {
				foreach (var provide in provides) {
					var label = new Label($"• {provide}") {
						style = {
							fontSize = 11,
							color    = new Color(0.8f, 0.8f, 0.8f)
						}
					};
					providesList.Add(label);
				}
			}
		}

		private void SetupEntryPoints(VisualElement item, object mod) {
			if (item == null || mod == null) return;

			try {
				var modType = mod.GetType();

				// Safe method invocation for entry points
				var getMainsMethod = modType.GetMethod("GetMains");
				var mains          = getMainsMethod?.Invoke(mod, null) as Array;
				SetupEntryPointSection(item, "main", mains);

				var getClientsMethod = modType.GetMethod("GetClients");
				var clients          = getClientsMethod?.Invoke(mod, null) as Array;
				SetupEntryPointSection(item, "client", clients);

				var getServersMethod = modType.GetMethod("GetServers");
				var servers          = getServersMethod?.Invoke(mod, null) as Array;
				SetupEntryPointSection(item, "server", servers);

				var getEditorsMethod = modType.GetMethod("GetEditors");
				var editors          = getEditorsMethod?.Invoke(mod, null) as Array;
				SetupEntryPointSection(item, "editor", editors);

				// Setup custom entries with null checks
				var getCustomEntriesMethod = modType.GetMethod("GetCustomEntries");
				var customEntries          = getCustomEntriesMethod?.Invoke(mod, null) as Array;
				var customContainer        = item.Q<VisualElement>("custom-entries");
				var customList             = item.Q<VisualElement>("custom-list");

				if (customContainer == null || customList == null || customEntries == null || customEntries.Length == 0) {
					if (customContainer != null) {
						customContainer.style.display = DisplayStyle.None;
					}
				} else {
					customContainer.style.display = DisplayStyle.Flex;
					customList.Clear();

					foreach (var customEntry in customEntries) {
						if (customEntry == null) continue;

						var getCustomMethod = modType.GetMethod("GetCustom");
						var customArray     = getCustomMethod?.Invoke(mod, new object[] { customEntry }) as Array;

						var isCustomEnabledMethod = modType.GetMethod("IsCustomEnabled");
						var isEnabled             = isCustomEnabledMethod != null && (bool)isCustomEnabledMethod.Invoke(mod, new object[] { customEntry });

						var entryLabel = new Label($"{customEntry} ({(isEnabled ? "enabled" : "disabled")}):");
						entryLabel.style.fontSize = 11;
						entryLabel.style.color    = isEnabled ? new Color(1, 1, 1) : new Color(0.6f, 0.6f, 0.6f);
						customList.Add(entryLabel);

						if (customArray != null) {
							foreach (var entry in customArray) {
								if (entry == null) continue;
								var label = new Label($"  • {entry}");
								label.style.fontSize = 10;
								label.style.color    = new Color(0.8f, 0.8f, 0.8f);
								customList.Add(label);
							}
						}
					}
				}
			} catch (System.Exception ex) {
				UnityEngine.Debug.LogError($"Error in SetupEntryPoints: {ex.Message}");
			}
		}

		private void SetupEntryPointSection(VisualElement item, string sectionName, Array entries) {
			if (item == null || string.IsNullOrEmpty(sectionName)) return;

			try {
				var container = item.Q<VisualElement>($"{sectionName}-entries");
				var list      = item.Q<VisualElement>($"{sectionName}-list");

				if (container == null || list == null || entries == null || entries.Length == 0) {
					if (container != null) {
						container.style.display = DisplayStyle.None;
					}
				} else {
					container.style.display = DisplayStyle.Flex;
					list.Clear();
					foreach (var entry in entries) {
						if (entry == null) continue;
						var label = new Label($"• {entry}");
						label.style.fontSize = 10;
						label.style.color    = new Color(0.8f, 0.8f, 0.8f);
						list.Add(label);
						UnityEngine.Debug.Log($"[EntryPoint] Added {sectionName} entry: {entry}");
					}
				}
			} catch (System.Exception ex) {
				UnityEngine.Debug.LogError($"Error in SetupEntryPointSection for {sectionName}: {ex.Message}");
			}
		}
	}
}
#endif
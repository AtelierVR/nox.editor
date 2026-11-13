#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Nox.CCK.Mods.Cores;
using Nox.CCK.Mods.Initializers;
using Nox.CCK.Theme;
using Nox.CCK.Utils;
using Nox.Editor.Panel;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using IPanel = Nox.Editor.Panel.IPanel;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Editor {
	/// <summary>
	/// Editor panel of the themes: the colour swaps of a <see cref="ThemeAsset"/>, editable live and saved into
	/// the asset itself.
	/// <para>
	/// One asset per theme: the panel edits the asset the user picks, or one of the built-in themes (edited in
	/// place, since the manager reads those very instances, so a colour changed here repaints the open menu at
	/// once in Play mode). "New asset from this theme..." copies the theme being edited into an asset, which
	/// then takes precedence over the built-in one with the same id.
	/// </para>
	/// </summary>
	public class ThemePanel : IEditorModInitializer, IPanel {
		/// <summary>Path of the panel in the navigation tree of the panel window.</summary>
		private static readonly string[] _panelPath = { "editor", "theme" };

		/// <summary>Editor API of the mod: also what the menu entry needs to reach the panel framework.</summary>
		internal static IEditorModCoreAPI API;
		internal ThemeInstance Instance;

		public void OnInitializeEditor(IEditorModCoreAPI api)
			=> API = api;

		public void OnDisposeEditor() {
			Instance?.OnDestroy();
			API = null;
		}

		public void OnUpdateEditor() { }

		public string[] GetPath()
			=> _panelPath;

		public string GetLabel()
			=> "Editor/Theme";

		public IInstance[] GetInstances()
			=> Instance != null
				? new IInstance[] { Instance }
				: Array.Empty<IInstance>();

		public IInstance Instantiate(IWindow window, Dictionary<string, object> data) {
			if (Instance != null)
				throw new InvalidOperationException($"{nameof(ThemePanel)} only supports a single instance.");

			return Instance = new ThemeInstance(this, window);
		}
	}

	/// <summary>Opens the theme panel from the menu, when the panel framework is available.</summary>
	internal static class ThemePanelMenu {
		private const int PRIORITY = 995;

		[MenuItem("Nox/Theme/Theme Panel...", false, PRIORITY)]
		private static void Open() {
			var panels = ThemePanel.API?.ModAPI
				?.GetMod("editor.panel")
				?.GetInstance<IPanelAPI>();
			if (panels == null) {
				Logger.LogWarning("The Nox panel framework (nox.editor.panel) is not available.", tag: nameof(ThemePanelMenu));
				return;
			}

			foreach (var panel in panels.GetPanels())
				if (panel is ThemePanel theme) {
					panels.TryOpen(theme);
					return;
				}

			Logger.LogWarning("The theme panel is not registered.", tag: nameof(ThemePanelMenu));
		}
	}

	public class ThemeInstance : IInstance {
		private readonly ThemePanel _panel;
		private readonly IWindow _window;

		private VisualElement _content;
		private VisualElement _rows;
		private VisualElement _themeHost;
		private PopupField<string> _themes;
		private ObjectField _themeField;
		private Label _hint;

		/// <summary>Theme being edited: an asset the user picked, or one of the built-in ones.</summary>
		private ThemeAsset _theme;

		public ThemeInstance(ThemePanel panel, IWindow window) {
			_panel = panel;
			_window = window;
		}

		public IPanel GetPanel()
			=> _panel;

		public IWindow GetWindow()
			=> _window;

		public string GetTitle()
			=> "Theme";

		public void OnDestroy() {
			if (_panel != null)
				_panel.Instance = null;
		}

		public VisualElement GetContent() {
			if (_content != null)
				return _content;

			_content = new VisualElement();
			_content.style.flexGrow = 1;

			var bar = new VisualElement();
			bar.style.flexDirection = FlexDirection.Row;
			bar.style.alignItems = Align.Center;
			bar.style.marginBottom = 4;

			_themeField = new ObjectField("Theme asset") {
				objectType = typeof(ThemeAsset),
				allowSceneObjects = false
			};
			_themeField.style.flexGrow = 1;
			_themeField.RegisterValueChangedCallback(evt => SelectAsset(evt.newValue as ThemeAsset));
			bar.Add(_themeField);

			var create = new Button(NewFromCurrent) { text = "New asset from this theme..." };
			create.style.marginLeft = 4;
			bar.Add(create);

			var save = new Button(() => AssetDatabase.SaveAssets()) { text = "Save" };
			save.style.marginLeft = 4;
			bar.Add(save);

			_content.Add(bar);

			_themeHost = new VisualElement();
			_content.Add(_themeHost);

			_hint = new Label();
			_hint.style.whiteSpace = WhiteSpace.Normal;
			_hint.style.marginBottom = 4;
			_hint.style.opacity = 0.75f;
			_content.Add(_hint);

			_rows = new ScrollView();
			_rows.style.flexGrow = 1;
			_content.Add(_rows);

			var add = new Button(AddSwap) { text = "Add colour" };
			add.style.marginTop = 4;
			_content.Add(add);

			RefreshThemes();
			return _content;
		}

		/// <summary>No toolbar option: everything the panel needs is inside its content.</summary>
		public IToolOption[] GetOptions()
			=> Array.Empty<IToolOption>();

		#region Theme

		/// <summary>Picks the theme to edit, and lets it take precedence when it is an asset.</summary>
		private void SelectAsset(ThemeAsset theme) {
			_theme = theme;

			if (_themeField != null && _themeField.value != _theme)
				_themeField.SetValueWithoutNotify(_theme);

			if (AssetDatabase.Contains(_theme))
				ThemeManager.Register(_theme);

			RefreshThemes();
		}

		/// <summary>Picks a theme from the list: only the rows change, the list itself stays.</summary>
		private void SelectTheme(string id) {
			_theme = ThemeManager.Find(id);

			if (_themeField != null)
				_themeField.SetValueWithoutNotify(_theme);

			if (AssetDatabase.Contains(_theme))
				ThemeManager.Register(_theme);

			Rebuild();
		}

		/// <summary>Copies the theme being edited into a new asset, so its colours can be kept.</summary>
		private void NewFromCurrent() {
			if (!_theme)
				return;

			var path = EditorUtility.SaveFilePanelInProject(
				"Create theme asset",
				_theme.id,
				"asset",
				"Where to save the theme"
			);

			if (string.IsNullOrEmpty(path))
				return;
				
			var copy = _theme.Copy();
			copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
			copy.hideFlags = HideFlags.None;
			AssetDatabase.CreateAsset(copy, path);
			AssetDatabase.SaveAssets();
			SelectAsset(copy);
		}

		private void RefreshThemes() {
			if (_themeHost == null)
				return;

			var ids = new List<string>();
			foreach (var (id, _) in ThemeManager.GetAvailableThemes())
				ids.Add(id);

			_themeHost.Clear();
			_themes = null;

			if (ids.Count == 0) {
				_theme = null;
				Rebuild();
				return;
			}

			if (!_theme)
				_theme = ThemeManager.Find(ids[0]);

			var wanted = _theme && ids.Contains(_theme.id)
				? _theme.id
				: ids[0];

			_themes = new PopupField<string>("Theme", ids, Mathf.Max(0, ids.IndexOf(wanted)));
			_themes.style.marginBottom = 4;
			_themes.RegisterValueChangedCallback(evt => SelectTheme(evt.newValue));
			_themeHost.Add(_themes);

			Rebuild();
		}

		private void Rebuild() {
			if (_rows == null)
				return;

			_rows.Clear();

			if (!_theme) {
				_rows.Add(new Label("No theme to edit."));
				UpdateHint();
				return;
			}

			_theme.colors ??= new List<ThemeAsset.Swap>();

			for (var i = 0; i < _theme.colors.Count; i++)
				_rows.Add(BuildSwap(i, _theme.colors[i]));

			UpdateHint();
		}

		private VisualElement BuildSwap(int index, ThemeAsset.Swap swap) {
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.alignItems = Align.Center;
			row.style.marginBottom = 2;

			var from = new ColorField("from") {
				value = swap.from,
				showAlpha = true,
				hdr = true
			};
			from.style.flexGrow = 1;
			from.RegisterValueChangedCallback(evt => {
				swap.from = evt.newValue;
				Changed();
			});
			row.Add(from);

			var to = new ColorField("to") {
				value = swap.to,
				showAlpha = true,
				hdr = true
			};
			to.style.flexGrow = 1;
			to.RegisterValueChangedCallback(evt => {
				swap.to = evt.newValue;
				Changed();
			});
			row.Add(to);

			var remove = new Button(() => {
				_theme.colors.RemoveAt(index);
				Rebuild();
				Changed();
			}) { text = "−" };
			remove.style.width = 22;
			remove.style.marginLeft = 4;
			row.Add(remove);

			return row;
		}

		private void AddSwap() {
			if (!_theme)
				return;

			_theme.colors ??= new List<ThemeAsset.Swap>();
			_theme.colors.Add(new ThemeAsset.Swap());
			Rebuild();
			Changed();
		}

		private void UpdateHint() {
			if (_hint == null)
				return;

			_hint.text = AssetDatabase.Contains(_theme)
				? $"Saved in {AssetDatabase.GetAssetPath(_theme)}. An asset takes precedence over the built-in theme with the same id."
				: "Built-in theme: the changes apply to this session only. Use 'New asset from this theme...' to keep them.";
		}

		/// <summary>Applies the edit: the asset is marked dirty, the open menu repaints, nothing is polled.</summary>
		private void Changed() {
			if (AssetDatabase.Contains(_theme))
				EditorUtility.SetDirty(_theme);

			if (Application.isPlaying)
				ThemeManager.ApplyAll();

			UpdateHint();
		}

		#endregion
	}
}
#endif

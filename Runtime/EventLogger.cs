#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Mods;
using Nox.CCK.Mods.Cores;
using Nox.CCK.Mods.Events;
using Nox.CCK.Mods.Initializers;
using Nox.Editor.Panel;
using UnityEngine.UIElements;

namespace Nox.Editor {
	public class EventLogger : IEditorModInitializer, Panel.IPanel {
		internal IEditorModCoreAPI API;
		readonly internal List<EventData> History = new();
		internal const uint MaxLogs = byte.MaxValue;
		private EventSubscription _subscription;

		public void OnInitializeEditor(IEditorModCoreAPI api) {
			API           = api;
			_subscription = api.EventAPI.Subscribe(null, OnReceiveLog);
		}

		public void OnDisposeEditor() {
			API?.EventAPI.Unsubscribe(_subscription);
			History.Clear();
			API = null;
		}

		public string[] GetPath()  => new[] { "editor", "logger" };
		public string   GetLabel() => "Editor/Logger";

		internal EventLoggerInstance Instance;

		public IInstance[] GetInstances()
			=> Instance != null ? new IInstance[] { Instance } : Array.Empty<IInstance>();

		public IInstance Instantiate(IWindow window, Dictionary<string, object> data) {
			if (Instance != null)
				throw new InvalidOperationException($"{nameof(EventLogger)} only supports a single instance.");
			return Instance = new EventLoggerInstance(this, window);
		}

		internal void OnReceiveLog(EventData ctx) {
			History.Add(ctx);
			while (History.Count > MaxLogs) History.RemoveAt(0);
			Instance?.AppendLogEntry(ctx);
		}
	}

	public class EventLoggerInstance : IInstance {
		private readonly EventLogger   _panel;
		private readonly IWindow       _window;
		private          VisualElement _content;
		private          VisualElement _logsContainer;
		private          string        _filter = string.Empty;

		public EventLoggerInstance(EventLogger panel, IWindow window) {
			_panel  = panel;
			_window = window;
		}

		public Nox.Editor.Panel.IPanel  GetPanel()  => _panel;
		public IWindow GetWindow() => _window;
		public string  GetTitle()  => "Event Logger";

		public void OnDestroy() => _panel.Instance = null;

		public IToolOption[] GetOptions()
			=> new IToolOption[] {
				new InputToolOption("Filter", OnFilterChanged),
				new DefaultToolOption("Clear", OnClear)
			};

		private void OnFilterChanged(string value) {
			_filter = value ?? string.Empty;
			ApplyFilter();
		}

		private void ApplyFilter() {
			if (_logsContainer == null) return;
			foreach (var child in _logsContainer.Children()) {
				var foldout = child.Q<Foldout>("entry-foldout");
				var text    = foldout?.text ?? string.Empty;
				var visible = string.IsNullOrEmpty(_filter)
					|| text.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;
				child.EnableInClassList("hidden", !visible);
			}
		}

		public VisualElement GetContent() {
			if (_content != null) return _content;

			_content = _panel.API.AssetAPI.GetAsset<VisualTreeAsset>("logger.uxml").CloneTree();
			_content.AddToClassList("flex-fill");
			_logsContainer = _content.Q<VisualElement>("logs");

			foreach (var ctx in _panel.History)
				AppendLogEntry(ctx, false);

			ApplyFilter();
			return _content;
		}

		internal void AppendLogEntry(EventData ctx, bool trim = true) {
			if (_logsContainer == null) return;

			var entry   = _panel.API.AssetAPI.GetAsset<VisualTreeAsset>("log-entry.uxml").CloneTree();
			var foldout = entry.Q<Foldout>("entry-foldout");
			if (foldout != null) foldout.text = CustomLabel(ctx);

			entry.Q<Label>("source-mod").text     = $"{ctx.Source.GetMetadata().GetId()}@{ctx.Source.GetMetadata().GetVersion()}";
			entry.Q<Label>("source-channel").text = ctx.SourceChannel.ToString();
			entry.Q<Label>("data-count").text     = $"Data ({ctx.Data.Length})";

			var dataList = entry.Q<VisualElement>("data-list");
			if (dataList != null) {
				foreach (var obj in ctx.Data) {
					var item = _panel.API.AssetAPI.GetAsset<VisualTreeAsset>("log-data-item.uxml").CloneTree();
					item.Q<Label>("data-text").text = ParseData(obj);
					dataList.Add(item);
				}
			}

			// Apply current filter to the new entry
			var label   = foldout?.text ?? string.Empty;
			var visible = string.IsNullOrEmpty(_filter)
				|| label.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;
			entry.EnableInClassList("hidden", !visible);

			_logsContainer.Add(entry);

			if (trim) {
				while (_logsContainer.childCount > EventLogger.MaxLogs)
					_logsContainer.RemoveAt(0);
			}
		}

		private void OnClear() {
			_panel.History.Clear();
			_logsContainer?.Clear();
		}

		private static string ParseData(object obj) {
			if (obj == null) return "null";
			if (obj is string s) return $"\"{s}\"";
			if (obj is Enum e) return e.ToString();
			if (obj is IList<object> list)
				return $"{obj.GetType().Name}[{string.Join(", ", list.Select(ParseData))}]";
			if (obj is IDictionary<string, object> dict)
				return $"{obj.GetType().Name}[{string.Join(", ", dict.Select(kv => $"{kv.Key}={ParseData(kv.Value)}"))}]";
			try { return obj.ToString(); } catch { return obj.GetType().Name; }
		}

		private static string CustomLabel(EventData ctx)
			=> ctx.EventName switch {
				"mod_initialize" when ctx.TryGet(0, out IMod mod)
					&& ctx.TryGet(1, out string entry)
					&& ctx.TryGet(2, out Enum type)
					=> $"{ctx.EventName} [{entry.ToUpper()}]{mod.GetMetadata().GetId()}@{mod.GetMetadata().GetVersion()} => {type}",
				"mod_post_initialize" when ctx.TryGet(0, out IMod mod)
					&& ctx.TryGet(1, out string entry)
					&& ctx.TryGet(2, out Enum type)
					=> $"{ctx.EventName} [{entry.ToUpper()}]{mod.GetMetadata().GetId()}@{mod.GetMetadata().GetVersion()} => {type}",
				"mod_dispose" when ctx.TryGet(0, out IMod mod)
					&& ctx.TryGet(1, out string entry)
					&& ctx.TryGet(2, out Enum type)
					=> $"{ctx.EventName} [{entry.ToUpper()}]{mod.GetMetadata().GetId()}@{mod.GetMetadata().GetVersion()} => {type}",
				"mod_pre_dispose" when ctx.TryGet(0, out IMod mod)
					&& ctx.TryGet(1, out string entry)
					&& ctx.TryGet(2, out Enum type)
					=> $"{ctx.EventName} [{entry.ToUpper()}]{mod.GetMetadata().GetId()}@{mod.GetMetadata().GetVersion()} => {type}",
				"mod_disabled" when ctx.TryGet(0, out IMod mod) && ctx.TryGet(1, out string entry)
					=> $"{ctx.EventName} [{entry.ToUpper()}]{mod.GetMetadata().GetId()}@{mod.GetMetadata().GetVersion()}",
				"mod_enabled" when ctx.TryGet(0, out IMod mod) && ctx.TryGet(1, out string entry)
					=> $"{ctx.EventName} [{entry.ToUpper()}]{mod.GetMetadata().GetId()}@{mod.GetMetadata().GetVersion()}",
				_ => ctx.EventName
			};
	}
}
#endif
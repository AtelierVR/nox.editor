#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Mods;
using Nox.CCK.Mods.Cores;
using Nox.CCK.Mods.Events;
using Nox.CCK.Mods.Initializers;
using Nox.CCK.Mods.Panels;
using Nox.CCK.Utils;
using UnityEngine.UIElements;

namespace Nox.Editor {
	public class EventLogger : IEditorModInitializer {
		internal static IEditorModCoreAPI  CoreAPI;
		private         IEditorPanel       _buildPanel;
		private         EventSubscription _subLogs;

		public void OnInitializeEditor(IEditorModCoreAPI api) {
			CoreAPI = api;
			var panel = new EventLoggerPanel();
			_buildPanel = api.PanelAPI.AddLocalPanel(panel);
			_subLogs    = CoreAPI.EventAPI.Subscribe(null, ctx => panel.OnReceiveLog(ctx));
		}

		public void OnDispose() {
			CoreAPI.PanelAPI.RemoveLocalPanel(_buildPanel);
			CoreAPI.EventAPI.Unsubscribe(_subLogs);
			CoreAPI = null;
		}
	}

	public class EventLoggerPanel : IEditorPanelBuilder {
		public string GetId()
			=> "logger";

		public string GetName()
			=> "Dev/Logger";

		public string GetTitle()
			=> "Event Logger";

		public bool IsHidden()
			=> false;

		public VisualElement[] GetHeaders() {
			var button = new Button { text = "Clear" };
			button.AddToClassList("nox-transparent");
			button.RegisterCallback<ClickEvent>(OnClear);
			return new VisualElement[] { button };
		}

		private void OnClear(ClickEvent evt) {
			_history.Clear();
			var logDiv = _root.Q<VisualElement>("logs");
			logDiv.Clear();
		}

		private readonly VisualElement _root = new();

		public VisualElement Make(Dictionary<string, object> data) {
			_root.ClearBindings();
			_root.Clear();
			_root.Add(EventLogger.CoreAPI.AssetAPI.GetAsset<VisualTreeAsset>("logger.uxml").CloneTree());
			foreach (var context in _history)
				OnReceiveLog(context, false);
			return _root;
		}

		private readonly List<EventData> _history = new();
		private const    uint            MaxLogs  = byte.MaxValue;

		public void OnReceiveLog(EventData context, bool save = true) {
			if (save) {
				_history.Add(context);
				while (_history.Count > MaxLogs)
					_history.RemoveAt(0);
			}

			if (_root.childCount == 0) return;
			var logDiv = _root.Q<VisualElement>("logs");
			var foldout = new Foldout {
				text  = CustomLabel(context),
				value = false,
			};

			// add to foldout the information of the event
			var div = new VisualElement {
				style = { flexDirection = FlexDirection.Column }
			};

			foldout.Add(div);

			div.Add(
				new Label(
					$"Source mod: {context.Source.GetMetadata().GetId()}@{context.Source.GetMetadata().GetVersion()}"
				)
			);
			div.Add(new Label($"Source channel: {context.SourceChannel}"));
			var divData = new VisualElement { style = { flexDirection = FlexDirection.Column } };
			div.Add(divData);
			divData.Add(new Label($"Data ({context.Data.Length})"));
			foreach (var obj in context.Data)
				divData.Add(new Label($" - {ParseData(obj)}"));


			logDiv.Add(foldout);

			while (logDiv.childCount > MaxLogs)
				logDiv.RemoveAt(0);
		}

		public void OnClosed() { }

		private string ParseData(object obj) {
			if (obj == null)
				return "null";

			if (obj is string s)
				return $"\"{s}\"";

			if (obj is Enum e)
				return e.ToString();

			if (obj is IList<object> list) {
				var items = string.Join(", ", list.Select(ParseData));
				return $"{obj.GetType().Name}[{items}]";
			}

			if (obj is IDictionary<string, object> dict) {
				var items = string.Join(", ", dict.Select(kv => $"{kv.Key}={ParseData(kv.Value)}"));
				return $"{obj.GetType().Name}[{items}]";
			}

			try {
				return obj.ToString();
			} catch {
				return obj.GetType().Name;
			}
		}

		private static string CustomLabel(EventData context)
			=> context.EventName switch {
				"mod_initialize" when context.TryGet(0, out IMod mod)
					&& context.TryGet(1, out string entry)
					&& context.TryGet(2, out Enum type)
					=> $"{context.EventName} [{entry.ToUpper()}]{mod.GetMetadata().GetId()}@{mod.GetMetadata().GetVersion()} => {type}",
				"mod_post_initialize" when context.TryGet(0, out IMod mod)
					&& context.TryGet(1, out string entry)
					&& context.TryGet(2, out Enum type)
					=> $"{context.EventName} [{entry.ToUpper()}]{mod.GetMetadata().GetId()}@{mod.GetMetadata().GetVersion()} => {type}",
				"mod_dispose" when context.TryGet(0, out IMod mod)
					&& context.TryGet(1, out string entry)
					&& context.TryGet(2, out Enum type)
					=> $"{context.EventName} [{entry.ToUpper()}]{mod.GetMetadata().GetId()}@{mod.GetMetadata().GetVersion()} => {type}",
				"mod_pre_dispose" when context.TryGet(0, out IMod mod)
					&& context.TryGet(1, out string entry)
					&& context.TryGet(2, out Enum type)
					=> $"{context.EventName} [{entry.ToUpper()}]{mod.GetMetadata().GetId()}@{mod.GetMetadata().GetVersion()} => {type}",
				"mod_disabled" when context.TryGet(0, out IMod mod) && context.TryGet(1, out string entry)
					=> $"{context.EventName} [{entry.ToUpper()}]{mod.GetMetadata().GetId()}@{mod.GetMetadata().GetVersion()}",
				"mod_enabled" when context.TryGet(0, out IMod mod) && context.TryGet(1, out string entry)
					=> $"{context.EventName} [{entry.ToUpper()}]{mod.GetMetadata().GetId()}@{mod.GetMetadata().GetVersion()}",
				_ => context.EventName
			};
	}
}
#endif
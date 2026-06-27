#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Mods;
using Nox.CCK.Mods.Cores;
using Nox.CCK.Mods.Events;
using Nox.CCK.Mods.Initializers;
using Nox.Editor.Panel;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace Nox.Editor {
	public class Performances : IEditorModInitializer, Panel.IPanel {
		internal IEditorModCoreAPI API;
		private EventSubscription[] _events = Array.Empty<EventSubscription>();

		internal static UnityEvent<IMod> ModLoadedEvent   = new();
		internal static UnityEvent<IMod> ModUnloadedEvent = new();

		public void OnInitializeEditor(IEditorModCoreAPI api) {
			API = api;
			_events = new[] {
				api.EventAPI.Subscribe("mod_loaded",   ctx => { if (ctx.TryGet(0, out IMod m)) ModLoadedEvent.Invoke(m); }),
				api.EventAPI.Subscribe("mod_unloaded", ctx => { if (ctx.TryGet(0, out IMod m)) ModUnloadedEvent.Invoke(m); }),
			};
			foreach (var m in api.ModAPI.GetMods())
				ModLoadedEvent.Invoke(m);
		}

		public void OnDisposeEditor() {
			foreach (var s in _events) API?.EventAPI.Unsubscribe(s);
			_events = Array.Empty<EventSubscription>();
			Instance?.OnDestroy();
			API = null;
		}

		public string[] GetPath()  => new[] { "editor", "performances" };
		public string   GetLabel() => "Editor/Performances";

		internal PerformancesInstance Instance;

		public IInstance[] GetInstances()
			=> Instance != null ? new IInstance[] { Instance } : Array.Empty<IInstance>();

		public IInstance Instantiate(IWindow window, Dictionary<string, object> data) {
			if (Instance != null)
				throw new InvalidOperationException($"{nameof(Performances)} only supports a single instance.");
			return Instance = new PerformancesInstance(this, window);
		}

		public void OnUpdateEditor() => Instance?.Update();

	}

	public class PerformancesInstance : IInstance {
		private readonly Performances _panel;
		private readonly IWindow      _window;
		private          VisualElement _content;
		private          SystemPerformanceMonitor _systemMonitor;
		private readonly Dictionary<string, ModPerformanceMonitor> _modMonitors = new();
		private          bool _autoUpdate  = true;
		private          bool _showDetailed = true;
		private          int  _updateRate   = 500;

		private class ModPerfUserData {
			internal readonly string ModId;
			internal ModPerfUserData(string id) => ModId = id;
			public bool Equals(string id) => ModId == id;
		}

		public PerformancesInstance(Performances panel, IWindow window) {
			_panel  = panel;
			_window = window;
		}

		public Nox.Editor.Panel.IPanel  GetPanel()  => _panel;
		public IWindow GetWindow() => _window;
		public string  GetTitle()  => "Performances";

		public void OnDestroy() {
			OnHidden();
			_panel.Instance = null;
		}

		public IToolOption[] GetOptions()
			=> new IToolOption[] {
				new DefaultToolOption("Clear", ClearAllData),
				new DefaultToolOption("Export", ExportPerformanceData),
			};

		public VisualElement GetContent() {
			if (_content != null) return _content;

			_content = _panel.API.AssetAPI.GetAsset<VisualTreeAsset>("perfs.uxml").CloneTree();
			_content.AddToClassList("flex-fill");

			var autoUpdate = _content.Q<Toggle>("auto-update");
			autoUpdate.SetValueWithoutNotify(_autoUpdate);
			autoUpdate.RegisterCallback<ChangeEvent<bool>>(e => _autoUpdate = e.newValue);

			var showDetailed = _content.Q<Toggle>("show-detailed");
			showDetailed.SetValueWithoutNotify(_showDetailed);
			showDetailed.RegisterCallback<ChangeEvent<bool>>(e => { _showDetailed = e.newValue; UpdateDetailedVisibility(); });

			var updateRate = _content.Q<SliderInt>("update-rate");
			updateRate.SetValueWithoutNotify(_updateRate);
			updateRate.RegisterCallback<ChangeEvent<int>>(e => _updateRate = e.newValue);

			_systemMonitor = new SystemPerformanceMonitor(_content);

			foreach (var mod in _panel.API.ModAPI.GetMods()) OnModAdded(mod);

			Performances.ModLoadedEvent.AddListener(OnModAdded);
			Performances.ModUnloadedEvent.AddListener(OnModRemoved);

			return _content;
		}

		public void Update() {
			if (!_autoUpdate) return;
			_systemMonitor?.Update();
			foreach (var monitor in _modMonitors.Values) monitor?.Update();
		}

		private VisualElement GetModElement(string modId)
			=> _content?.Q<VisualElement>("mods-container")
				?.Children()
				.FirstOrDefault(c => c.userData is ModPerfUserData d && d.Equals(modId));

		private void OnModAdded(IMod mod) {
			if (mod == null || _content == null) return;
			var modId     = mod.GetMetadata().GetId();
			var container = _content.Q<VisualElement>("mods-container");
			if (container == null) return;

			var child = GetModElement(modId);
			if (child != null) { _modMonitors.TryGetValue(modId, out var ex); ex?.UpdateMod(mod); return; }

			child          = _panel.API.AssetAPI.GetAsset<VisualTreeAsset>("mod-perf-item.uxml").CloneTree();
			child.userData = new ModPerfUserData(modId);
			var monitor    = new ModPerformanceMonitor(mod, child, _panel.API);
			_modMonitors[modId] = monitor;
			container.Add(child);
		}

		private void OnModRemoved(IMod mod) {
			if (mod == null) return;
			var modId = mod.GetMetadata().GetId();
			var child = GetModElement(modId);
			if (child != null) {
				_content?.Q<VisualElement>("mods-container")?.Remove(child);
				child.RemoveFromHierarchy();
			}
			if (_modMonitors.TryGetValue(modId, out var monitor)) {
				monitor.Dispose();
				_modMonitors.Remove(modId);
			}
		}

		private void ClearAllData() {
			_systemMonitor?.ClearData();
			foreach (var m in _modMonitors.Values) m?.ClearData();
		}

		private void UpdateDetailedVisibility() {
			foreach (var m in _modMonitors.Values) m?.SetDetailedVisibility(_showDetailed);
		}

		private void ExportPerformanceData() {
			var path = EditorUtility.SaveFilePanel("Export Performance Data", "", "performance_data", "json");
			if (string.IsNullOrEmpty(path)) return;
			EditorUtility.DisplayDialog("Export", "Performance data exported successfully!", "Ok");
		}

		private void OnHidden() {
			Performances.ModLoadedEvent.RemoveListener(OnModAdded);
			Performances.ModUnloadedEvent.RemoveListener(OnModRemoved);
			foreach (var m in _modMonitors.Values) m?.Dispose();
			_modMonitors.Clear();
			_systemMonitor?.Dispose();
			_systemMonitor = null;
		}
	}

	public class SystemPerformanceMonitor {
		private readonly VisualElement _root;
		private readonly VisualElement _graphContainer;
		private readonly Foldout _graphFoldout;
		private readonly Label _fpsLabel;
		private readonly Label _memoryLabel;
		private readonly Label _drawCallsLabel;
		
		private Image _graphImage;
		private Texture2D _graphTexture;
		
		private DateTime _lastUpdate = DateTime.MinValue;
		private readonly List<float> _fpsCurrent = new();
		private readonly List<(float, float, float)> _fpsHistory = new();
		private readonly List<float> _memoryHistory = new();
		private readonly List<float> _drawCallsHistory = new();
		
		private const int MaxHistoryPoints = 256;
		private const float UpdateInterval = 0.1f;

		public SystemPerformanceMonitor(VisualElement root) {
			_root = root;
			_graphContainer = root.Q<VisualElement>("system-graph");
			_graphFoldout = root.Q<Foldout>("system-graph-foldout");
			_fpsLabel = root.Q<Label>("fps-label");
			_memoryLabel = root.Q<Label>("memory-label");
			_drawCallsLabel = root.Q<Label>("draw-calls-label");
			
			InitializeGraph();
		}

		private void InitializeGraph() {
			if (_graphContainer == null) return;

			var w = Mathf.Max(512, (int)_graphContainer.layout.width);
			var h = Mathf.Max(200, (int)_graphContainer.layout.height);

			_graphTexture = new Texture2D(w, h, TextureFormat.RGBA32, false) {
				filterMode = FilterMode.Point
			};
			_graphImage       = _graphContainer.Q<Image>("graph-image");
			if (_graphImage != null) _graphImage.image = _graphTexture;
			PerformanceMonitor.Fill(_graphTexture);
		}

		public void Update() {
			_fpsCurrent.Add(Time.deltaTime);
			
			if ((DateTime.UtcNow - _lastUpdate).TotalSeconds >= UpdateInterval) {
				_lastUpdate = DateTime.UtcNow;
				
				// Calculate FPS
				var avg = _fpsCurrent.Count > 0 ? 1f / _fpsCurrent.Average() : 0f;
				var min = _fpsCurrent.Count > 0 ? 1f / _fpsCurrent.Max() : 0f;
				var max = _fpsCurrent.Count > 0 ? 1f / _fpsCurrent.Min() : 0f;
				_fpsCurrent.Clear();
				
				// Update history
				_fpsHistory.Add((min, avg, max));
				
				var memoryUsageMb = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
				_memoryHistory.Add(memoryUsageMb);
				
				var drawCalls = UnityStats.drawCalls;
				_drawCallsHistory.Add(drawCalls);
				
				// Limit history
				if (_fpsHistory.Count > MaxHistoryPoints) _fpsHistory.RemoveAt(0);
				if (_memoryHistory.Count > MaxHistoryPoints) _memoryHistory.RemoveAt(0);
				if (_drawCallsHistory.Count > MaxHistoryPoints) _drawCallsHistory.RemoveAt(0);
				
				UpdateLabels(avg, min, max, memoryUsageMb, drawCalls);
				
				if (_graphFoldout?.value == true) {
					UpdateGraph();
				}
			}
		}

		private void UpdateLabels(float avgFps, float minFps, float maxFps, float memory, float drawCalls) {
			if (_fpsLabel != null) {
				_fpsLabel.text = $"FPS: {avgFps:F1} (Min: {minFps:F1}, Max: {maxFps:F1})";
			}
			
			if (_memoryLabel != null) {
				_memoryLabel.text = $"Memory: {memory:F1} MB";
			}
			
			if (_drawCallsLabel != null) {
				_drawCallsLabel.text = $"Draw Calls: {drawCalls:F0}";
			}
		}

		private void UpdateGraph() {
			if (_graphTexture == null || _graphContainer == null) return;
			
			PerformanceMonitor.Fill(_graphTexture);
			PerformanceMonitor.DrawGrid(_graphTexture);
			
			var maxFps = _fpsHistory.Count > 0 ? Mathf.Max(_fpsHistory.Max(x => x.Item3), 60) : 60;
			var maxMemory = _memoryHistory.Count > 0 ? Mathf.Max(_memoryHistory.Max(), 100) : 100;
			var maxDrawCalls = _drawCallsHistory.Count > 0 ? Mathf.Max(_drawCallsHistory.Max(), 100) : 100;
			
			// Draw FPS lines
			PerformanceMonitor.DrawDataLine(_graphTexture, _fpsHistory.Select(x => x.Item2).ToList(), maxFps, new Color(0, 1, 0, 0.7f), false);
			PerformanceMonitor.DrawDataLine(_graphTexture, _fpsHistory.Select(x => x.Item1).ToList(), maxFps, new Color(0, 1, 1, 0.7f), false);
			PerformanceMonitor.DrawDataLine(_graphTexture, _fpsHistory.Select(x => x.Item3).ToList(), maxFps, new Color(1, 1, 0, 0.7f), false);
			
			// Draw memory and draw calls
			PerformanceMonitor.DrawDataLine(_graphTexture, _memoryHistory, maxMemory, new Color(0, 0.5f, 1, 0.7f), false);
			PerformanceMonitor.DrawDataLine(_graphTexture, _drawCallsHistory, maxDrawCalls, new Color(1, 0.5f, 0, 0.7f), false);
			
			_graphTexture.Apply();
			_graphImage?.MarkDirtyRepaint();
		}

		public void ClearData() {
			_fpsHistory.Clear();
			_memoryHistory.Clear();
			_drawCallsHistory.Clear();
			_fpsCurrent.Clear();
			
			if (_graphTexture != null) {
				PerformanceMonitor.Fill(_graphTexture);
				_graphTexture.Apply();
			}
		}

		public void Dispose() {
			if (_graphTexture) {
				UnityEngine.Object.DestroyImmediate(_graphTexture);
				_graphTexture = null;
			}
			
			_graphImage?.RemoveFromHierarchy();
		}
	}

	public class ModPerformanceMonitor {
		private IMod _mod;
		private readonly VisualElement _container;
		private readonly Foldout _foldout;
		private readonly VisualElement _content;
		private readonly VisualElement _graphContainer;
		private readonly VisualElement _profilersList;
		private readonly Label _totalTimeLabel;
		private readonly Label _avgTimeLabel;
		private readonly Label _peakTimeLabel;
		private readonly IEditorModCoreAPI _api;

		private Image     _graphImage;
		private Texture2D _graphTexture;

		private DateTime _lastUpdate = DateTime.MinValue;
		private readonly Dictionary<string, ProfilerLineMonitor> _profilerMonitors = new();
		private readonly Dictionary<string, List<(float, float, float)>> _profilerHistory = new();

		private const float UpdateInterval = 0.2f;

		public ModPerformanceMonitor(IMod mod, VisualElement container, IEditorModCoreAPI api) {
			_mod            = mod;
			_container      = container;
			_api            = api;
			_foldout        = container.Q<Foldout>("mod-foldout");
			_content        = container.Q<VisualElement>("mod-content");
			_graphContainer = container.Q<VisualElement>("performance-graph");
			_profilersList  = container.Q<VisualElement>("profilers-list");
			_totalTimeLabel = container.Q<Label>("total-time");
			_avgTimeLabel   = container.Q<Label>("avg-time");
			_peakTimeLabel  = container.Q<Label>("peak-time");

			InitializeGraph();
			UpdateModInfo();
		}

		private void InitializeGraph() {
			if (_graphContainer == null) return;

			var w = Mathf.Max(400, (int)_graphContainer.layout.width);
			var h = Mathf.Max(120, (int)_graphContainer.layout.height);

			_graphTexture = new Texture2D(w, h, TextureFormat.RGBA32, false) {
				filterMode = FilterMode.Point
			};
			_graphImage       = _graphContainer.Q<Image>("graph-image");
			if (_graphImage != null) _graphImage.image = _graphTexture;
			PerformanceMonitor.Fill(_graphTexture);
		}

		public void UpdateMod(IMod mod) {
			// Update mod reference if needed
			UpdateModInfo();
		}

		private void UpdateModInfo() {
			if (_foldout != null && _mod != null) {
				var meta = _mod.GetMetadata();
				_foldout.text = $"{meta.GetName()} ({meta.GetId()})";
			}
		}

		public void Update() {
			if (_mod == null || (DateTime.UtcNow - _lastUpdate).TotalSeconds < UpdateInterval) return;
			
			_lastUpdate = DateTime.UtcNow;
			
			var profilers = _mod.GetProfiler().ToArray();
			Array.Sort(profilers, (p1, p2) => string.Compare(p1.GetName(), p2.GetName(), StringComparison.Ordinal));
			
			UpdateProfilers(profilers);
			UpdateSummary(profilers);
			
			if (_foldout?.value == true) {
				UpdateGraph();
			}
		}

		private void UpdateProfilers(Profile[] profilers) {
			var currentNames = profilers.Select(p => p.GetName()).ToHashSet();
			var toRemove     = _profilerMonitors.Keys.Where(k => !currentNames.Contains(k)).ToList();
			foreach (var name in toRemove) {
				if (_profilerMonitors.TryGetValue(name, out var mon)) { mon.Dispose(); _profilerMonitors.Remove(name); }
				_profilerHistory.Remove(name);
			}

			for (int i = 0; i < profilers.Length; i++) {
				var profiler = profilers[i];
				var name     = profiler.GetName();
				var duration = (float)profiler.Duration.TotalMilliseconds;

				if (!_profilerMonitors.TryGetValue(name, out var monitor)) {
					var element = _api.AssetAPI.GetAsset<VisualTreeAsset>("profiler-line.uxml").CloneTree();
					_profilersList.Add(element);
					monitor = new ProfilerLineMonitor(name, element, i);
					_profilerMonitors[name] = monitor;
					_profilerHistory[name]  = new List<(float, float, float)>();
				}

				monitor.UpdateCurrent(duration);
				monitor.UpdateColor(i);

				if (!_profilerHistory.TryGetValue(name, out var history)) {
					history = new List<(float, float, float)>();
					_profilerHistory[name] = history;
				}
				history.Add((duration, duration, duration));
				if (history.Count > 100) history.RemoveAt(0);
				if (history.Count > 0) {
					monitor.UpdateMin(history.Min(x => x.Item1));
					monitor.UpdateAvg(history.Average(x => x.Item2));
					monitor.UpdateMax(history.Max(x => x.Item3));
				}
			}
		}

		private void UpdateSummary(Profile[] profilers) {
			if (profilers.Length == 0) return;
			
			var totalTime = profilers.Sum(p => p.Duration.TotalMilliseconds);
			var avgTime = totalTime / profilers.Length;
			var peakTime = profilers.Max(p => p.Duration.TotalMilliseconds);
			
			if (_totalTimeLabel != null) _totalTimeLabel.text = $"Total: {totalTime:F2}ms";
			if (_avgTimeLabel != null) _avgTimeLabel.text = $"Avg: {avgTime:F2}ms";
			if (_peakTimeLabel != null) _peakTimeLabel.text = $"Peak: {peakTime:F2}ms";
		}

		private void UpdateGraph() {
			if (_graphTexture == null || _profilerHistory.Count == 0) return;
			
			PerformanceMonitor.Fill(_graphTexture);
			PerformanceMonitor.DrawGrid(_graphTexture);
			
			var maxValue = _profilerHistory.Values
				.SelectMany(x => x)
				.DefaultIfEmpty((0f, 0f, 0f))
				.Max(x => x.Item3);
			
			var histories = _profilerHistory.ToArray();
			Array.Sort(histories, (x, y) => string.Compare(x.Key, y.Key, StringComparison.Ordinal));
			
			for (int i = 0; i < histories.Length; i++) {
				var history = histories[i].Value;
				var color = PerformanceMonitor.HSLToRGB(i / (float)histories.Length * 360f, 0.7f, 0.5f);
				PerformanceMonitor.DrawDataLine(_graphTexture, history.Select(x => x.Item2).ToList(), maxValue, color, false);
			}
			
			_graphTexture.Apply();
			_graphImage?.MarkDirtyRepaint();
		}

		public void SetDetailedVisibility(bool visible) {
			_content?.EnableInClassList("hidden", !visible);
		}

		public void ClearData() {
			foreach (var history in _profilerHistory.Values) {
				history.Clear();
			}
			
			if (_graphTexture != null) {
				PerformanceMonitor.Fill(_graphTexture);
				_graphTexture.Apply();
			}
		}

		public void Dispose() {
			foreach (var monitor in _profilerMonitors.Values) {
				monitor.Dispose();
			}
			_profilerMonitors.Clear();
			_profilerHistory.Clear();
			
			if (_graphTexture) {
				UnityEngine.Object.DestroyImmediate(_graphTexture);
				_graphTexture = null;
			}
			
			_graphImage?.RemoveFromHierarchy();
		}
	}

	public class ProfilerLineMonitor {
		private readonly VisualElement _element;
		private readonly VisualElement _colorIndicator;
		private readonly Label _nameLabel;
		private readonly Label _currentLabel;
		private readonly Label _minLabel;
		private readonly Label _avgLabel;
		private readonly Label _maxLabel;
		private          int   _lastColorIndex = -1;

		public ProfilerLineMonitor(string name, VisualElement element, int colorIndex) {
			_element        = element;
			_colorIndicator = element.Q<VisualElement>("color-indicator");
			_nameLabel      = element.Q<Label>("profiler-name");
			_currentLabel   = element.Q<Label>("current-ms");
			_minLabel       = element.Q<Label>("min-ms");
			_avgLabel       = element.Q<Label>("avg-ms");
			_maxLabel       = element.Q<Label>("max-ms");

			if (_nameLabel != null) _nameLabel.text = name;
			UpdateColor(colorIndex);
		}

		public void UpdateColor(int index) {
			if (_colorIndicator == null) return;
			var idx = index % 12;
			if (idx == _lastColorIndex) return;
			if (_lastColorIndex >= 0) _colorIndicator.RemoveFromClassList($"profiler-color-{_lastColorIndex}");
			_colorIndicator.AddToClassList($"profiler-color-{idx}");
			_lastColorIndex = idx;
		}

		public void UpdateCurrent(float value) {
			if (_currentLabel != null) _currentLabel.text = $"{value:F2}ms";
		}

		public void UpdateMin(float value) {
			if (_minLabel != null) _minLabel.text = $"{value:F2}ms";
		}

		public void UpdateAvg(float value) {
			if (_avgLabel != null) _avgLabel.text = $"{value:F2}ms";
		}

		public void UpdateMax(float value) {
			if (_maxLabel != null) _maxLabel.text = $"{value:F2}ms";
		}

		public void Dispose() {
			_element?.RemoveFromHierarchy();
		}
	}

	// ...existing code for PerformanceMonitor static methods...
	public class PerformanceMonitor {
		public static void Fill(Texture2D texture) {
			var clearColor = new Color(0.15f, 0.15f, 0.15f, 1.0f);
			var pixels     = new Color[texture.width * texture.height];
			for (int i = 0; i < pixels.Length; i++) {
				pixels[i] = clearColor;
			}

			texture.SetPixels(pixels);
			texture.Apply();
		}

		public static void DrawGrid(Texture2D texture) {
			var gridColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
			var width     = texture.width;
			var height    = texture.height;

			// Draw horizontal grid lines
			for (int y = 0; y < height; y += height / 4)
			for (int x = 0; x < width; x++)
				texture.SetPixel(x, y, gridColor);

			// Draw vertical grid lines
			for (int x = 0; x < width; x += width / 10)
			for (int y = 0; y < height; y++)
				texture.SetPixel(x, y, gridColor);
		}

		public static void DrawDataLine(Texture2D texture, List<float> data, float maxValue, Color lineColor, bool reverseY = false) {
			if (data.Count < 2) return;
			var width  = texture.width;
			var height = texture.height;

			var lastIndex  = data.Count - 1;

			for (int i = 0; i < data.Count - 1; i++) {
				var x1 = (int)((i       / (float)lastIndex) * (width - 1));
				var x2 = (int)(((i + 1) / (float)lastIndex) * (width - 1));

				// Calculate y coordinates based on reverseY flag
				int y1, y2;
				if (reverseY) {
					// Direct mapping - higher values are at the bottom
					y1 = (int)((data[i]     / maxValue) * (height - 1));
					y2 = (int)((data[i + 1] / maxValue) * (height - 1));
				} else {
					// Inverted mapping - higher values are at the top (original behavior)
					y1 = (int)((1 - (data[i]     / maxValue)) * (height - 1));
					y2 = (int)((1 - (data[i + 1] / maxValue)) * (height - 1));
				}

				DrawLine(texture, x1, y1, x2, y2, lineColor);
			}
		}

		public static Color HSLToRGB(float h, float s, float l) {
			float c = (1f - Mathf.Abs(2f * l - 1f)) * s;
			float x = c                             * (1f - Mathf.Abs((h / 60f) % 2 - 1f));
			float m = l - c / 2f;

			float r1 = 0, g1 = 0, b1 = 0;

			if (h < 60) {
				r1 = c;
				g1 = x;
				b1 = 0;
			} else if (h < 120) {
				r1 = x;
				g1 = c;
				b1 = 0;
			} else if (h < 180) {
				r1 = 0;
				g1 = c;
				b1 = x;
			} else if (h < 240) {
				r1 = 0;
				g1 = x;
				b1 = c;
			} else if (h < 300) {
				r1 = x;
				g1 = 0;
				b1 = c;
			} else {
				r1 = c;
				g1 = 0;
				b1 = x;
			}

			return new Color(r1 + m, g1 + m, b1 + m);
		}

		public static void DrawLine(Texture2D texture, int x0, int y0, int x1, int y1, Color color) {
			int dx  = Mathf.Abs(x1 - x0);
			int dy  = Mathf.Abs(y1 - y0);
			int sx  = x0 < x1 ? 1 : -1;
			int sy  = y0 < y1 ? 1 : -1;
			int err = dx - dy;

			while (true) {
				// Set the pixel at the current coordinates
				if (x0 >= 0 && x0 < texture.width && y0 >= 0 && y0 < texture.height)
					texture.SetPixel(x0, y0, color);

				// Check if we've reached the end point
				if (x0 == x1 && y0 == y1) break;

				int e2 = 2 * err;
				if (e2 > -dy) {
					err -= dy;
					x0  += sx;
				}

				if (e2 < dx) {
					err += dx;
					y0  += sy;
				}
			}
		}
	}
}

#endif

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nox.Editor {
	public class LinkerFile : ScriptableObject {
		public string[] assemblies = Array.Empty<string>();
	}

	[ScriptedImporter(1, null, new[] { "xml" })]
	public class LinkerFileImporter : ScriptedImporter {
		public override void OnImportAsset(AssetImportContext ctx) {
			if (!Path.GetFileName(ctx.assetPath).Equals("link.xml", StringComparison.OrdinalIgnoreCase)) {
				var text = new TextAsset(File.ReadAllText(ctx.assetPath));
				ctx.AddObjectToAsset("main", text);
				ctx.SetMainObject(text);
				return;
			}

			var asset = ScriptableObject.CreateInstance<LinkerFile>();
			try {
				var doc   = new XmlDocument();
				doc.Load(ctx.assetPath);
				var list  = new List<string>();
				var nodes = doc.SelectNodes("//assembly");
				if (nodes != null)
					foreach (XmlNode node in nodes) {
						var fullname = node.Attributes?["fullname"]?.Value;
						if (!string.IsNullOrEmpty(fullname))
							list.Add(fullname);
					}
				asset.assemblies = list.ToArray();
			} catch (Exception e) {
				Debug.LogError($"Failed to parse link.xml at {ctx.assetPath}: {e}");
			}

			ctx.AddObjectToAsset("main", asset);
			ctx.SetMainObject(asset);
		}
	}

	[InitializeOnLoad]
	public static class LinkerFileBootstrap {
		static LinkerFileBootstrap() {
			EditorApplication.delayCall += EnsureLinkerImporters;
		}

		private static void EnsureLinkerImporters() {
			var guids = AssetDatabase.FindAssets("link");
			foreach (var guid in guids) {
				var path = AssetDatabase.GUIDToAssetPath(guid);
				if (!Path.GetFileName(path).Equals("link.xml", StringComparison.OrdinalIgnoreCase)) continue;
				if (AssetImporter.GetAtPath(path) is LinkerFileImporter) continue;
				AssetDatabase.SetImporterOverride<LinkerFileImporter>(path);
			}
		}
	}

	[CustomEditor(typeof(LinkerFile))]
	public class LinkerFileEditor : UnityEditor.Editor {
		public override VisualElement CreateInspectorGUI() {
			var root       = new VisualElement();
			var linkerFile = (LinkerFile)target;
			var lookup     = BuildLookup();

			var info = new HelpBox($"Managed automatically by {nameof(ModLinker)}.", HelpBoxMessageType.Info);
			root.Add(info);

			var listView = new ListView {
				showAddRemoveFooter  = false,
				showBorder           = true,
				showFoldoutHeader    = true,
				headerTitle          = $"Assemblies",
				virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
				makeItem = () => {
					var row = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 2, paddingRight = 2 } };
					var lbl = new Label { name = "key", style = { flexBasis = 200, flexShrink = 1, flexGrow = 0, overflow = Overflow.Hidden, unityTextAlign = TextAnchor.MiddleLeft, marginRight = 4 } };
					var fld = new ObjectField { name = "val", objectType = typeof(UnityEngine.Object), allowSceneObjects = false, style = { flexGrow = 1, flexShrink = 1 } };
					fld.labelElement.style.display = DisplayStyle.None;
					row.Add(lbl);
					row.Add(fld);
					return row;
				},
				bindItem = (e, i) => {
					var name = linkerFile.assemblies[i];
					e.Q<Label>("key").text = name;
					var of = e.Q<ObjectField>("val");
					lookup.TryGetValue(name, out var obj);
					of.SetValueWithoutNotify(obj);
				},
				itemsSource = linkerFile.assemblies,
			};

			root.Add(listView);
			return root;
		}

		private static Dictionary<string, UnityEngine.Object> BuildLookup() {
			var lookup = new Dictionary<string, UnityEngine.Object>();

			// asmdef assets
			foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(AssemblyDefinitionAsset)}")) {
				var path  = AssetDatabase.GUIDToAssetPath(guid);
				var json  = File.ReadAllText(path);
				var match = Regex.Match(json, @"""name""\s*:\s*""([^""]+)""");
				if (!match.Success) continue;
				var asset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(path);
				if (asset != null)
					lookup[match.Groups[1].Value] = asset;
			}

			// managed DLL assets
			foreach (var path in AssetDatabase.GetAllAssetPaths()) {
				if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
				if (AssetImporter.GetAtPath(path) is not PluginImporter pi || pi.isNativePlugin) continue;
				try {
					var name  = AssemblyName.GetAssemblyName(Path.GetFullPath(path)).Name;
					var asset = AssetDatabase.LoadMainAssetAtPath(path) ?? (UnityEngine.Object)pi;
					if (string.IsNullOrEmpty(asset.name))
						asset.name = Path.GetFileNameWithoutExtension(path);
					lookup.TryAdd(name, asset);
				} catch { /* native or unreadable DLL — skip */ }
			}

			return lookup;
		}
	}
}

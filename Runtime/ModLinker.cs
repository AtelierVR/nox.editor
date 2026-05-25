using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Nox.ModLoader;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Editor {
	public class ModLinker : UnityEditor.AssetModificationProcessor {
		private static string[] OnWillSaveAssets(string[] paths) {
			if (paths.Any(path => path.EndsWith(".asmdef")))
				ModLinkerHelper.EnsureLinkerClassExists();
			return paths;
		}
	}

	public static class ModLinkerHelper {
		private const string LinkXmlName = "link.xml";

		// Assemblies that must always be preserved regardless of mod discover.
		private static readonly string[] AlwaysPreservedAssemblies = {
			"System",
			"System.Configuration",
		};

		[MenuItem("Nox/Tools/Update Linker Files")]
		public static void EnsureLinkerClassExists() {
			var li = new List<string>();

			foreach (var (path, fullNames) in GetAssemblyByMod()) {
				UpdateLinkXml(path, fullNames.OrderBy(a => a).ToArray());
				li.AddRange(fullNames);
			}

			// Resolve transitive asmdef dependencies so sub-dependencies
			// (e.g. Nox.CCK referenced by a mod's asmdef) are also preserved.
			var asmIndex    = BuildAsmDefIndex();
			var allWithDeps = CollectTransitiveDeps(li, asmIndex);

			// Also include assemblies declared in any package-level link.xml files
			// so that Assets/link.xml is a complete mirror of everything preserved in the project.
			var fromPackageLinkXmls = ReadAllPackageLinkXmlAssemblies();

			UpdateLinkXml(
				Path.Combine(Application.dataPath, LinkXmlName),
				allWithDeps.Concat(fromPackageLinkXmls).Concat(AlwaysPreservedAssemblies).Distinct().OrderBy(a => a).ToArray()
			);
		}

		/// <summary>Reads assembly fullnames from every link.xml found under Packages/.</summary>
		private static IEnumerable<string> ReadAllPackageLinkXmlAssemblies() {
			foreach (var path in Directory.GetFiles("Packages", LinkXmlName, SearchOption.AllDirectories)) {
				XmlDocument doc;
				try {
					doc = new XmlDocument();
					doc.Load(path);
				} catch { continue; }
				var nodes = doc.SelectNodes("//assembly");
				if (nodes == null) continue;
				foreach (XmlNode node in nodes) {
					var fullname = node.Attributes?["fullname"]?.Value;
					if (!string.IsNullOrEmpty(fullname))
						yield return fullname;
				}
			}
		}

		// --- asmdef name helpers ---

		private static string ReadAsmDefName(string path) {
			try {
				var json = File.ReadAllText(path);
				var match = Regex.Match(json, @"""name""\s*:\s*""([^""]+)""");
				return match.Success ? match.Groups[1].Value : null;
			} catch {
				return null;
			}
		}

		// --- transitive dependency resolution ---

		/// <summary>Builds a map of assembly name → asmdef path for every asmdef in the project.</summary>
		private static Dictionary<string, string> BuildAsmDefIndex() {
			var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(AssemblyDefinitionAsset)}")) {
				var path = AssetDatabase.GUIDToAssetPath(guid);
				var name = ReadAsmDefName(path);
				if (name != null)
					index[name] = path;
			}
			return index;
		}

		/// <summary>Reads the "references" array from an asmdef and resolves each entry to an assembly name.</summary>
		private static IEnumerable<string> ReadAsmDefRefs(string asmDefPath) {
			string json;
			try { json = File.ReadAllText(asmDefPath); } catch { yield break; }

			var arrayMatch = Regex.Match(json, @"""references""\s*:\s*\[([^\]]*)\]", RegexOptions.Singleline);
			if (!arrayMatch.Success) yield break;

			foreach (Match r in Regex.Matches(arrayMatch.Groups[1].Value, @"""([^""]+)""")) {
				var val = r.Groups[1].Value;
				if (val.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase)) {
					// GUID-based reference — resolve via AssetDatabase
					var refPath = AssetDatabase.GUIDToAssetPath(val.Substring(5));
					if (string.IsNullOrEmpty(refPath)) continue;
					var name = ReadAsmDefName(refPath);
					if (name != null) yield return name;
				} else {
					yield return val;
				}
			}
		}

		/// <summary>BFS over asmdef references to collect all transitive assembly names.</summary>
		private static HashSet<string> CollectTransitiveDeps(IEnumerable<string> roots, Dictionary<string, string> index) {
			var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var queue   = new Queue<string>(roots);
			while (queue.Count > 0) {
				var name = queue.Dequeue();
				if (!visited.Add(name)) continue;
				if (index.TryGetValue(name, out var path))
					foreach (var dep in ReadAsmDefRefs(path))
						if (!visited.Contains(dep))
							queue.Enqueue(dep);
			}
			return visited;
		}

		private static (string, string[])[] GetAssemblyByMod() {
			var assetMods   = Directory.GetFiles("Assets", "nox.mod.*", SearchOption.AllDirectories);
			var packageMods = Directory.GetFiles("Packages", "nox.mod.*", SearchOption.AllDirectories);
			return (from mod in assetMods.Concat(packageMods).Distinct().ToArray()
				select Path.GetDirectoryName(mod) into dir
				let def = Directory.GetFiles(dir, "*.asmdef", SearchOption.AllDirectories)
				let asmNames = def.Select(ReadAsmDefName).Where(n => n != null)
				let pluginNames = GetManagedPluginAssemblyNames(dir)
				select (Path.Combine(dir, LinkXmlName), asmNames.Concat(pluginNames).Distinct().ToArray())).ToArray();
		}

		private static IEnumerable<string> GetManagedPluginAssemblyNames(string modDir) {
			var pluginsDir = Path.Combine(modDir, "Plugins");
			if (!Directory.Exists(pluginsDir))
				yield break;

			foreach (var dll in Directory.GetFiles(pluginsDir, "*.dll", SearchOption.AllDirectories)) {
				string name = null;
				try {
					name = AssemblyName.GetAssemblyName(dll).Name;
				} catch {
					// native DLL or invalid managed assembly — skip
				}
				if (name != null)
					yield return name;
			}
		}


		private static void UpdateLinkXml(string path, string[] assemblies) {
			try {
				var doc           = new XmlDocument();
				var linkerElement = doc.CreateElement("linker");
				doc.AppendChild(doc.CreateXmlDeclaration("1.0", "utf-8", null));
				doc.AppendChild(linkerElement);

				foreach (var assembly in assemblies) {
					var assemblyElement = doc.CreateElement("assembly");
					assemblyElement.SetAttribute("fullname", assembly);
					assemblyElement.SetAttribute("preserve", "all");
					linkerElement.AppendChild(assemblyElement);
				}

				// Sérialiser en mémoire
				var xmlSettings = new XmlWriterSettings {
					Indent       = true,
					IndentChars  = "\t",
					NewLineChars = "\n",
					Encoding     = new UTF8Encoding(false)
				};
				string newContent;
				using (var ms = new MemoryStream())
				using (var xw = XmlWriter.Create(ms, xmlSettings)) {
					doc.Save(xw);
					xw.Flush();
					newContent = Encoding.UTF8.GetString(ms.ToArray());
				}

				// N'écrire sur le disque que si le contenu a vraiment changé
				if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == newContent)
					return;

				File.WriteAllText(path, newContent, Encoding.UTF8);
			} catch (Exception e) {
				Logger.LogError($"Failed to update link.xml at {path}: {e}");
				Logger.LogError(e);
			}
		}
	}
}
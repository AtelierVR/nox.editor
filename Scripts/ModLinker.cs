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
		

		// Assemblies that must always be preserved regardless of mod discovery
		// (system-level packages with no nox.mod.json, or precompiled DLLs)
		private static readonly string[] AlwaysPreservedAssemblies = {
			"Nox.ModLoader",
			"Mono.Cecil",
			"Mono.Cecil.Mdb",
			"Mono.Cecil.Pdb",
			"Mono.Cecil.Rocks",
		};

		[MenuItem("Nox/Tools/Update Linker Files")]
		public static void EnsureLinkerClassExists() {
			var li = new List<string>();

			foreach (var (path, fullNames) in GetAssemblyByMod()) {
				UpdateLinkXml(path, fullNames);
				li.AddRange(fullNames);
			}

			UpdateLinkXml(
				Path.Combine(Application.dataPath, LinkXmlName),
				li.Concat(AlwaysPreservedAssemblies).Distinct().ToArray()
			);
		}

		private static string ReadAsmDefName(string path) {
			try {
				var json = File.ReadAllText(path);
				var match = Regex.Match(json, @"""name""\s*:\s*""([^""]+)""");
				return match.Success ? match.Groups[1].Value : null;
			} catch {
				return null;
			}
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

				// Ajouter les nouvelles entrées
				foreach (var assembly in assemblies.OrderBy(a => a)) {
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
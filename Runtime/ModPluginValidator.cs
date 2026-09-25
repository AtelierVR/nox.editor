#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nox.CCK.Attributes;
using Nox.CCK.Utils;
using UnityEditor;
using Logger = Nox.CCK.Utils.Logger;
using UObject = UnityEngine.Object;

namespace Nox.Editor {
	public enum PluginIssueSeverity {
		Warning,
		Error,
	}

	public readonly struct PluginIssue {
		public readonly string AssetPath;
		public readonly PluginIssueSeverity Severity;
		public readonly string Message;

		public PluginIssue(string assetPath, PluginIssueSeverity severity, string message) {
			AssetPath = assetPath;
			Severity  = severity;
			Message   = message;
		}
	}

	/// <summary>
	/// Checks that every mod (any folder holding a <c>nox.mod.json</c>) stores its native and managed
	/// plugins at the expected place and that every imported plugin carries the matching
	/// <see cref="PluginImporter"/> settings.
	///
	/// <para><b>Expected layout</b> — relative to <c>&lt;mod&gt;/Plugins</c>, from most to least
	/// specific. This mirrors the runtime search order of
	/// <c>Nox.ModLoader.Core.Libs.LibManager</c> / <c>Library.GetSubFolders</c>:</para>
	/// <list type="number">
	/// <item><description><c>&lt;platform&gt;/&lt;arch&gt;/file</c> — e.g. <c>Plugins/windows/x64/avcodec-61.dll</c></description></item>
	/// <item><description><c>&lt;platform&gt;/file</c> — binaries shared by every architecture of that platform</description></item>
	/// <item><description><c>file</c> — platform-agnostic <i>managed</i> assemblies only</description></item>
	/// </list>
	/// <para>Platform folder names come from <c>Platform.GetPlatformName()</c>
	/// (<c>windows</c>, <c>linux</c>, <c>macos</c>, <c>android</c>, <c>ios</c>, <c>visionos</c>) and
	/// architecture folder names from <c>Architecture.GetArchitectureName()</c>
	/// (<c>x86</c>, <c>x64</c>, <c>arm</c>, <c>arm64</c>). The legacy flat folders
	/// (<c>win64</c>, <c>win32</c>, <c>osx</c>, <c>x86_64</c>, …) are reported and can be migrated by
	/// the "Fix Mod Plugins" menu entry.</para>
	///
	/// <para><b>Expected settings</b> — a plugin sitting under a platform folder must be compatible
	/// with that platform only (and with the Editor when that platform is the host platform), with
	/// the CPU matching the architecture folder (<c>x86_64</c>, <c>ARM64</c>, …; <c>AnyCPU</c> for
	/// managed binaries). A managed assembly left at the <c>Plugins</c> root must be compatible with
	/// "Any Platform".</para>
	///
	/// This matters because Unity only auto-detects its own folder names: with the nested
	/// <c>&lt;platform&gt;/&lt;arch&gt;</c> layout Unity cannot infer the platform anymore, so the
	/// <see cref="PluginImporter"/> settings have to be explicit.
	/// </summary>
	public static class ModPluginValidator {
		private const string Prefix      = "[ModPlugins] ";
		private const string ModFileName = "nox.mod.json";
		private const string PluginsName = "Plugins";
		private const string OnImportPref = "Nox.Editor.ValidateModPluginsOnImport";

		private const string MenuValidate  = "Nox/Tools/Validate Mod Plugins";
		private const string MenuFix       = "Nox/Tools/Fix Mod Plugins";
		private const string MenuOnImport  = "Nox/Tools/Validate Mod Plugins On Import";

		/// <summary>Extensions the validator looks at.</summary>
		private static readonly string[] PluginExtensions = {
			".dll", ".so", ".dylib", ".bundle", ".aar", ".jar", ".androidlib", ".a",
		};

		/// <summary>
		/// Build targets the validator reasons about. Restricted to modules that ship with every Unity
		/// install — console targets are left untouched (setting their compatibility can throw when the
		/// module is not installed).
		/// </summary>
		private static readonly BuildTarget[] Targets = {
			BuildTarget.StandaloneWindows,
			BuildTarget.StandaloneWindows64,
			BuildTarget.StandaloneLinux64,
			BuildTarget.StandaloneOSX,
			BuildTarget.Android,
			BuildTarget.iOS,
			BuildTarget.tvOS,
			BuildTarget.VisionOS,
			BuildTarget.WebGL,
		};

		/// <summary>Canonical platform folder name → platform.</summary>
		private static readonly Dictionary<string, Platform> PlatformFolders = new(StringComparer.OrdinalIgnoreCase) {
			{ "windows",  Platform.Windows  },
			{ "linux",    Platform.Linux    },
			{ "macos",    Platform.MacOS    },
			{ "android",  Platform.Android  },
			{ "ios",      Platform.IOS      },
			{ "visionos", Platform.VisionOS },
		};

		/// <summary>
		/// Architecture folder name → architecture. Mirrors <c>Library.InferArchitecture</c> so the
		/// validator and the mod builder agree on what a folder name means.
		/// </summary>
		private static readonly Dictionary<string, Architecture> ArchFolders = new(StringComparer.OrdinalIgnoreCase) {
			{ "x86",    Architecture.X86   },
			{ "x64",    Architecture.X64   },
			{ "x86_64", Architecture.X64   },
			{ "arm",    Architecture.Arm   },
			{ "armv7",  Architecture.Arm   },
			{ "arm64",  Architecture.Arm64 },
		};

		/// <summary>Canonical architecture folder name for a given architecture.</summary>
		private static readonly Dictionary<Architecture, string> ArchNames = new() {
			{ Architecture.X86,   "x86"   },
			{ Architecture.X64,   "x64"   },
			{ Architecture.Arm,   "arm"   },
			{ Architecture.Arm64, "arm64" },
		};

		/// <summary>Legacy folder name → the canonical sub-path that replaces it.</summary>
		private static readonly Dictionary<string, string> LegacyFolders = new(StringComparer.OrdinalIgnoreCase) {
			{ "win64", "windows/x64" },
			{ "win32", "windows/x86" },
			{ "win",   "windows"     },
			{ "osx",   "macos"       },
			{ "mac",   "macos"       },
		};

		/// <summary>
		/// Validates the mod plugins automatically when they are imported or moved.
		/// Never fixes anything — the postprocessor only reports (see <see cref="ValidateOnImport"/>).
		/// </summary>
		public static bool ValidateOnImport {
			get => EditorPrefs.GetBool(OnImportPref, true);
			set => EditorPrefs.SetBool(OnImportPref, value);
		}

		// ── Menu entries ──

		[MenuItem(MenuValidate, false, 700)]
		public static void ValidateMenu() {
			var issues = ValidateAll(fix: false);
			ReportIssues(issues);
		}

		[MenuItem(MenuFix, false, 701)]
		public static void FixMenu() {
			if (!EditorUtility.DisplayDialog(
				    "Fix Mod Plugins",
				    "Apply the expected PluginImporter settings to every mod plugin,\n" +
				    "and migrate the legacy plugin folders (win64, osx, x86_64, …)\n" +
				    "to <platform>/<arch>?",
				    "Fix", "Cancel"))
				return;

			var issues = ValidateAll(fix: true);
			AssetDatabase.Refresh();
			ReportIssues(issues);
		}

		[MenuItem(MenuOnImport, false, 702)]
		private static void ToggleOnImport()
			=> ValidateOnImport = !ValidateOnImport;

		[MenuItem(MenuOnImport, true)]
		private static bool ToggleOnImportValidate() {
			Menu.SetChecked(MenuOnImport, ValidateOnImport);
			return true;
		}

		/// <summary>
		/// Build-pipeline step: reports mod plugin problems before a build. Never modifies assets —
		/// run "Nox ▸ Tools ▸ Fix Mod Plugins" to apply the expected settings.
		/// </summary>
		[NoxInvokable("build:any")]
		public static bool ValidateBeforeBuild() {
			var issues = ValidateAll(fix: false);
			ReportIssues(issues);

			var errors = issues.Count(i => i.Severity == PluginIssueSeverity.Error);
			if (errors == 0) return true;

			Logger.LogWarning($"{Prefix}{errors} plugin placement/configuration error(s) — the build may ship " +
			                  "native plugins that get loaded on the wrong platform.", "build:any");
			return false;
		}

		// ── Public helpers ──

		public static bool IsPluginAsset(string path)
			=> Array.IndexOf(PluginExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;

		/// <summary>
		/// Project-relative <c>Plugins</c> folders of every mod, discovered from the
		/// <c>nox.mod.json</c> manifests under <c>Assets/</c> and <c>Packages/</c>
		/// (<c>Library/PackageCache</c> is skipped: it only mirrors <c>Packages/</c>).
		/// </summary>
		public static List<string> GetModPluginRoots() {
			var roots = new List<string>();

			foreach (var searchRoot in new[] { "Assets", "Packages" }) {
				if (!Directory.Exists(searchRoot)) continue;

				foreach (var manifest in Directory.GetFiles(searchRoot, ModFileName, SearchOption.AllDirectories)) {
					var modDir = Path.GetDirectoryName(manifest);
					if (string.IsNullOrEmpty(modDir)) continue;

					var plugins = ToAssetPath(Path.Combine(modDir, PluginsName));
					if (Directory.Exists(plugins) && !roots.Contains(plugins, StringComparer.OrdinalIgnoreCase))
						roots.Add(plugins);
				}
			}

			return roots;
		}

		/// <summary>Validates every plugin of every mod. Returns the issues found (also logged by the menu entries).</summary>
		public static List<PluginIssue> ValidateAll(bool fix) {
			var paths = new List<string>();

			foreach (var root in GetModPluginRoots()) {
				if (!Directory.Exists(root)) continue;

				foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
					if (IsPluginAsset(file))
						paths.Add(ToAssetPath(file));
			}

			return Validate(paths, fix, logSummary: true);
		}

		/// <summary>Validates the given asset paths (used by the <see cref="AssetPostprocessor"/>).</summary>
		public static List<PluginIssue> ValidatePaths(IEnumerable<string> assetPaths, bool fix)
			=> Validate(
				assetPaths.Where(IsPluginAsset).Select(ToAssetPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
				fix,
				logSummary: false);

		// ── Core ──

		private static List<PluginIssue> Validate(List<string> assetPaths, bool fix, bool logSummary) {
			var issues = new List<PluginIssue>();
			var moves  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var roots  = GetModPluginRoots();

			var checkedCount = 0;
			var missingCount = 0;

			foreach (var path in assetPaths) {
				var root = roots.FirstOrDefault(r => path.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase));
				if (root == null) continue;

				var importer = AssetImporter.GetAtPath(path) as PluginImporter;
				if (importer == null) {
					missingCount++;
					if (!fix)
						issues.Add(new PluginIssue(path, PluginIssueSeverity.Warning,
							"not imported yet — run Assets ▸ Refresh, then validate again."));
					continue;
				}

				checkedCount++;
				ValidateAsset(importer, path, root, fix, moves, issues);
			}

			if (fix && moves.Count > 0)
				ApplyMoves(moves);

			if (logSummary)
				Logger.Log($"{Prefix}checked {checkedCount} plugin(s) in {roots.Count} mod(s) — " +
				           $"{issues.Count(i => i.Severity == PluginIssueSeverity.Error)} error(s), " +
				           $"{issues.Count(i => i.Severity == PluginIssueSeverity.Warning)} warning(s)" +
				           (missingCount > 0 ? $", {missingCount} not imported" : "") + ".");

			return issues;
		}

		private static void ValidateAsset(PluginImporter importer, string path, string pluginsRoot, bool fix,
			Dictionary<string, string> moves, List<PluginIssue> issues) {
			var extension     = Path.GetExtension(path).ToLowerInvariant();
			var isNative      = importer.isNativePlugin;
			var isAndroidOnly = extension is ".aar" or ".jar" or ".androidlib";
			var needsPlatform = isNative || isAndroidOnly;

			var relative = path.Substring(pluginsRoot.Length).TrimStart('/');
			var segments = relative.Split('/');
			var folders  = segments.Take(segments.Length - 1).ToArray();

			// ── 1. placement ──
			var placement = ParsePlacement(folders, extension);
			if (placement.Error != null) {
				issues.Add(new PluginIssue(path, PluginIssueSeverity.Error,
					$"{placement.Error}. Expected 'Plugins/<platform>[/<arch>]/'" +
					(placement.Suggestion != null ? $", e.g. 'Plugins/{placement.Suggestion}/'" : "") + "."));

				if (fix && placement.Suggestion != null)
					moves[pluginsRoot + "/" + string.Join("/", folders)] =
						pluginsRoot + "/" + placement.Suggestion;

				return; // settings of a misplaced plugin are meaningless
			}

			if (placement.Warning != null) {
				issues.Add(new PluginIssue(path, PluginIssueSeverity.Warning, placement.Warning + "."));

				// Canonicalise the folder name as well (e.g. "windows/x86_64" → "windows/x64").
				if (fix && placement.CanonicalFolder != null)
					moves[pluginsRoot + "/" + placement.Folder] = pluginsRoot + "/" + placement.CanonicalFolder;
			}

			if (placement.Platform == Platform.None && isNative) {
				issues.Add(new PluginIssue(path, PluginIssueSeverity.Error,
					"native plugin at the Plugins root — move it under <platform>[/<arch>] so Unity does not " +
					"ship and load it on every platform."));

				var nativeSuggestion = PlatformFromExtension(extension);
				if (fix && nativeSuggestion != Platform.None)
					moves[path] = pluginsRoot + "/" + nativeSuggestion.GetPlatformName() + "/" + Path.GetFileName(path);

				return;
			}

			// ── 2. settings ──
			var settings = BuildExpectedSettings(placement, needsPlatform);
			var changed  = false;

			if (settings.Any)
				ValidateAnyPlatformSettings(importer, path, fix, issues, ref changed);
			else
				ValidatePlatformSettings(importer, path, fix, issues, placement, settings, ref changed);

			// ── 3. informational: Unity must have loaded the module on Windows ──
			// LibManager resolves Windows handles through GetModuleHandle, which only finds a module
			// Unity already loaded. 'Preloaded' is what guarantees it, so flag it (never auto-fixed:
			// it changes startup cost).
			if (isNative && placement.Platform == Platform.Windows && !importer.isPreloaded)
				issues.Add(new PluginIssue(path, PluginIssueSeverity.Warning,
					"native Windows plugin is not 'Preloaded' — Nox.ModLoader resolves Windows modules via " +
					"GetModuleHandle, which requires Unity to have loaded it first."));

			if (fix && changed)
				importer.SaveAndReimport();
		}

		/// <summary>Folder segments between <c>Plugins</c> and the file → platform/architecture, or a placement error.</summary>
		private static Placement ParsePlacement(string[] folders, string extension) {
			var result = new Placement {
				Platform = Platform.None,
				Arch     = Architecture.None,
				Folder   = string.Join("/", folders),
			};

			if (folders.Length == 0) return result; // Plugins/<file> — the runtime's last-resort fallback

			var first = folders[0];

			if (PlatformFolders.TryGetValue(first, out var platform)) {
				result.Platform = platform;

				if (folders.Length == 1) return result;

				if (folders.Length == 2 && ArchFolders.TryGetValue(folders[1], out var arch)) {
					result.Arch = arch;

					if (ArchNames.TryGetValue(arch, out var canonical) &&
					    !canonical.Equals(folders[1], StringComparison.OrdinalIgnoreCase)) {
						result.Warning         = $"architecture folder '{folders[1]}' — prefer the canonical name '{canonical}'";
						result.CanonicalFolder = platform.GetPlatformName() + "/" + canonical;
					}

					return result;
				}

				result.Error = folders.Length > 2
					? $"'{string.Join("/", folders)}' is nested too deep"
					: $"unknown architecture folder '{folders[1]}'";
				return result;
			}

			if (LegacyFolders.TryGetValue(first, out var legacy)) {
				result.Error = $"legacy folder '{first}'";
				result.Suggestion = folders.Length == 2 && ArchFolders.TryGetValue(folders[1], out var legacyArch)
					? legacy + "/" + ArchNames[legacyArch]
					: legacy;
				return result;
			}

			if (ArchFolders.TryGetValue(first, out var archOnly)) {
				result.Error = $"architecture-only folder '{first}'";
				var hinted = PlatformFromExtension(extension);
				result.Suggestion = hinted == Platform.None
					? null
					: hinted.GetPlatformName() + "/" + ArchNames[archOnly];
				return result;
			}

			result.Error = folders.Length == 1
				? $"unknown folder '{first}'"
				: $"unknown platform folder '{first}'";
			return result;
		}

		/// <summary>Builds the settings every plugin of this kind must have.</summary>
		private static ExpectedSettings BuildExpectedSettings(Placement placement, bool needsPlatform) {
			var settings = new ExpectedSettings();

			if (placement.Platform == Platform.None && !needsPlatform) {
				// Platform-agnostic managed assembly at the Plugins root.
				settings.Any = true;
				return settings;
			}

			if (placement.Platform == Platform.None) {
				settings.Invalid = "an Android-only plugin (.aar/.jar/.androidlib) must live under 'android/'";
				return settings;
			}

			settings.Target = ToBuildTarget(placement.Platform, placement.Arch);
			if (settings.Target == null) {
				settings.Invalid = $"'{placement.Platform.GetPlatformName()}' cannot be mapped to a Unity build target";
				return settings;
			}

			settings.Editor = placement.Platform == PlatformExtensions.RuntimePlatform;

			// No <arch> folder → the binary is architecture-agnostic, leave the CPU untouched.
			// With one, a native plugin must declare it; a managed one stays 'AnyCPU'.
			if (placement.Arch != Architecture.None)
				settings.Cpu = needsPlatform ? CpuFor(placement.Arch, managed: false) : "AnyCPU";

			return settings;
		}

		private static void ValidateAnyPlatformSettings(PluginImporter importer, string path, bool fix,
			List<PluginIssue> issues, ref bool changed) {
			if (!importer.GetCompatibleWithAnyPlatform()) {
				issues.Add(new PluginIssue(path, PluginIssueSeverity.Error,
					"managed assembly must be compatible with 'Any Platform'."));
				if (fix) { importer.SetCompatibleWithAnyPlatform(true); changed = true; }
			}

			foreach (var target in Targets) {
				if (!GetExclude(importer, target)) continue;

				issues.Add(new PluginIssue(path, PluginIssueSeverity.Error,
					$"excluded from '{target}' while 'Any Platform' is enabled."));
				if (fix) { SetExclude(importer, target, false); changed = true; }
			}

			if (importer.GetExcludeEditorFromAnyPlatform()) {
				issues.Add(new PluginIssue(path, PluginIssueSeverity.Error,
					"excluded from the Editor while 'Any Platform' is enabled."));
				if (fix) { importer.SetExcludeEditorFromAnyPlatform(false); changed = true; }
			}
		}

		private static void ValidatePlatformSettings(PluginImporter importer, string path, bool fix,
			List<PluginIssue> issues, Placement placement, ExpectedSettings expected, ref bool changed) {
			if (expected.Invalid != null) {
				issues.Add(new PluginIssue(path, PluginIssueSeverity.Error, expected.Invalid + "."));
				return;
			}

			var target = expected.Target.Value;

			if (importer.GetCompatibleWithAnyPlatform()) {
				issues.Add(new PluginIssue(path, PluginIssueSeverity.Error,
					$"compatible with 'Any Platform' — it must be restricted to '{target}'."));
				if (fix) { importer.SetCompatibleWithAnyPlatform(false); changed = true; }
			}

			foreach (var candidate in Targets) {
				var wanted = candidate == target;
				if (GetCompatible(importer, candidate) == wanted) continue;

				issues.Add(new PluginIssue(path, PluginIssueSeverity.Error,
					wanted
						? $"not compatible with '{target}' although it sits under 'Plugins/{placement.Folder}/'."
						: $"compatible with '{candidate}' although it sits under 'Plugins/{placement.Folder}/'."));

				if (fix && SetCompatible(importer, candidate, wanted)) changed = true;
			}

			if (importer.GetCompatibleWithEditor() != expected.Editor) {
				issues.Add(new PluginIssue(path, PluginIssueSeverity.Error,
					expected.Editor
						? "not compatible with the Editor although it targets the host platform."
						: $"compatible with the Editor although it targets '{placement.Platform.GetPlatformName()}'."));
				if (fix) { importer.SetCompatibleWithEditor(expected.Editor); changed = true; }
			}

			if (expected.Cpu == null) return;

			string cpu = null;
			try { cpu = importer.GetPlatformData(target, "CPU"); } catch { /* module not available */ }

			if (cpu == expected.Cpu) return;

			issues.Add(new PluginIssue(path, PluginIssueSeverity.Error,
				$"CPU is '{cpu ?? "unset"}' for '{target}' but the folder says '{expected.Cpu}'."));
			if (fix) {
				try { importer.SetPlatformData(target, "CPU", expected.Cpu); changed = true; } catch { /* module not available */ }
			}
		}

		// ── Fixing ──

		private static void ApplyMoves(Dictionary<string, string> moves) {
			// Legacy platform/arch folders can collapse onto the same destination (e.g. "win64" and
			// "x86_64" both becoming "windows/x64"): the destination is created once, then every source is
			// merged into it entry by entry instead of moving the folder itself.
			foreach (var group in moves.GroupBy(m => m.Value, StringComparer.OrdinalIgnoreCase)) {
				var destination = group.Key;
				EnsureAssetFolder(destination);

				foreach (var (source, _) in group) {
					var asset = ToAssetPath(source);

					try {
						if (AssetDatabase.IsValidFolder(asset)) {
							foreach (var child in Directory.GetDirectories(asset))
								MoveOne(child, destination);
							foreach (var child in Directory.GetFiles(asset))
								if (!child.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
									MoveOne(child, destination);

							AssetDatabase.DeleteAsset(asset);
						} else if (File.Exists(asset)) {
							MoveOne(asset, destination);
						}
					} catch (Exception e) {
						Logger.LogWarning($"{Prefix}could not move '{asset}': {e.Message}");
					}
				}
			}
		}

		private static void MoveOne(string source, string destinationFolder) {
			var asset = ToAssetPath(source);
			var fileName = Path.GetFileName(asset);
			var error = AssetDatabase.MoveAsset(asset, destinationFolder + "/" + fileName);
			if (string.IsNullOrEmpty(error))
				Logger.Log($"{Prefix}moved '{asset}' → '{destinationFolder}/{fileName}'.");
			else
				Logger.LogWarning($"{Prefix}could not move '{asset}': {error}");
		}

		private static void EnsureAssetFolder(string folder) {
			if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;

			var parent = ToAssetPath(Path.GetDirectoryName(folder));
			if (string.IsNullOrEmpty(parent) || parent == folder) return;

			EnsureAssetFolder(parent);
			if (!AssetDatabase.IsValidFolder(folder))
				AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
		}

		// ── Reporting ──

		/// <summary>Logs the issues found by <see cref="ValidateAll"/> / <see cref="ValidatePaths"/>.</summary>
		public static void ReportIssues(List<PluginIssue> issues) {
			if (issues.Count == 0) {
				Logger.Log($"{Prefix}every mod plugin is correctly placed and configured.");
				return;
			}

			foreach (var group in issues.GroupBy(i => i.AssetPath)) {
				var context = AssetDatabase.LoadAssetAtPath<UObject>(group.Key);

				var label = string.IsNullOrEmpty(group.Key) ? string.Empty : group.Key + ": ";

				foreach (var issue in group)
					if (issue.Severity == PluginIssueSeverity.Error) Logger.LogError(Prefix + label + issue.Message, context);
					else Logger.LogWarning(Prefix + label + issue.Message, context);
			}
		}

		// ── Mapping helpers ──

		private static BuildTarget? ToBuildTarget(Platform platform, Architecture arch) {
			if (platform == Platform.Windows)  return arch == Architecture.X86 ? BuildTarget.StandaloneWindows : BuildTarget.StandaloneWindows64;
			if (platform == Platform.Linux)    return BuildTarget.StandaloneLinux64;
			if (platform == Platform.MacOS)    return BuildTarget.StandaloneOSX;
			if (platform == Platform.Android)  return BuildTarget.Android;
			if (platform == Platform.IOS)      return BuildTarget.iOS;
			if (platform == Platform.VisionOS) return BuildTarget.VisionOS;
			return null;
		}

		/// <summary>Unity's <c>CPU</c> platform data value for an architecture folder.</summary>
		private static string CpuFor(Architecture arch, bool managed) {
			if (managed) return "AnyCPU";

			return arch switch {
				Architecture.X86   => "x86",
				Architecture.X64   => "x86_64",
				Architecture.Arm   => "ARMv7",
				Architecture.Arm64 => "ARM64",
				_                  => null,
			};
		}

		/// <summary>Best-effort platform guess from the binary extension (<c>.so</c> is shared by Linux and Android).</summary>
		private static Platform PlatformFromExtension(string extension)
			=> extension switch {
				".dll"   => Platform.Windows,
				".dylib" => Platform.MacOS,
				".bundle" => Platform.MacOS,
				_        => Platform.None,
			};

		private static string ToAssetPath(string path)
			=> string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/').TrimStart('.', '/');

		// ── PluginImporter accessors, tolerant to missing Unity modules ──

		private static bool GetCompatible(PluginImporter importer, BuildTarget target) {
			try { return importer.GetCompatibleWithPlatform(target); } catch { return false; }
		}

		private static bool SetCompatible(PluginImporter importer, BuildTarget target, bool value) {
			try { importer.SetCompatibleWithPlatform(target, value); return true; } catch { return false; }
		}

		private static bool GetExclude(PluginImporter importer, BuildTarget target) {
			try { return importer.GetExcludeFromAnyPlatform(target); } catch { return false; }
		}

		private static void SetExclude(PluginImporter importer, BuildTarget target, bool value) {
			try { importer.SetExcludeFromAnyPlatform(target, value); } catch { /* module not available */ }
		}

		// ── Nested types ──

		private struct Placement {
			internal Platform Platform;
			internal Architecture Arch;
			internal string Folder;
			internal string Error;
			internal string Warning;
			internal string Suggestion;
			internal string CanonicalFolder;
		}

		private sealed class ExpectedSettings {
			internal bool Any;
			internal BuildTarget? Target;
			internal bool Editor;
			internal string Cpu;
			internal string Invalid;
		}
	}

	/// <summary>
	/// Reports mod plugin placement/configuration problems as soon as a plugin is imported or moved.
	/// Reporting only — use "Nox ▸ Tools ▸ Fix Mod Plugins" to apply the settings.
	/// </summary>
	public class ModPluginPostprocessor : AssetPostprocessor {
		private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets,
			string[] movedFromAssetPaths) {
			if (!ModPluginValidator.ValidateOnImport) return;

			var touched = importedAssets.Concat(movedAssets)
				.Where(ModPluginValidator.IsPluginAsset)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();

			if (touched.Length == 0) return;

			var issues = ModPluginValidator.ValidatePaths(touched, fix: false);
			if (issues.Count > 0) ModPluginValidator.ReportIssues(issues);
		}
	}
}
#endif

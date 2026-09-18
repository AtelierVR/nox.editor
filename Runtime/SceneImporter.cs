#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Editor {
	public class SceneImporter : AssetPostprocessor {
		/// <summary>
		/// Scenes owned by packages/mods are loaded at runtime through the asset API
		/// (see <c>EditorKernelAssetAPI.LoadInternalWorld</c>), which opens them with
		/// <c>EditorSceneManager.OpenScene</c> and therefore needs no build settings entry.
		/// Registering them here would also mean writing UPM-owned content into
		/// ProjectSettings, so they are deliberately skipped.
		/// </summary>
		private const string PackagesPrefix = "Packages/";

		private static bool IsTrackedScene(string asset)
			=> asset.EndsWith(".unity") && !asset.StartsWith(PackagesPrefix);

		/// <summary>
		/// Runs at editor startup. <c>OnPostprocessAllAssets</c> is only raised when the
		/// asset database changes, so a scene already present in the project but missing
		/// from the build settings would otherwise never be registered.
		/// </summary>
		[InitializeOnLoadMethod]
		private static void OnEditorLoad() {
			// Deferred: the asset database may still be importing when this runs.
			EditorApplication.delayCall += () => RefreshScenes(logWhenUpToDate: false);
		}

		private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths) {
			var scenes = EditorBuildSettings.scenes.ToList();

			foreach (var asset in importedAssets)
				if (IsTrackedScene(asset)) {
					Logger.Log("Scene imported: " + asset);
					if (scenes.All(s => s.path != asset))
						scenes.Add(new EditorBuildSettingsScene(asset, true));
				}

			foreach (var asset in deletedAssets)
				if (asset.EndsWith(".unity")) {
					Logger.Log("Scene deleted: " + asset);
					scenes.RemoveAll(s => s.path == asset);
				}

			foreach (var asset in movedAssets)
				if (IsTrackedScene(asset)) {
					Logger.Log("Scene moved: " + asset);
					// Reuse the existing entry so its enabled flag survives the move.
					var previous = scenes.FirstOrDefault(s => s.path == asset);
					if (previous != null) scenes.Remove(previous);
					scenes.Add(new EditorBuildSettingsScene(asset, previous?.enabled ?? true));
				}

			foreach (var asset in movedFromAssetPaths)
				if (asset.EndsWith(".unity")) {
					Logger.Log("Scene moved from: " + asset);
					scenes.RemoveAll(s => s.path == asset);
				}

			Commit(scenes);
		}


		[MenuItem("Nox/Tools/Refresh Scenes in Build Settings")]
		public static void RefreshScenesInBuildSettings()
			=> RefreshScenes(logWhenUpToDate: true);

		/// <summary>
		/// Adds every tracked scene missing from the build settings without rebuilding the
		/// existing entries, so their <c>enabled</c> flag is preserved.
		/// </summary>
		private static void RefreshScenes(bool logWhenUpToDate) {
			var scenes = EditorBuildSettings.scenes.ToList();
			var known  = new HashSet<string>(scenes.Select(s => s.path));

			foreach (var path in AssetDatabase.FindAssets("t:Scene")
				         .Select(AssetDatabase.GUIDToAssetPath)
				         .Where(IsTrackedScene)
				         .Distinct()) {
				if (!known.Add(path))
					continue;
				Logger.Log("Scene added to build settings: " + path);
				scenes.Add(new EditorBuildSettingsScene(path, true));
			}

			if (!Commit(scenes) && logWhenUpToDate)
				Logger.Log("Scenes in build settings are already up to date.");
		}

		/// <summary>
		/// Writes <paramref name="scenes"/> only when it actually differs from the current
		/// build settings. Both the paths and the enabled flags are compared, so a
		/// disable/enable is detected and a pure reorder is not rewritten.
		/// </summary>
		/// <returns><c>true</c> when a write happened.</returns>
		private static bool Commit(IEnumerable<EditorBuildSettingsScene> scenes) {
			var next = scenes.Distinct().ToArray();
			if (Signature(EditorBuildSettings.scenes).SequenceEqual(Signature(next)))
				return false;

			EditorBuildSettings.scenes = next;
			AssetDatabase.SaveAssets();
			Logger.Log("Updated scenes in build settings.");
			return true;
		}

		private static string[] Signature(IEnumerable<EditorBuildSettingsScene> scenes)
			=> scenes
				.Select(scene => (scene.enabled ? "1|" : "0|") + scene.path)
				.OrderBy(key => key, StringComparer.Ordinal)
				.ToArray();
	}
}
#endif
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.SceneManagement;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Editor {
	public class SceneImporter : AssetPostprocessor {
		private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths) {
			var old = EditorBuildSettings.scenes.ToArray();

			var scenes = EditorBuildSettings.scenes.ToList();
			foreach (var asset in importedAssets)
				if (asset.EndsWith(".unity") && !asset.StartsWith("Packages/")) {
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
				if (asset.EndsWith(".unity") && !asset.StartsWith("Packages/")) {
					Logger.Log("Scene moved: " + asset);
					var oldScene = scenes.FirstOrDefault(s => s.path == asset);
					if (oldScene != null) scenes.Remove(oldScene);
					scenes.Add(new EditorBuildSettingsScene(asset, oldScene?.enabled ?? true));
				}

			foreach (var asset in movedFromAssetPaths)
				if (asset.EndsWith(".unity")) {
					Logger.Log("Scene moved from: " + asset);
					scenes.RemoveAll(s => s.path == asset);
				}

			var newScenes = scenes.Distinct().ToArray();

			var newPaths = new HashSet<string>(newScenes.Select(s => s.path));
			var oldPaths = new HashSet<string>(old.Select(s => s.path));
			var hasChanges = !newPaths.SetEquals(oldPaths);
			if (!hasChanges) return;

			EditorBuildSettings.scenes = newScenes;
			AssetDatabase.SaveAssets();
			Logger.Log("Updated scenes in build settings.");
		}


		[MenuItem("Nox/Tools/Refresh Scenes in Build Settings")]
		public static void RefreshScenesInBuildSettings() {
			var oldScenes = EditorBuildSettings.scenes;
			var newScenes = AssetDatabase.FindAssets("t:Scene")
				.Select(AssetDatabase.GUIDToAssetPath)
				.Select(path => new EditorBuildSettingsScene(path, true))
				.Distinct()
				.ToArray();

			if (!oldScenes.Except(newScenes).Any()) {
				Logger.Log("Scenes in build settings are already up to date.");
				return;
			}

			EditorBuildSettings.scenes = newScenes;
			AssetDatabase.SaveAssets();
			Logger.Log("Refreshed scenes in build settings.");
		}
	}
}
#endif
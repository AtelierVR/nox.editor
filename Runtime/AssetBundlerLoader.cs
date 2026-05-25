#if UNITY_EDITOR
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Editor {
	public class AssetBundleLoader : EditorWindow {
		[MenuItem("Nox/Tools/AssetBundle Loader")]
		public static void ShowWindow()
			=> GetWindow<AssetBundleLoader>("AssetBundle Loader");

		public string      file = Constants.CachePath;
		public AssetBundle asset;

		private void OnGUI() {
			GUILayout.Label("AssetBundle Loader", EditorStyles.boldLabel);
			EditorGUILayout.Space();
			file = EditorGUILayout.TextField("File", file);
			if (GUILayout.Button("Open AssetBundle"))
				Open(file).Forget();
		}

		public async UniTask Open(string path) {
			if (string.IsNullOrEmpty(path)) {
				Logger.LogError("File path is empty");
				return;
			}

			if (asset) {
				await asset.UnloadAsync(false);
				asset = null;
			}

			asset = await AssetBundle.LoadFromFileAsync(path);
			if (!asset) {
				Logger.LogError($"Failed to load AssetBundle from {path}");
				return;
			}

			var scenes = asset.GetAllScenePaths();
			if (scenes.Length == 0) {
				Logger.LogError($"No scenes found in AssetBundle {path}");
				asset.Unload(false);
				return;
			}

			var scenePath = scenes[0];
			await SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
			var opened = SceneManager.GetSceneByPath(scenePath);

			if (!opened.IsValid()) {
				Logger.LogError($"Failed to open scene {scenePath} from AssetBundle {path}");
				asset.Unload(false);
				return;
			}

			Logger.Log($"Successfully opened scene {scenePath} from AssetBundle {path}");
		}
	}
}
#endif
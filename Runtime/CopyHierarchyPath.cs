#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Nox.Editor {
	public static class CopyHierarchyPath {
		// ── GameObject ──────────────────────────────────────────────────────────

		[MenuItem("GameObject/Copy Path", false, 0)]
		private static void CopyGameObjectPath(MenuCommand command) {
			if (command.context is not GameObject go) return;
			// Skip duplicate calls when multiple objects are selected
			if (command.context != Selection.activeObject) return;
			EditorGUIUtility.systemCopyBuffer = BuildPath(go);
		}

		[MenuItem("GameObject/Copy Path", true)]
		private static bool CopyGameObjectPathValidate() => Selection.activeGameObject != null;

		// ── Component (Inspector context menu) ──────────────────────────────────

		[MenuItem("CONTEXT/Component/Copy Path", false, 10000)]
		private static void CopyComponentPath(MenuCommand command) {
			if (command.context is not Component comp) return;
			var path = BuildPath(comp.gameObject) + $" [{comp.GetType().Name}]";
			EditorGUIUtility.systemCopyBuffer = path;
		}

		// ── Helpers ─────────────────────────────────────────────────────────────

		/// <summary>Returns "SceneName/Root/Child/…/GameObject".</summary>
		private static string BuildPath(GameObject go) {
			var scene = go.scene;
			var sceneName = string.IsNullOrEmpty(scene.name)
				? "DontDestroyOnLoad"
				: scene.name;

			return sceneName + "/" + GetTransformPath(go.transform);
		}

		private static string GetTransformPath(Transform t) {
			if (t.parent == null) return t.name;
			return GetTransformPath(t.parent) + "/" + t.name;
		}
	}
}
#endif

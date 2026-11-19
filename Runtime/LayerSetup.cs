#if UNITY_EDITOR
using Nox.CCK.Utils;
using UnityEditor;
using UnityEngine;

namespace Nox.Editor {
	/// <summary>
	/// Creates the project layers the Nox runtime resolves by name (<see cref="Layers.Required"/>), so a
	/// project that never ran the AutoHand wizard still gets a working collision matrix instead of the
	/// <c>-1</c> lookups that silently disable the hand physics.
	/// </summary>
	[InitializeOnLoad]
	public static class LayerSetup {
		private const string MENU_PATH = "Nox/Settings/Ensure Layers";

		static LayerSetup()
			=> EditorApplication.delayCall += EnsureLayers;

		[MenuItem(MENU_PATH, false, 1001)]
		private static void EnsureLayersMenu()
			=> EnsureLayers();

		private static void EnsureLayers() {
			var created = Layers.EnsureRequired();

			if (created.Length == 0)
				return;

			AssetDatabase.Refresh();
			Debug.Log($"[Nox] Missing layers created: {string.Join(", ", created)}");
		}
	}
}
#endif

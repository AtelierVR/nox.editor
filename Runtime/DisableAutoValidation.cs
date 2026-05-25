#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Nox.Editor {
	/// <summary>
	/// Désactive les fenêtres de validation automatique qui s'ouvrent au démarrage
	/// et peuvent causer des bugs d'interface
	/// </summary>
	public static class DisableAutoValidation {
		private const string MENU_PATH = "Nox/Settings/Disable Auto Validation Windows";
		private const string PREF_KEY = "Nox.DisableAutoValidation";

		[MenuItem(MENU_PATH, false, 1000)]
		private static void ToggleDisableAutoValidation() {
			bool current = IsDisabled();
			SetDisabled(!current);
			Debug.Log($"Auto validation windows: {(IsDisabled() ? "DISABLED" : "ENABLED")}");
		}

		[MenuItem(MENU_PATH, true)]
		private static bool ToggleDisableAutoValidationValidate() {
			Menu.SetChecked(MENU_PATH, IsDisabled());
			return true;
		}

		public static bool IsDisabled() {
			return EditorPrefs.GetBool(PREF_KEY, false);
		}

		public static void SetDisabled(bool disabled) {
			EditorPrefs.SetBool(PREF_KEY, disabled);
		}
	}
}
#endif


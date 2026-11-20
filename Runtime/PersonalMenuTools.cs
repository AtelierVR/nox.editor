#if UNITY_EDITOR
using UnityEditor;
using Nox.Controllers;
using Nox.Controllers.Runtime;

namespace Nox.Editor {
	/// <summary>
	/// Opens or closes the menu of the current controller from the menu bar, so a menu problem can be
	/// checked without putting the headset back on. The entry is only offered while the game runs and the
	/// current controller drives a menu (see <see cref="IControllerMenu"/>); the check mark follows the
	/// shown state.
	/// </summary>
	public static class PersonalMenuTools {
		private const string MENU_PATH = "Nox/Tools/Menu";

		[MenuItem(MENU_PATH, false, 100)]
		private static void Toggle() {
			var menu = FindMenu();
			if (menu == null)
				return;

			menu.IsOpen = !menu.IsOpen;
		}

		[MenuItem(MENU_PATH, true)]
		private static bool ToggleValidate() {
			var menu = FindMenu();

			Menu.SetChecked(MENU_PATH, menu != null && menu.IsOpen);

			return menu != null;
		}

		/// <summary>
		/// Menu of the current controller, or <c>null</c> outside Play Mode and when the controller drives
		/// no menu (the desktop rig is built without one).
		/// </summary>
		private static IControllerMenu FindMenu() {
			if (!EditorApplication.isPlaying)
				return null;

			return Main.Instance != null
				? Main.Instance.Current as IControllerMenu
				: null;
		}
	}
}
#endif

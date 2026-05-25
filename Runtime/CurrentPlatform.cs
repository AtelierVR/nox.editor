#if UNITY_EDITOR
using UnityEditor;
using PlatformExtensions = Nox.CCK.Utils.PlatformExtensions;

namespace Nox.Editor {
	public class CurrentPlatform {
		[MenuItem("Nox/Platform/Switch to Runtime")]
		public static void SwitchPlatform() {
			var runtime = PlatformExtensions.RuntimePlatform;
			var current = PlatformExtensions.CurrentPlatform;
			if (runtime == current) {
				EditorUtility.DisplayDialog("Platform Switch", "You are already on the initial platform.", "OK");
				return;
			}

			if (EditorUtility.DisplayDialog(
				    "Platform Switch",
				    $"You are currently on {current}, but the initial platform is {runtime}. Do you want to switch?",
				    "Yes", "No"
			    )) {
				PlatformExtensions.CurrentPlatform = runtime;
				EditorUtility.DisplayDialog("Platform Switch", $"Switched to {runtime}.", "OK");
			} else {
				EditorUtility.DisplayDialog("Platform Switch", "No changes made.", "OK");
			}
		}
	}
}
#endif
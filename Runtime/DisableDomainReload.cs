using System;
using Nox.CCK.Utils;
using UnityEditor;

namespace Nox.Editor {
	/// <summary>
	/// Turns Unity's domain reload off when entering Play Mode (Editor Settings ▸ Enter Play Mode
	/// Options enabled, "Reload Domain" unchecked).
	/// <para>
	/// With the reload enabled, every press of Play rebuilds the whole scripting domain and re-imports
	/// the asset database. Measured on this project it costs 13.8-17.6 s of blocked editor time
	/// (<c>Asset Pipeline Refresh … Initiated by StopAssetImportingV2(ForceSynchronousImport |
	/// ForceDomainReload)</c>), of which <c>ImportOutOfDateAssets</c> 8.9-11 s with <i>zero</i> changed
	/// asset files, 4.5-5.8 s of actual domain reload, and 2.5 s re-running the editor-side
	/// <c>[InitializeOnLoadMethod]</c>s (the mod loader bootstrap). The loader supports Play Mode
	/// without a reload — see <c>Nox.ModLoader.Loader.EditorLoader</c> — so the reload is pure
	/// startup cost here.
	/// </para>
	/// <para>
	/// Use <c>Nox/Play Mode/Disable Domain Reload</c> to opt out; the choice is stored in the editor
	/// config and re-applied on every editor start.
	/// </para>
	/// </summary>
	[InitializeOnLoad]
	public static class DisableDomainReload {
		private const string MenuPath  = "Nox/Play Mode/Disable Domain Reload";
		private const string ConfigKey = "disable_domain_reload";

		static DisableDomainReload() {
			// Deferred to the end of the first update: writing EditorSettings while the settings
			// asset is still being imported would be overwritten.
			EditorApplication.delayCall += () => Apply(Enabled);
		}

		/// <summary>Whether the auto-disable is active. Persisted in the editor config.</summary>
		public static bool Enabled {
			get => Config.LoadEditor().Get(ConfigKey, true);
			set {
				var config = Config.LoadEditor();
				config.Set(ConfigKey, value);
				config.Save();
			}
		}

		[MenuItem(MenuPath, false)]
		private static void Toggle() {
			Enabled = !Enabled;
			Apply(Enabled);

			Logger.Log(Enabled
				? "Domain reload disabled when entering Play Mode (effective on the next Play)."
				: "Domain reload restored when entering Play Mode (effective on the next Play).");
		}

		[MenuItem(MenuPath, true)]
		private static bool ToggleValidate() {
			Menu.SetChecked(MenuPath, Enabled);
			return true;
		}

		/// <summary>
		/// Applies (or reverts) the option. Only flags owned by this class are touched, and
		/// <c>ProjectSettings/EditorSettings.asset</c> is only written when something actually
		/// changes, so the file is not dirtied on every editor start.
		/// </summary>
		private static void Apply(bool disableDomainReload) {
			try {
				var options = EditorSettings.enterPlayModeOptions;
				var enabled = EditorSettings.enterPlayModeOptionsEnabled;
				var changed = false;

				// "Enter Play Mode Options" has to be on for the flags to be honoured: with it off,
				// Unity always reloads the domain.
				if (disableDomainReload) {
					if (!enabled) {
						enabled = true;
						changed = true;
					}

					if ((options & EnterPlayModeOptions.DisableDomainReload) == 0) {
						options |= EnterPlayModeOptions.DisableDomainReload;
						changed = true;
					}
				} else {
					if ((options & EnterPlayModeOptions.DisableDomainReload) != 0) {
						options &= ~EnterPlayModeOptions.DisableDomainReload;
						changed = true;
					}

					// Nothing else left to disable: going back to "options off" restores the default
					// behaviour (both the domain and the scene are reloaded).
					if (enabled && options == EnterPlayModeOptions.None) {
						enabled = false;
						changed = true;
					}
				}

				if (!changed)
					return;

				EditorSettings.enterPlayModeOptionsEnabled = enabled;
				EditorSettings.enterPlayModeOptions        = options;
			} catch (Exception e) {
				// A settings API change must never break the editor startup.
				Logger.LogWarning($"[{nameof(DisableDomainReload)}] Could not update the Play Mode options: {e.Message}");
			}
		}
	}
}

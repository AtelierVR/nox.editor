using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Nox.Editor {
	/// <summary>
	/// Removes the scene backups Unity keeps in <c>Temp/__Backupscenes</c>. There is no switch to disable them: the
	/// recovery dialog and the backup itself are native (Unity 6 has no <c>SceneBackupManager</c> type and no
	/// preference for it), and the dialog is only shown when such a file is found when the project opens.
	/// <para>
	/// The folder is therefore emptied when the domain loads, when play mode ends and when the editor quits, so a
	/// session that ends cleanly never leaves anything behind, and a leftover from a crash is dropped as early as
	/// the scripts can run.
	/// </para>
	/// </summary>
	[InitializeOnLoad]
	public static class DisableSceneRecoveryPopup {
		private static readonly string _folder = Path.Combine(
			Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
			"Temp",
			"__Backupscenes"
		);

		static DisableSceneRecoveryPopup() {
			Clear();

			EditorApplication.update += OnFirstUpdate;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			EditorApplication.quitting += Clear;
		}

		private static void OnFirstUpdate() {
			EditorApplication.update -= OnFirstUpdate;
			Clear();
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange state) {
			// EnteredEditMode: the scene has been restored from the backup, the file is not needed anymore.
			if (state == PlayModeStateChange.EnteredEditMode)
				Clear();
		}

		private static void Clear() {
			try {
				if (!Directory.Exists(_folder))
					return;

				foreach (var file in Directory.GetFiles(_folder, "*.backup"))
					File.Delete(file);
			} catch (IOException) {
				// ignore: the file is locked by Unity, it will be deleted on the next run
			} catch (UnauthorizedAccessException) {
				// ignore: the file is locked by Unity, it will be deleted on the next run
			}
		}
	}
}

using UnityEditor;
using System.Reflection;
using UnityEditor.SceneManagement;

namespace Nox.Editor
{
    /// <summary>
    /// Disables the Unity "Scene backups from a previous Editor session" recovery popup
    /// by clearing backup scenes and setting the backup count to 0 on editor startup.
    /// </summary>
    [InitializeOnLoad]
    public static class DisableSceneRecoveryPopup
    {
        private static bool _hasRun;

        static DisableSceneRecoveryPopup()
        {
            // Run immediately — before the recovery dialog can appear
            TryDisable();

            // Fallback: also run on the first update tick in case
            // the backup check hasn't happened yet at init time
            EditorApplication.update += OnFirstUpdate;
        }

        private static void OnFirstUpdate()
        {
            EditorApplication.update -= OnFirstUpdate;
            TryDisable();
        }

        /// <summary>
        /// Attempt every known method to disable the scene backup / recovery popup.
        /// All failures are silently ignored — the feature may not exist in every Unity version.
        /// </summary>
        private static void TryDisable()
        {
            if (_hasRun) return;
            _hasRun = true;

            // 1 — EditorPrefs: tell Unity not to create backups (Preferences > Scene View > Number of Backup Scenes)
            TrySetEditorPref("SceneBackupManager.BackupCount", 0);
            TrySetEditorPref("SceneBackupManager.backupSceneCount", 0);
            TrySetEditorPref("kSceneBackupEnabled", 0);

            // 2 — Internal SceneBackupManager API (Unity 2021.2+)
            var mgrType = typeof(EditorSceneManager).Assembly
                .GetType("UnityEditor.SceneManagement.SceneBackupManager");
            if (mgrType == null) return;

            // Set backupSceneCount property
            TrySetStaticProp(mgrType, "backupSceneCount", 0);

            // Clear any leftover backup scenes on disk
            TryInvokeStatic(mgrType, "ClearBackupScenes");

            // Also try the enable/disable toggle if it exists
            TryInvokeStatic(mgrType, "SetBackupEnabled", false);
        }

        // ---- helpers ----

        private static void TrySetEditorPref(string key, int value)
        {
            try { EditorPrefs.SetInt(key, value); }
            catch { /* ignore */ }
        }

        private static void TrySetStaticProp(System.Type type, string name, int value)
        {
            var prop = type.GetProperty(name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null || !prop.CanWrite) return;
            try { prop.SetValue(null, value); }
            catch { /* ignore */ }
        }

        private static void TryInvokeStatic(System.Type type, string name, params object[] args)
        {
            var method = type.GetMethod(name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) return;
            try { method.Invoke(null, args); }
            catch { /* ignore */ }
        }
    }
}

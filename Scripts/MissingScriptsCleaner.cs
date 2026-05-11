#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Nox.Editor {
public class MissingScriptsCleaner : EditorWindow
{
    [MenuItem("Nox/Tools/Clean Missing Scripts in Scene")]
    private static void CleanMissingScriptsInScene()
    {
        int totalCount = 0;

        // Récupérer tous les root GameObjects de la scène active
        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

        foreach (var go in roots)
        {
            totalCount += CleanGameObject(go);
        }

        Debug.Log($"✅ Nettoyage terminé : {totalCount} script(s) manquant(s) supprimé(s).");
    }

    private static int CleanGameObject(GameObject go)
    {
        int count = 0;

        // Supprime les scripts manquants de ce GameObject
        count += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);

        // Parcourt tous les enfants
        foreach (Transform child in go.transform)
        {
            count += CleanGameObject(child.gameObject);
        }

        return count;
    }
}
}
#endif
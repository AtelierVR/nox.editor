
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace Nox.Editor {
public class FBXAnimationExporter : EditorWindow
{
    private GameObject selectedFBX;
    private string exportPath = "Assets/ExportedAnimations/";
    private bool createSeparateFiles = true;
    private bool includeRootMotion = true;
    private Vector2 scrollPosition;
    private List<AnimationClip> animationClips = new List<AnimationClip>();

    [MenuItem("Nox/Tools/FBX Animation Exporter")]
    public static void ShowWindow()
    {
        GetWindow<FBXAnimationExporter>("FBX Animation Exporter");
    }

    private void OnGUI()
    {
        GUILayout.Label("Exportateur d'Animations FBX", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Sélection du FBX
        EditorGUILayout.LabelField("Fichier FBX source:", EditorStyles.label);
        selectedFBX = (GameObject)EditorGUILayout.ObjectField(selectedFBX, typeof(GameObject), false);

        EditorGUILayout.Space();

        // Chemin d'export
        EditorGUILayout.LabelField("Dossier d'export:", EditorStyles.label);
        EditorGUILayout.BeginHorizontal();
        exportPath = EditorGUILayout.TextField(exportPath);
        if (GUILayout.Button("Parcourir", GUILayout.Width(80)))
        {
            string newPath = EditorUtility.OpenFolderPanel("Sélectionner le dossier d'export", "Assets", "");
            if (!string.IsNullOrEmpty(newPath))
            {
                exportPath = "Assets" + newPath.Substring(Application.dataPath.Length) + "/";
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Options
        EditorGUILayout.LabelField("Options d'export:", EditorStyles.label);
        createSeparateFiles = EditorGUILayout.Toggle("Créer des fichiers séparés", createSeparateFiles);
        includeRootMotion = EditorGUILayout.Toggle("Inclure Root Motion", includeRootMotion);

        EditorGUILayout.Space();

        // Affichage des clips trouvés
        if (selectedFBX != null)
        {
            UpdateAnimationClipsList();
            
            if (animationClips.Count > 0)
            {
                EditorGUILayout.LabelField($"Clips d'animation trouvés ({animationClips.Count}):", EditorStyles.label);
                
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(150));
                foreach (var clip in animationClips)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(clip.name);
                    EditorGUILayout.LabelField($"{clip.length:F2}s", GUILayout.Width(50));
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.HelpBox("Aucun clip d'animation trouvé dans ce FBX.", MessageType.Info);
            }
        }

        EditorGUILayout.Space();

        // Boutons d'action
        EditorGUI.BeginDisabledGroup(selectedFBX == null || animationClips.Count == 0);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Exporter toutes les animations"))
        {
            ExportAllAnimations();
        }
        
        if (GUILayout.Button("Exporter la sélection"))
        {
            ExportSelectedAnimations();
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUI.EndDisabledGroup();

        if (selectedFBX == null)
        {
            EditorGUILayout.HelpBox("Veuillez sélectionner un fichier FBX contenant des animations.", MessageType.Warning);
        }
    }

    private void UpdateAnimationClipsList()
    {
        animationClips.Clear();
        
        if (selectedFBX == null) return;

        string assetPath = AssetDatabase.GetAssetPath(selectedFBX);
        Object[] objects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        
        foreach (Object obj in objects)
        {
            if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                animationClips.Add(clip);
            }
        }
    }

    private void ExportAllAnimations()
    {
        if (!Directory.Exists(exportPath))
        {
            Directory.CreateDirectory(exportPath);
        }

        int exportedCount = 0;
        
        try
        {
            EditorUtility.DisplayProgressBar("Export des animations", "Préparation...", 0f);
            
            for (int i = 0; i < animationClips.Count; i++)
            {
                AnimationClip sourceClip = animationClips[i];
                float progress = (float)i / animationClips.Count;
                
                EditorUtility.DisplayProgressBar("Export des animations", 
                    $"Export de {sourceClip.name}...", progress);
                
                if (ExportAnimationClip(sourceClip))
                {
                    exportedCount++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.Refresh();
        
        string message = $"Export terminé ! {exportedCount}/{animationClips.Count} animations exportées.";
        EditorUtility.DisplayDialog("Export terminé", message, "OK");
        
        Debug.Log($"[FBX Animation Exporter] {message}");
    }

    private void ExportSelectedAnimations()
    {
        // Pour cette version simple, on exporte toutes les animations
        // Dans une version plus avancée, on pourrait ajouter une sélection par checkboxes
        ExportAllAnimations();
    }

    private bool ExportAnimationClip(AnimationClip sourceClip)
    {
        try
        {
            // Créer une copie du clip d'animation
            var newClip = Instantiate(sourceClip);
            newClip.name = sourceClip.name;

            // Appliquer les options
            if (!includeRootMotion)
            {
                // Supprimer les courbes de root motion si nécessaire
                var bindings = AnimationUtility.GetCurveBindings(newClip);
                foreach (var binding in bindings)
                {
                    if (binding.path == "" && (binding.propertyName.Contains("RootT") || binding.propertyName.Contains("RootQ")))
                    {
                        AnimationCurve emptyCurve = new AnimationCurve();
                        AnimationUtility.SetEditorCurve(newClip, binding, emptyCurve);
                    }
                }
            }

            // Générer le nom de fichier
            string fileName = createSeparateFiles ? 
                $"{sourceClip.name}.anim" : 
                $"{selectedFBX.name}_animations.anim";
                
            string fullPath = Path.Combine(exportPath, fileName);
            
            // Créer l'asset
            if (createSeparateFiles || !File.Exists(fullPath))
            {
                AssetDatabase.CreateAsset(newClip, fullPath);
            }
            else
            {
                // Ajouter à un asset existant (mode fichier unique)
                AssetDatabase.AddObjectToAsset(newClip, fullPath);
            }

            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[FBX Animation Exporter] Erreur lors de l'export de {sourceClip.name}: {e.Message}");
            return false;
        }
    }

    private void OnSelectionChange()
    {
        // Auto-sélection du FBX si un objet FBX est sélectionné dans le projet
        if (Selection.activeObject != null)
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            {
                selectedFBX = Selection.activeObject as GameObject;
                Repaint();
            }
        }
    }
}

[System.Serializable]
public class AnimationExportSettings
{
    public bool exportRootMotion = true;
    public bool createSeparateFiles = true;
    public bool optimizeKeyframes = true;
    public float compressionTolerance = 0.01f;
    public string customPrefix = "";
    public string customSuffix = "";
}
}
#endif

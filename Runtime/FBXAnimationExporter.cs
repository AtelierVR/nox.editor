
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Editor {
public class FBXAnimationExporter : EditorWindow
{
    private GameObject selectedFBX;
    private string exportPath = "Assets/ExportedAnimations/";
    private bool createSeparateFiles = true;
    private bool includeRootMotion = true;
    private Vector2 scrollPosition;
    private List<AnimationClip> animationClips = new List<AnimationClip>();
    private readonly HashSet<string> usedFileNames = new HashSet<string>();

    private static GUIStyle mutedLabel;

    /// <summary>Dimmed label used to preview a renamed (sanitized) export file.</summary>
    private static GUIStyle MutedLabel
    {
        get
        {
            if (mutedLabel == null)
            {
                mutedLabel = new GUIStyle(EditorStyles.miniLabel);
                mutedLabel.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.5f)
                    : new Color(0f, 0f, 0f, 0.5f);
                mutedLabel.padding.left = 14;
            }

            return mutedLabel;
        }
    }

    [MenuItem("Nox/Tools/FBX Animation Exporter")]
    public static void ShowWindow()
        => GetWindow<FBXAnimationExporter>("FBX Animation Exporter");

    private void OnGUI() {
        // FBX selection
        EditorGUILayout.LabelField("Source FBX:", EditorStyles.label);
        selectedFBX = (GameObject)EditorGUILayout.ObjectField(selectedFBX, typeof(GameObject), false);

        EditorGUILayout.Space();

        // Export path
        EditorGUILayout.LabelField("Export folder:", EditorStyles.label);
        EditorGUILayout.BeginHorizontal();
        exportPath = EditorGUILayout.TextField(exportPath);
        if (GUILayout.Button("Browse", GUILayout.Width(80)))
        {
            string newPath = EditorUtility.OpenFolderPanel("Select export folder", "Assets", "");
            if (!string.IsNullOrEmpty(newPath))
            {
                exportPath = "Assets" + newPath[Application.dataPath.Length..] + "/";
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Options
        EditorGUILayout.LabelField("Export options:", EditorStyles.label);
        createSeparateFiles = EditorGUILayout.Toggle("Create separate files", createSeparateFiles);
        includeRootMotion = EditorGUILayout.Toggle("Include Root Motion", includeRootMotion);

        EditorGUILayout.Space();

        // Found clips
        if (selectedFBX != null) {
            UpdateAnimationClipsList();
            
            if (animationClips.Count > 0) {
                EditorGUILayout.LabelField($"Animation clips found ({animationClips.Count}):", EditorStyles.label);
                
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(150));
                foreach (var clip in animationClips) {
                    string sanitized = SanitizeFileName(clip.name);

                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(clip.name);
                    EditorGUILayout.LabelField($"{clip.length:F2}s", GUILayout.Width(50));
                    EditorGUILayout.EndHorizontal();

                    // Preview the renamed (sanitized) export file, muted, when it differs.
                    if (sanitized != clip.name)
                        EditorGUILayout.LabelField($"\u2192 {sanitized}.anim", MutedLabel);

                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.HelpBox("No animation clip found in this FBX.", MessageType.Info);
            }
        }

        EditorGUILayout.Space();

        // Action buttons
        EditorGUI.BeginDisabledGroup(selectedFBX == null || animationClips.Count == 0);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Export all animations"))
            ExportAllAnimations();
        
        if (GUILayout.Button("Export selection"))
            ExportSelectedAnimations();
        EditorGUILayout.EndHorizontal();
        
        EditorGUI.EndDisabledGroup();

        if (selectedFBX == null)
            EditorGUILayout.HelpBox("Please select an FBX file containing animations.", MessageType.Warning);
    }

    private void UpdateAnimationClipsList()
    {
        animationClips.Clear();
        
        if (selectedFBX == null) return;

        string assetPath = AssetDatabase.GetAssetPath(selectedFBX);
        UnityEngine.Object[] objects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        
        foreach (UnityEngine.Object obj in objects)
            if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                animationClips.Add(clip);
    }

    private void ExportAllAnimations()
    {
        if (!Directory.Exists(exportPath))
            Directory.CreateDirectory(exportPath);

        usedFileNames.Clear();

        int exportedCount = 0;
        
        try
        {
            EditorUtility.DisplayProgressBar("Exporting animations", "Preparing...", 0f);
            
            for (int i = 0; i < animationClips.Count; i++)
            {
                AnimationClip sourceClip = animationClips[i];
                float progress = (float)i / animationClips.Count;
                
                EditorUtility.DisplayProgressBar("Exporting animations", 
                    $"Exporting {sourceClip.name}...", progress);
                
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
        
        string message = $"Export complete! {exportedCount}/{animationClips.Count} animations exported.";
        EditorUtility.DisplayDialog("Export complete", message, "OK");
        
        Logger.Log(message, this, tag: nameof(FBXAnimationExporter));
    }

    private void ExportSelectedAnimations()
    {
        // Simple version: export every animation.
        // A future version could add per-clip checkboxes.
        ExportAllAnimations();
    }

    private bool ExportAnimationClip(AnimationClip sourceClip) {
        try {
            // Duplicate the animation clip
            var newClip = Instantiate(sourceClip);

            // Apply options
            if (!includeRootMotion) {
                // Strip root motion curves when requested
                var bindings = AnimationUtility.GetCurveBindings(newClip);
                foreach (var binding in bindings)
                    if (binding.path == "" && (binding.propertyName.Contains("RootT") || binding.propertyName.Contains("RootQ"))) {
                        AnimationCurve emptyCurve = new();
                        AnimationUtility.SetEditorCurve(newClip, binding, emptyCurve);
                    }
            }

            // Build a valid file name: FBX clips often contain characters that are illegal
            // in file paths (e.g. "Armature|Idle_Loop" from the Quaternius FBX).
            string assetName = createSeparateFiles
                ? MakeUniqueFileName(SanitizeFileName(sourceClip.name))
                : SanitizeFileName(selectedFBX.name) + "_animations";

            newClip.name = assetName;

            string fileName = $"{assetName}.anim";
            string fullPath = Path.Combine(exportPath, fileName);
            
            // Create the asset
            if (createSeparateFiles || !File.Exists(fullPath))
                AssetDatabase.CreateAsset(newClip, fullPath);
            else AssetDatabase.AddObjectToAsset(newClip, fullPath);

            return true;
        } catch (Exception e) {
            Logger.LogError($"Failed to export {sourceClip.name}: {e.Message}", this, tag: nameof(FBXAnimationExporter));
            return false;
        }
    }

    /// <summary>
    /// Replaces the characters that are illegal in a file name (e.g. the '|' found in
    /// FBX clip names like "Armature|Idle_Loop") with '_'.
    /// </summary>
    private static string SanitizeFileName(string name) {
        if (string.IsNullOrEmpty(name))
            return "Unnamed";

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (char c in name)
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

        // On Windows a name cannot end with a dot or a space.
        string sanitized = builder.ToString().Trim().TrimEnd('.');
        return string.IsNullOrEmpty(sanitized) ? "Unnamed" : sanitized;
    }

    /// <summary>Avoids collisions when two clips resolve to the same file name.</summary>
    private string MakeUniqueFileName(string name)  {
        if (usedFileNames.Add(name))
            return name;

        int i = 1;
        string candidate;
        do { candidate = $"{name}_{i++}"; } 
        while (!usedFileNames.Add(candidate));

        return candidate;
    }

    private void OnSelectionChange() {
        // Auto-select the FBX when an FBX object is selected in the project
        if (Selection.activeObject != null) {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) {
                selectedFBX = Selection.activeObject as GameObject;
                Repaint();
            }
        }
    }
}

[Serializable]
public class AnimationExportSettings {
    public bool exportRootMotion = true;
    public bool createSeparateFiles = true;
    public bool optimizeKeyframes = true;
    public float compressionTolerance = 0.01f;
    public string customPrefix = "";
    public string customSuffix = "";
}
}
#endif

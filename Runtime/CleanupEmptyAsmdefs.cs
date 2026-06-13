using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nox.CCK.Attributes;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Nox.Editor
{
    /// <summary>
    /// Scans for .asmdef files with no .cs scripts in their scope and disables them
    /// by setting "autoReferenced": false. Unity 6000.4+ treats "will not be compiled"
    /// warnings as errors via EmitExceptionAsError, which blocks the player build.
    ///
    /// Handles nested asmdefs correctly: a parent is only considered empty
    /// if it has no .cs files outside of nested asmdef subtrees.
    /// </summary>
    public class CleanupEmptyAsmdefsWindow : EditorWindow
    {
        private List<Object> _emptyAsmdefs = new();
        private Vector2 _scroll;

        [MenuItem("Nox/Tools/Cleanup Empty Asmdefs")]
        public static void ShowWindow()
        {
            var window = GetWindow<CleanupEmptyAsmdefsWindow>("Empty Asmdefs");
            window.Refresh();
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Empty .asmdef files (no .cs scripts in scope):", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            if (_emptyAsmdefs.Count == 0)
            {
                EditorGUILayout.HelpBox("No empty asmdefs found.", MessageType.Info);
                if (GUILayout.Button("Refresh", GUILayout.Height(30)))
                    Refresh();
                return;
            }

            EditorGUILayout.HelpBox(
                $"Found {_emptyAsmdefs.Count} empty asmdef(s). " +
                "These cause Unity 6000.4+ to fail builds via EmitExceptionAsError. " +
                "Clicking 'Disable' sets autoReferenced to false so Unity skips them.",
                MessageType.Warning);

            EditorGUILayout.Space(4);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var asmdef in _emptyAsmdefs)
            {
                EditorGUILayout.ObjectField(asmdef, typeof(Object), false);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Height(30)))
                Refresh();

            GUI.backgroundColor = new Color(1f, 0.6f, 0f);
            if (GUILayout.Button($"Disable ({_emptyAsmdefs.Count})", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog(
                    "Disable Empty Asmdefs",
                    $"Set autoReferenced=false on {_emptyAsmdefs.Count} empty asmdef(s)?\n\n" +
                    "Unity will skip them during compilation. " +
                    "The files remain on disk and can be re-enabled manually.",
                    "Disable", "Cancel"))
                {
                    Disable();
                    Refresh();
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private void Refresh()
        {
            _emptyAsmdefs = FindEmptyAsmdefs()
                .Select(LoadObject)
                .Where(o => o != null)
                .ToList();
        }

        private void Disable()
        {
            foreach (var asmdef in _emptyAsmdefs)
            {
                var path = AssetDatabase.GetAssetPath(asmdef);
                if (string.IsNullOrEmpty(path)) continue;
                SetAutoReferenced(path, false);
                Debug.Log($"[CleanupEmptyAsmdefs] Disabled: {path}");
            }
            AssetDatabase.Refresh();
        }

        private static Object LoadObject(string path)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var relative = path.StartsWith(projectRoot!)
                ? path.Substring(projectRoot.Length + 1).Replace('\\', '/')
                : path.Replace('\\', '/');

            return AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(relative)
                ?? (Object)AssetDatabase.LoadAssetAtPath<DefaultAsset>(relative);
        }

        /// <summary>
        /// Sets "autoReferenced" in an asmdef JSON file.
        /// </summary>
        public static void SetAutoReferenced(string asmdefPath, bool value)
        {
            var fullPath = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(Application.dataPath)!, asmdefPath));
            if (!File.Exists(fullPath)) return;

            var json = File.ReadAllText(fullPath);
            var pattern = @"""autoReferenced""\s*:\s*(true|false)";
            var replacement = $"\"autoReferenced\": {(value ? "true" : "false")}";

            if (Regex.IsMatch(json, pattern))
                json = Regex.Replace(json, pattern, replacement);
            else
                json = json.TrimEnd('}', ' ', '\r', '\n') + $",\n    \"autoReferenced\": {(value ? "true" : "false")}\n}}";

            File.WriteAllText(fullPath, json);
        }

        /// <summary>
        /// Returns all .asmdef paths that have no .cs scripts in their scope.
        /// Handles nested asmdefs: .cs files in nested asmdef subtrees don't count for the parent.
        /// </summary>
        public static List<string> FindEmptyAsmdefs()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var dirs = new[]
            {
                Path.GetFullPath(Path.Combine(projectRoot!, "Library", "PackageCache")),
                Path.GetFullPath(Path.Combine(projectRoot!, "Packages"))
            };

            var allAsmdefs = new List<string>();
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                allAsmdefs.AddRange(Directory.GetFiles(dir, "*.asmdef", SearchOption.AllDirectories));
            }

            if (allAsmdefs.Count == 0) return new List<string>();

            var asmdefDirs = allAsmdefs.Select(Path.GetDirectoryName).Distinct().ToHashSet();
            var nested = new Dictionary<string, List<string>>();
            foreach (var ad in asmdefDirs)
            {
                nested[ad] = asmdefDirs
                    .Where(other => other != ad && other.StartsWith(ad + Path.DirectorySeparatorChar))
                    .ToList();
            }

            var empty = new List<string>();
            foreach (var asmdef in allAsmdefs)
            {
                var asmdefDir = Path.GetDirectoryName(asmdef);
                if (!Directory.Exists(asmdefDir)) continue;

                var allCs = Directory.GetFiles(asmdefDir, "*.cs", SearchOption.AllDirectories);

                var ownedCs = allCs.Where(cs =>
                {
                    var csDir = Path.GetDirectoryName(cs);
                    return !nested[asmdefDir].Any(n =>
                        csDir.StartsWith(n + Path.DirectorySeparatorChar) || csDir == n);
                }).ToArray();

                if (ownedCs.Length == 0)
                    empty.Add(asmdef);
            }

            return empty;
        }

        // ═══════════════════════════════════════════════════════════════
        // Build step – called automatically by the game builder via reflection.
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Disables empty asmdefs by setting autoReferenced=false.
        /// Returns true if any were disabled, false if nothing to do.
        /// </summary>
        [NoxInvokable("build:any")]
        public static bool CleanupForBuild()
        {
            var empty = FindEmptyAsmdefs();
            if (empty.Count == 0) return false;

            foreach (var asmdef in empty)
                SetAutoReferenced(asmdef, false);

            AssetDatabase.Refresh();
            Debug.Log($"[CleanupEmptyAsmdefs] Build step: disabled {empty.Count} empty asmdef(s).");
            return true;
        }
    }
}

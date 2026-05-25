#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nox.Editor {
	public static class ResourcesCompatibilityChecker {
		
		[MenuItem("Nox/Tools/Check Resources Compatibility")]
		public static void CheckResourcesCompatibility() {
			Debug.Log("=== Vérification de compatibilité des fichiers Resources ===");
			
			List<string> issues = new List<string>();
			List<string> resourcesPaths = FindAllResourcesFolders();
			
			foreach (string resourcesPath in resourcesPaths) {
				CheckResourcesFolder(resourcesPath, issues);
			}
			
			if (issues.Count > 0) {
				Debug.LogError($"Problèmes détectés dans les dossiers Resources ({issues.Count} problèmes):");
				foreach (string issue in issues) {
					Debug.LogError($"- {issue}");
				}
			} else {
				Debug.Log("Aucun problème détecté dans les dossiers Resources");
			}
		}
		
		[MenuItem("Nox/Tools/Fix Resources Compatibility Issues")]
		public static void FixResourcesCompatibilityIssues() {
			Debug.Log("=== Correction des problèmes de compatibilité Resources ===");
			
			List<string> resourcesPaths = FindAllResourcesFolders();
			int fixedCount = 0;
			
			foreach (string resourcesPath in resourcesPaths) {
				fixedCount += FixResourcesFolder(resourcesPath);
			}
			
			if (fixedCount > 0) {
				Debug.Log($"Correction terminée: {fixedCount} problèmes corrigés");
				AssetDatabase.Refresh();
			} else {
				Debug.Log("Aucun problème à corriger trouvé");
			}
		}
		
		private static List<string> FindAllResourcesFolders() {
			List<string> resourcesFolders = new List<string>();
			
			// Rechercher tous les dossiers Resources dans le projet
			string[] allFolders = Directory.GetDirectories(Application.dataPath, "Resources", SearchOption.AllDirectories);
			foreach (string folder in allFolders) {
				resourcesFolders.Add(folder.Replace(Application.dataPath, "Assets"));
			}
			
			// Rechercher dans les packages
			string packagesPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages");
			if (Directory.Exists(packagesPath)) {
				string[] packageResourcesFolders = Directory.GetDirectories(packagesPath, "Resources", SearchOption.AllDirectories);
				foreach (string folder in packageResourcesFolders) {
					string relativePath = folder.Replace(Directory.GetParent(Application.dataPath).FullName + Path.DirectorySeparatorChar, "");
					resourcesFolders.Add(relativePath.Replace(Path.DirectorySeparatorChar, '/'));
				}
			}
			
			return resourcesFolders;
		}
		
		private static void CheckResourcesFolder(string resourcesPath, List<string> issues) {
			Debug.Log($"Vérification du dossier: {resourcesPath}");
			
			if (!Directory.Exists(resourcesPath)) {
				issues.Add($"Dossier Resources introuvable: {resourcesPath}");
				return;
			}
			
			string[] files = Directory.GetFiles(resourcesPath, "*", SearchOption.AllDirectories);
			
			foreach (string filePath in files) {
				// Convertir le chemin vers un format Unity asset path
				string relativePath = ConvertToUnityAssetPath(filePath);
				
				// Ignorer les fichiers .meta
				if (filePath.EndsWith(".meta")) continue;
				
				// Vérifier si le fichier .meta existe
				string metaPath = filePath + ".meta";
				if (!File.Exists(metaPath)) {
					issues.Add($"Fichier .meta manquant: {relativePath}");
					continue;
				}
				
				// Vérifier si le fichier .meta est vide ou corrompu
				string metaContent = File.ReadAllText(metaPath);
				if (string.IsNullOrWhiteSpace(metaContent) || !metaContent.Contains("fileFormatVersion")) {
					issues.Add($"Fichier .meta corrompu ou vide: {relativePath}.meta");
					continue;
				}
				
				// Charger l'asset et vérifier ses flags
				Object asset = AssetDatabase.LoadAssetAtPath<Object>(relativePath);
				if (asset != null) {
					if ((asset.hideFlags & HideFlags.DontSave) != 0) {
						issues.Add($"Asset avec HideFlags.DontSave (problématique pour le build): {relativePath}");
					}
					
					if ((asset.hideFlags & HideFlags.DontSaveInBuild) != 0) {
						issues.Add($"Asset avec HideFlags.DontSaveInBuild: {relativePath}");
					}
					
					if ((asset.hideFlags & HideFlags.DontSaveInEditor) != 0) {
						issues.Add($"Asset avec HideFlags.DontSaveInEditor: {relativePath}");
					}
				}
			}
		}
		
		private static string ConvertToUnityAssetPath(string fullPath) {
			// Normaliser les séparateurs
			fullPath = fullPath.Replace('\\', '/');
			
			// Si c'est dans Assets/
			if (fullPath.Contains("/Assets/")) {
				return "Assets" + fullPath.Substring(fullPath.IndexOf("/Assets/") + 7);
			}
			
			// Si c'est dans Packages/
			if (fullPath.Contains("/Packages/")) {
				return "Packages" + fullPath.Substring(fullPath.IndexOf("/Packages/") + 9);
			}
			
			// Fallback
			return fullPath;
		}
		
		private static int FixResourcesFolder(string resourcesPath) {
			int fixedCount = 0;
			
			if (!Directory.Exists(resourcesPath)) {
				return 0;
			}
			
			string[] files = Directory.GetFiles(resourcesPath, "*", SearchOption.AllDirectories);
			
			foreach (string filePath in files) {
				string relativePath = ConvertToUnityAssetPath(filePath);
				
				// Ignorer les fichiers .meta
				if (filePath.EndsWith(".meta")) continue;
				
				// Vérifier si le fichier .meta existe
				string metaPath = filePath + ".meta";
				if (!File.Exists(metaPath)) {
					Debug.Log($"Régénération du fichier .meta pour: {relativePath}");
					AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
					fixedCount++;
					continue;
				}
				
				// Vérifier si le fichier .meta est vide ou corrompu
				string metaContent = File.ReadAllText(metaPath);
				if (string.IsNullOrWhiteSpace(metaContent) || !metaContent.Contains("fileFormatVersion")) {
					Debug.Log($"Régénération du fichier .meta corrompu pour: {relativePath}");
					File.Delete(metaPath);
					AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
					fixedCount++;
					continue;
				}
				
				// Charger l'asset et corriger ses flags
				Object asset = AssetDatabase.LoadAssetAtPath<Object>(relativePath);
				if (asset != null) {
					bool needsSave = false;
					HideFlags originalFlags = asset.hideFlags;
					
					if ((asset.hideFlags & HideFlags.DontSave) != 0) {
						Debug.Log($"Correction des HideFlags.DontSave pour: {relativePath}");
						asset.hideFlags &= ~HideFlags.DontSave;
						needsSave = true;
					}
					
					if ((asset.hideFlags & HideFlags.DontSaveInBuild) != 0) {
						Debug.Log($"Correction des HideFlags.DontSaveInBuild pour: {relativePath}");
						asset.hideFlags &= ~HideFlags.DontSaveInBuild;
						needsSave = true;
					}
					
					if ((asset.hideFlags & HideFlags.DontSaveInEditor) != 0) {
						Debug.Log($"Correction des HideFlags.DontSaveInEditor pour: {relativePath}");
						asset.hideFlags &= ~HideFlags.DontSaveInEditor;
						needsSave = true;
					}
					
					if (needsSave) {
						Debug.Log($"HideFlags changés de {originalFlags} vers {asset.hideFlags} pour: {relativePath}");
						EditorUtility.SetDirty(asset);
						AssetDatabase.SaveAssets();
						fixedCount++;
					}
				}
			}
			
			return fixedCount;
		}
		
		[MenuItem("Nox/Tools/Clean Empty Meta Files")]
		public static void CleanEmptyMetaFiles() {
			Debug.Log("=== Nettoyage des fichiers .meta vides ou corrompus ===");
			
			string[] allMetaFiles = Directory.GetFiles(Application.dataPath, "*.meta", SearchOption.AllDirectories);
			int cleanedCount = 0;
			
			foreach (string metaFile in allMetaFiles) {
				string content = File.ReadAllText(metaFile);
				if (string.IsNullOrWhiteSpace(content) || !content.Contains("fileFormatVersion")) {
					string assetPath = metaFile.Substring(0, metaFile.Length - 5); // Remove .meta extension
					string relativeAssetPath = assetPath.Replace(Application.dataPath, "Assets").Replace('\\', '/');
					
					if (File.Exists(assetPath)) {
						Debug.Log($"Suppression du fichier .meta corrompu et re-import: {relativeAssetPath}");
						File.Delete(metaFile);
						AssetDatabase.ImportAsset(relativeAssetPath, ImportAssetOptions.ForceUpdate);
						cleanedCount++;
					} else {
						Debug.Log($"Suppression du fichier .meta orphelin: {metaFile}");
						File.Delete(metaFile);
						cleanedCount++;
					}
				}
			}
			
			// Rechercher aussi dans les packages
			string packagesPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages");
			if (Directory.Exists(packagesPath)) {
				string[] packageMetaFiles = Directory.GetFiles(packagesPath, "*.meta", SearchOption.AllDirectories);
				
				foreach (string metaFile in packageMetaFiles) {
					string content = File.ReadAllText(metaFile);
					if (string.IsNullOrWhiteSpace(content) || !content.Contains("fileFormatVersion")) {
						string assetPath = metaFile.Substring(0, metaFile.Length - 5);
						
						if (File.Exists(assetPath)) {
							Debug.Log($"Suppression du fichier .meta corrompu dans les packages: {metaFile}");
							File.Delete(metaFile);
							cleanedCount++;
						} else {
							Debug.Log($"Suppression du fichier .meta orphelin dans les packages: {metaFile}");
							File.Delete(metaFile);
							cleanedCount++;
						}
					}
				}
			}
			
			if (cleanedCount > 0) {
				Debug.Log($"Nettoyage terminé: {cleanedCount} fichiers .meta corrigés");
				AssetDatabase.Refresh();
			} else {
				Debug.Log("Aucun fichier .meta corrompu trouvé");
			}
		}
	}
}
#endif // UNITY_EDITOR

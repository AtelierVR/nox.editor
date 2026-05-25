#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace Nox.Editor {
	public class MissingComponentRemover : EditorWindow {
		[MenuItem("Nox/Tools/Remove Missing Components")]
		public static void ShowWindow() {
			GetWindow<MissingComponentRemover>("Missing Component Remover");
		}

		private Vector2          _scrollPosition;
		private bool             _includeChildren    = true;
		private bool             _showPreview        = true;
		private List<GameObject> _objectsWithMissing = new List<GameObject>();

		void OnGUI() {
			GUILayout.Label("Missing Component Remover", EditorStyles.boldLabel);

			EditorGUILayout.Space();

			_includeChildren = EditorGUILayout.Toggle("Inclure les enfants", _includeChildren);
			_showPreview     = EditorGUILayout.Toggle("Afficher l'aperçu", _showPreview);

			EditorGUILayout.Space();

			if (GUILayout.Button("Analyser la sélection")) {
				AnalyzeSelection();
			}

			if (GUILayout.Button("Retirer tous les composants manquants")) {
				RemoveMissingComponents();
			}

			EditorGUILayout.Space();

			if (_showPreview && _objectsWithMissing.Count > 0) {
				DrawPreview();
			}
		}

		void AnalyzeSelection() {
			_objectsWithMissing.Clear();

			GameObject[] selectedObjects = Selection.gameObjects;

			if (selectedObjects.Length == 0) {
				Debug.LogWarning("Aucun objet sélectionné");
				return;
			}

			foreach (GameObject obj in selectedObjects) {
				if (_includeChildren) {
					AnalyzeGameObjectRecursive(obj);
				} else {
					AnalyzeGameObject(obj);
				}
			}

			Debug.Log($"Analyse terminée: {_objectsWithMissing.Count} objets avec des composants manquants trouvés");
		}

		void AnalyzeGameObjectRecursive(GameObject obj) {
			AnalyzeGameObject(obj);

			foreach (Transform child in obj.transform) {
				AnalyzeGameObjectRecursive(child.gameObject);
			}
		}

		void AnalyzeGameObject(GameObject obj) {
			Component[] components           = obj.GetComponents<Component>();
			bool        hasMissingComponents = false;

			foreach (Component component in components) {
				if (component == null) {
					hasMissingComponents = true;
				}
			}

			if (hasMissingComponents && !_objectsWithMissing.Contains(obj)) {
				_objectsWithMissing.Add(obj);
			}
		}

		void RemoveMissingComponents() {
			if (_objectsWithMissing.Count == 0) {
				Debug.LogWarning("Aucun objet avec des composants manquants trouvé. Analysez d'abord la sélection.");
				return;
			}

			int removedCount = 0;

			// Enregistrer l'état pour l'undo
			Undo.RecordObjects(_objectsWithMissing.Cast<Object>().ToArray(), "Remove Missing Components");

			foreach (GameObject obj in _objectsWithMissing) {
				int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(obj);
				removedCount += removed;

				// Marquer l'objet comme modifié
				EditorUtility.SetDirty(obj);

				// Si c'est un prefab, marquer comme modifié
				if (PrefabUtility.IsPartOfPrefabInstance(obj)) {
					PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
				}
			}

			Debug.Log($"Suppression terminée: {removedCount} composants manquants retirés de {_objectsWithMissing.Count} objets");

			// Rafraîchir l'analyse
			AnalyzeSelection();
		}

		void DrawPreview() {
			GUILayout.Label($"Objets avec composants manquants ({_objectsWithMissing.Count}):", EditorStyles.boldLabel);

			_scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(200));

			foreach (GameObject obj in _objectsWithMissing) {
				EditorGUILayout.BeginHorizontal();

				EditorGUILayout.ObjectField(obj, typeof(GameObject), true);

				if (GUILayout.Button("Retirer", GUILayout.Width(60))) {
					RemoveMissingComponentsFromObject(obj);
				}

				EditorGUILayout.EndHorizontal();
			}

			EditorGUILayout.EndScrollView();
		}

		void RemoveMissingComponentsFromObject(GameObject obj) {
			Undo.RecordObject(obj, "Remove Missing Components");

			int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(obj);
			EditorUtility.SetDirty(obj);

			if (PrefabUtility.IsPartOfPrefabInstance(obj)) {
				PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
			}

			Debug.Log($"Retiré {removed} composants manquants de {obj.name}");

			// Rafraîchir l'analyse
			AnalyzeSelection();
		}

		// Méthode statique pour utilisation rapide
		[MenuItem("Nox/Tools/Quick Remove Missing Components")]
		public static void QuickRemoveMissingComponents() {
			GameObject[] selectedObjects = Selection.gameObjects;

			if (selectedObjects.Length == 0) {
				Debug.LogWarning("Aucun objet sélectionné");
				return;
			}

			int              totalRemoved    = 0;
			List<GameObject> modifiedObjects = new List<GameObject>();

			foreach (GameObject obj in selectedObjects) {
				if (HasMissingComponents(obj)) {
					modifiedObjects.Add(obj);
				}

				// Inclure les enfants
				foreach (Transform child in obj.GetComponentsInChildren<Transform>()) {
					if (HasMissingComponents(child.gameObject) && !modifiedObjects.Contains(child.gameObject)) {
						modifiedObjects.Add(child.gameObject);
					}
				}
			}

			if (modifiedObjects.Count > 0) {
				Undo.RecordObjects(modifiedObjects.Cast<Object>().ToArray(), "Quick Remove Missing Components");

				foreach (GameObject obj in modifiedObjects) {
					int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(obj);
					totalRemoved += removed;
					EditorUtility.SetDirty(obj);

					if (PrefabUtility.IsPartOfPrefabInstance(obj)) {
						PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
					}
				}

				Debug.Log($"Suppression rapide terminée: {totalRemoved} composants manquants retirés de {modifiedObjects.Count} objets");
			} else {
				Debug.Log("Aucun composant manquant trouvé dans la sélection");
			}
		}

		static bool HasMissingComponents(GameObject obj) {
			Component[] components = obj.GetComponents<Component>();
			return components.Any(component => component == null);
		}
	}
}
#endif // UNITY_EDITOR
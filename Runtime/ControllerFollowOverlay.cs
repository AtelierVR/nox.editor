#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;
using Nox.CCK.Entities;
using Nox.CCK.Utils;
using Nox.Controllers;
using Nox.Controllers.Runtime;
using Nox.CCK;

namespace Nox.Editor {
	[Overlay(typeof(SceneView), "nox-controller-follow", "Follow Controller")]
	public class ControllerFollowOverlay : Overlay {
		private const string ConfigKey = "editor.controller_follow.active";
		private const string BaseRotationConfigKey = "editor.controller_follow.base_rotation";

		private bool _active;

		/// <summary>Pivots along the up axis of the base part instead of the world up.</summary>
		private bool _useBaseRotation;

		/// <summary>
		/// Rotation of the base part seen by the previous call, so only its movement is applied to the view: a
		/// teleport (respawn, avatar change) must not spin the SceneView.
		/// </summary>
		private Quaternion _lastBaseRotation = Quaternion.identity;

		/// <summary>Whether <see cref="_lastBaseRotation"/> holds a real value.</summary>
		private bool _hasBaseRotation;

		private static bool SavedActive {
			get => Config.LoadEditor().Get(ConfigKey, false);
			set {
				var config = Config.LoadEditor();
				config.Set(ConfigKey, value);
				config.Save();
			}
		}

		private static bool SavedBaseRotation {
			get => Config.LoadEditor().Get(BaseRotationConfigKey, false);
			set {
				var config = Config.LoadEditor();
				config.Set(BaseRotationConfigKey, value);
				config.Save();
			}
		}

		public override void OnCreated() {
			base.OnCreated();
			_active = SavedActive;
			_useBaseRotation = SavedBaseRotation;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			if (_active)
				Subscribe();
		}

		public override VisualElement CreatePanelContent() {
			var root = new VisualElement();
			root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/nox.editor/Assets/nox.editor/panels.uss"));
			root.AddToClassList("p-4");

			var toggle = new Toggle("Follow Controller") { value = _active };
			toggle.RegisterValueChangedCallback(evt => SetFollowing(evt.newValue));
			root.Add(toggle);

			var baseRotationToggle = new Toggle("Use Base rotation") {
				value = _useBaseRotation,
				tooltip = "Follow the up axis of the base part instead of the world up."
			};
			baseRotationToggle.RegisterValueChangedCallback(evt => SetBaseRotation(evt.newValue));
			root.Add(baseRotationToggle);

			return root;
		}

		private void SetFollowing(bool follow) {
			_active = follow;
			SavedActive = follow;
			if (follow)
				Subscribe();
			else
				Unsubscribe();
		}

		private void SetBaseRotation(bool useBaseRotation) {
			_useBaseRotation = useBaseRotation;
			SavedBaseRotation = useBaseRotation;
			_hasBaseRotation = false;
			SceneView.RepaintAll();
		}

		private void Subscribe() {
			_hasBaseRotation       = false;
			SceneView.duringSceneGui -= OnSceneGUI;
			SceneView.duringSceneGui += OnSceneGUI;
		}

		private void Unsubscribe() {
			SceneView.duringSceneGui -= OnSceneGUI;
		}

		private void OnPlayModeStateChanged(PlayModeStateChange state) {
			// Auto-follow as soon as we enter play mode if the toggle is on.
			if (state == PlayModeStateChange.EnteredPlayMode && _active)
				Subscribe();
			else if (state == PlayModeStateChange.ExitingPlayMode)
				Unsubscribe();
		}

		public override void OnWillBeDestroyed() {
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			Unsubscribe();
			base.OnWillBeDestroyed();
		}

		private void OnSceneGUI(SceneView sceneView) {
			var controller = (Main.Instance as IControllerAPI)?.Current;
			if (controller == null) return;

			var collider = controller.GetCollider();

			// Base part position (floor level) and rotation: the up axis of that part is the one the pivot can
			// follow, instead of the world up.
			Vector3 basePos;
			var baseRotation = Quaternion.identity;
			if (controller.TryGetPart(EntityRig.Base.ToIndex(), out var basePart)) {
				basePos = basePart.GetPosition();
				baseRotation = basePart.GetRotation();
			} else if (collider) {
				basePos = collider.transform.position;
				baseRotation = collider.transform.rotation;
			} else
				return;

			// Eye height of the avatar: the pivot follows the eyes (where the camera is), not the top of
			// the collider. Falls back to the collider height when the controller exposes no avatar.
			float eyeHeight = 1.7f;
			var abilities = controller.GetAbilities();
			if (abilities.TryGetValue("eye_height", out var eyeObj) && eyeObj != null)
				eyeHeight = eyeObj.ToFloat();
			else if (abilities.TryGetValue("height", out var hObj) && hObj != null)
				eyeHeight = hObj.ToFloat();

			var up = _useBaseRotation
				? baseRotation * Vector3.up
				: Vector3.up;
			if (up.sqrMagnitude < 0.0001f)
				up = Vector3.up;

			// Following the base rotation also means following the angle: the view keeps its angle relative to
			// the base instead of staying fixed in world space, so turning the avatar does not send the camera
			// around it. Only the movement of the base since the previous call is applied.
			if (_useBaseRotation) {
				if (_hasBaseRotation)
					sceneView.rotation = baseRotation * Quaternion.Inverse(_lastBaseRotation) * sceneView.rotation;

				_lastBaseRotation = baseRotation;
				_hasBaseRotation  = true;
			} else
				_hasBaseRotation = false;

			// Keep the SceneView pivoting at the eyes of the avatar while preserving
			// the current orbit angle and zoom distance.
			sceneView.pivot = basePos + up * eyeHeight;

			// Draw a subtle white indicator ring at the base position.
			Handles.color = new Color(1f, 1f, 1f, 0.6f);
			Handles.DrawWireDisc(basePos, up, 0.4f);

			// Direction arrow scaled by speed / max speed.
			var rb = collider ? collider.attachedRigidbody : null;
			if (rb != null) {
				var horizontalVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
				var speed = horizontalVel.magnitude;
				if (speed > 0.05f) {
					float maxSpeed = abilities.TryGetValue("max_move_speed", out var msObj) ? msObj.ToFloat() : 2.3f;
					if (abilities.TryGetValue("sprinting", out var spObj) && (bool)spObj
						&& abilities.TryGetValue("sprint_multiplier", out var smObj))
						maxSpeed *= smObj.ToFloat();

					float speedRatio = Mathf.Clamp01(speed / maxSpeed);
					float shaftLen = Mathf.Lerp(0.3f, 1.0f, speedRatio);
					float alpha = Mathf.Lerp(0.5f, 1.0f, speedRatio);
					var dir = horizontalVel.normalized;
					var tip = basePos + dir * shaftLen;

					Handles.color = new Color(1f, 1f, 1f, alpha);
					Handles.DrawLine(basePos, tip, 2f);
					Handles.ConeHandleCap(0, tip, Quaternion.LookRotation(dir), 0.12f, EventType.Repaint);
				}
			}
		}
	}
}
#endif

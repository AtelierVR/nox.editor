#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;
using Nox.CCK.Players;
using Nox.CCK.Utils;
using Nox.Controllers;
using Nox.Controllers.Runtime;
using Nox.CCK;

namespace Nox.Editor
{
    [Overlay(typeof(SceneView), "nox-controller-follow", "Follow Controller")]
    public class ControllerFollowOverlay : Overlay
    {
        private const string ConfigKey = "editor.controller_follow.active";

        private bool _active;

        private static bool SavedActive
        {
            get => Config.LoadEditor().Get(ConfigKey, false);
            set
            {
                var config = Config.LoadEditor();
                config.Set(ConfigKey, value);
                config.Save();
            }
        }

        public override void OnCreated()
        {
            base.OnCreated();
            _active = SavedActive;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (_active)
                Subscribe();
        }

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.paddingLeft = 4;
            root.style.paddingRight = 4;
            root.style.paddingTop = 4;
            root.style.paddingBottom = 4;

            var toggle = new Toggle("Follow Controller") { value = _active };
            toggle.RegisterValueChangedCallback(evt => SetFollowing(evt.newValue));
            root.Add(toggle);

            return root;
        }

        private void SetFollowing(bool follow)
        {
            _active = follow;
            SavedActive = follow;
            if (follow)
                Subscribe();
            else
                Unsubscribe();
        }

        private void Subscribe()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void Unsubscribe()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Auto-follow as soon as we enter play mode if the toggle is on.
            if (state == PlayModeStateChange.EnteredPlayMode && _active)
                Subscribe();
            else if (state == PlayModeStateChange.ExitingPlayMode)
                Unsubscribe();
        }

        public override void OnWillBeDestroyed()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Unsubscribe();
            base.OnWillBeDestroyed();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            var controller = (Main.Instance as IControllerAPI)?.Current;
            if (controller == null) return;

            var collider = controller.GetCollider();

            // Base part position (floor level).
            Vector3 basePos;
            if (controller.TryGetPart(PlayerRig.Base.ToIndex(), out var basePart))
                basePos = basePart.GetPosition();
            else if (collider)
                basePos = collider.transform.position;
            else
                return;

            // Collider height to lift the pivot to roughly head level.
            float height = 1.7f;
            var abilities = controller.GetAbilities();
            if (abilities.TryGetValue("height", out var hObj) && hObj != null)
                height = hObj.ToFloat();

            // Keep the SceneView pivoting at the top of the collider while preserving
            // the current orbit angle and zoom distance.
            sceneView.pivot = basePos + Vector3.up * height;

            // Draw a subtle white indicator ring at the base position.
            Handles.color = new Color(1f, 1f, 1f, 0.6f);
            Handles.DrawWireDisc(basePos, Vector3.up, 0.4f);

            // Direction arrow scaled by speed / max speed.
            var rb = collider ? collider.attachedRigidbody : null;
            if (rb != null)
            {
                var horizontalVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
                var speed = horizontalVel.magnitude;
                if (speed > 0.05f)
                {
                    float maxSpeed = abilities.TryGetValue("max_move_speed", out var msObj) ? msObj.ToFloat() : 2.3f;
                    if (abilities.TryGetValue("sprinting", out var spObj) && (bool)spObj
                        && abilities.TryGetValue("sprint_multiplier", out var smObj))
                        maxSpeed *= smObj.ToFloat();

                    float speedRatio = Mathf.Clamp01(speed / maxSpeed);
                    float shaftLen   = Mathf.Lerp(0.3f, 1.0f, speedRatio);
                    float alpha      = Mathf.Lerp(0.5f, 1.0f, speedRatio);
                    var   dir        = horizontalVel.normalized;
                    var   tip        = basePos + dir * shaftLen;

                    Handles.color = new Color(1f, 1f, 1f, alpha);
                    Handles.DrawLine(basePos, tip, 2f);
                    Handles.ConeHandleCap(0, tip, Quaternion.LookRotation(dir), 0.12f, EventType.Repaint);
                }
            }
        }
    }
}
#endif

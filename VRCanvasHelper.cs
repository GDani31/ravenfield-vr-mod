using UnityEngine;
using UnityEngine.UI;

namespace RavenfieldVRMod
{
    /// <summary>
    /// Helper to convert a canvas to WorldSpace for VR visibility.
    /// Positions it in front of the camera, facing the player.
    /// </summary>
    public static class VRCanvasHelper
    {
        private static float lastDistance = 4.5f;

        public static void ConvertCanvasToWorldSpace(Canvas canvas, float distance = 4.5f)
        {
            if (canvas == null) return;
            lastDistance = distance;

            // Already WorldSpace — just reposition
            if (canvas.renderMode == RenderMode.WorldSpace)
            {
                RepositionCanvas(canvas, distance);
                return;
            }

            Camera cam = GetCamera();
            if (cam == null) return;

            UndoHUDViewport(canvas);

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;

            RepositionCanvas(canvas, distance);

            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        private static void RepositionCanvas(Canvas canvas, float distance)
        {
            Camera cam = GetCamera();
            if (cam == null) return;

            canvas.worldCamera = cam;

            RectTransform rect = canvas.GetComponent<RectTransform>();
            if (rect != null)
            {
                float scale = 0.002f;
                rect.localScale = new Vector3(scale, scale, scale);

                Vector3 forward = cam.transform.forward;
                forward.y = 0;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                forward.Normalize();

                Vector3 pos = cam.transform.position + forward * distance;
                pos.y = cam.transform.position.y;
                rect.position = pos;
                rect.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
        }

        /// <summary>
        /// Sets up a canvas for body-tracked WorldSpace rendering.
        /// The canvas follows the player body position and faces the body forward direction,
        /// but does NOT rotate with head movement.
        /// </summary>
        public static void ConvertCanvasToBodyTracked(Canvas canvas, float distance = 3f)
        {
            if (canvas == null) return;

            Camera cam = GetCamera();
            if (cam == null) return;

            // Undo any VR_HUD_Viewport damage from ConvertCanvasesForVR.
            // That system wraps children into a viewport for HUD centering,
            // which breaks menu content when the canvas is later used as WorldSpace.
            UndoHUDViewport(canvas);

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;

            RectTransform rect = canvas.GetComponent<RectTransform>();
            if (rect != null)
                rect.localScale = Vector3.one * 0.002f;

            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();

            var tracker = canvas.gameObject.GetComponent<VRBodyTrackedCanvas>();
            if (tracker == null)
                tracker = canvas.gameObject.AddComponent<VRBodyTrackedCanvas>();
            tracker.distance = distance;
            tracker.enabled = true;
        }

        /// <summary>
        /// If ConvertCanvasesForVR wrapped this canvas's children into a
        /// VR_HUD_Viewport, undo it by reparenting children back and
        /// destroying the viewport.
        /// </summary>
        public static void UndoHUDViewport(Canvas canvas)
        {
            if (canvas == null) return;
            Transform viewport = canvas.transform.Find("VR_HUD_Viewport");
            if (viewport == null) return;

            Plugin.Log.LogInfo($"VR: Undoing HUD viewport on '{canvas.name}'");

            // Reparent all viewport children back to the canvas
            var children = new System.Collections.Generic.List<Transform>();
            for (int i = viewport.childCount - 1; i >= 0; i--)
                children.Add(viewport.GetChild(i));
            foreach (var child in children)
                child.SetParent(canvas.transform, false);

            // Destroy the viewport
            Object.Destroy(viewport.gameObject);
        }

        public static void StopBodyTracking(Canvas canvas)
        {
            if (canvas == null) return;
            var tracker = canvas.gameObject.GetComponent<VRBodyTrackedCanvas>();
            if (tracker != null) tracker.enabled = false;
        }

        private static Camera GetCamera()
        {
            if (FpsActorController.instance != null)
            {
                Camera c = FpsActorController.instance.GetActiveCamera();
                if (c != null) return c;
            }
            if (Camera.main != null) return Camera.main;
            foreach (var cam in Camera.allCameras)
                if (cam.isActiveAndEnabled) return cam;
            return null;
        }
    }

    /// <summary>
    /// MonoBehaviour that keeps a WorldSpace canvas positioned in front of the player body.
    /// Follows the player's position and body yaw (not head rotation).
    /// </summary>
    public class VRBodyTrackedCanvas : MonoBehaviour
    {
        public float distance = 3f;

        private Canvas canvas;
        private RectTransform rect;

        void OnEnable()
        {
            canvas = GetComponent<Canvas>();
            rect = GetComponent<RectTransform>();
            UpdatePosition(); // Snap to body immediately
        }

        void LateUpdate()
        {
            if (!VRManager.IsVRActive || canvas == null || !canvas.enabled) return;
            UpdatePosition();
        }

        private void UpdatePosition()
        {
            Camera cam = FindCamera();
            if (cam == null) return;

            Vector3 playerPos = cam.transform.position;
            Vector3 bodyForward;

            if (FpsActorController.instance != null)
            {
                float bodyYaw = VRCameraManager.PlayerYaw;
                bodyForward = Quaternion.Euler(0, bodyYaw, 0) * Vector3.forward;
            }
            else
            {
                bodyForward = cam.transform.forward;
                bodyForward.y = 0;
                if (bodyForward.sqrMagnitude < 0.01f) bodyForward = Vector3.forward;
                bodyForward.Normalize();
            }

            Vector3 pos = playerPos + bodyForward * distance;
            pos.y = playerPos.y;

            if (rect != null)
            {
                rect.position = pos;
                rect.rotation = Quaternion.LookRotation(bodyForward, Vector3.up);
            }

            canvas.worldCamera = cam;
        }

        private Camera FindCamera()
        {
            if (FpsActorController.instance != null)
            {
                Camera c = FpsActorController.instance.GetActiveCamera();
                if (c != null && c.isActiveAndEnabled) return c;
            }
            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;
            foreach (var cam in Camera.allCameras)
                if (cam.isActiveAndEnabled) return cam;
            return null;
        }
    }
}

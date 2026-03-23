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
}

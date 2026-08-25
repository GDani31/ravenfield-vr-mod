using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RavenfieldVRMod
{
    /// <summary>
    /// Fixes for game UI that the VR canvas conversion breaks.
    ///
    /// TMP caches its canvas render mode on enable and isn't notified when
    /// renderMode changes on a live canvas, so its mesh goes degenerate and the
    /// text stops rendering (plain UI.Text is unaffected). And GraphicRaycaster
    /// only raycasts its OWN canvas, so nested canvases need their own.
    /// </summary>
    public static class VRUICompat
    {
        private static readonly List<Graphic> graphicBuffer = new List<Graphic>();

        // Frames between full-tree raycaster scans (~3x/sec at 90Hz)
        private const int DeepScanInterval = 30;

        /// <summary>
        /// Call after changing a canvas's renderMode.
        /// </summary>
        public static void RefreshAfterRenderModeChange(Canvas root, Camera cam)
        {
            if (root == null) return;

            EnsureRaycasters(root, cam);
            RefreshGraphics(root);
        }

        /// <summary>
        /// Adds a GraphicRaycaster to the root canvas and every nested canvas,
        /// without which the laser can't click anything inside them.
        /// </summary>
        public static void EnsureRaycasters(Canvas root, Camera cam)
        {
            if (root == null) return;

            var canvases = root.GetComponentsInChildren<Canvas>(true);
            foreach (var canvas in canvases)
            {
                if (canvas == null) continue;

                // Nested canvases ignore worldCamera for rendering, but
                // GraphicRaycaster.eventCamera reads it directly
                if (cam != null && canvas.worldCamera != cam)
                    canvas.worldCamera = cam;

                if (canvas.GetComponent<GraphicRaycaster>() == null)
                    canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        /// <summary>
        /// Per-frame variant. Covers the root every frame but only walks the
        /// full tree periodically, which catches canvases created at runtime
        /// such as an open TMP_Dropdown's list.
        /// </summary>
        public static void EnsureRaycastersThrottled(Canvas root, Camera cam)
        {
            if (root == null) return;

            if (root.GetComponent<GraphicRaycaster>() == null)
                root.gameObject.AddComponent<GraphicRaycaster>();
            if (cam != null && root.worldCamera != cam)
                root.worldCamera = cam;

            if (Time.frameCount % DeepScanInterval != 0) return;
            EnsureRaycasters(root, cam);
        }

        /// <summary>
        /// Forces every graphic under the canvas to rebuild.
        /// </summary>
        public static void RefreshGraphics(Canvas root)
        {
            if (root == null) return;

            graphicBuffer.Clear();
            root.GetComponentsInChildren(true, graphicBuffer);

            int tmpCount = 0;
            foreach (var graphic in graphicBuffer)
            {
                if (graphic == null) continue;

                var tmp = graphic as TMP_Text;
                if (tmp != null)
                {
                    RefreshTMP(tmp);
                    tmpCount++;
                }
                else
                {
                    graphic.SetAllDirty();
                }
            }

            graphicBuffer.Clear();

            if (tmpCount > 0)
                Plugin.Log.LogInfo($"VR: Rebuilt {tmpCount} TextMeshPro component(s) on '{root.name}'.");
        }

        /// <summary>
        /// Re-caches a TMP component's canvas and regenerates its mesh. The
        /// enable cycle is what drops the stale render mode — ForceMeshUpdate
        /// alone regenerates with the old value and the text stays invisible.
        /// </summary>
        private static void RefreshTMP(TMP_Text tmp)
        {
            if (tmp == null) return;

            try
            {
                // Don't cycle hidden labels back into view
                if (tmp.enabled)
                {
                    tmp.enabled = false;
                    tmp.enabled = true;
                }

                tmp.SetAllDirty();
                tmp.ForceMeshUpdate(true, true);
                tmp.UpdateMeshPadding();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"VR: TMP refresh failed on '{tmp.name}': {e.Message}");
            }
        }

        /// <summary>
        /// Sets a button label that may be UI.Text or TextMeshPro.
        /// </summary>
        public static void SetButtonLabel(GameObject buttonObject, string text)
        {
            if (buttonObject == null) return;

            var uiText = buttonObject.GetComponentInChildren<Text>(true);
            if (uiText != null)
            {
                uiText.text = text;
                return;
            }

            var tmp = buttonObject.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = text;
                tmp.ForceMeshUpdate(true, true);
            }
        }
    }
}

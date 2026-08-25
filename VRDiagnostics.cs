using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RavenfieldVRMod
{
    /// <summary>
    /// Dumps the live UI tree to the log (F10). Game updates reshuffle the menu
    /// hierarchy, and this shows which assumption stopped holding.
    /// </summary>
    public static class VRDiagnostics
    {
        public static void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10))
                DumpUI();
        }

        public static void DumpUI()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== VR UI DUMP ===");
            sb.AppendLine($"Scene ingame={GameCompat.IsIngame()}  VRActive={VRManager.IsVRActive}  " +
                          $"Screen={Screen.width}x{Screen.height}");

            // The laser pointer routes clicks through this
            var es = EventSystem.current;
            if (es == null)
            {
                sb.AppendLine("EventSystem.current = NULL  (laser pointer cannot click anything)");
            }
            else
            {
                sb.AppendLine($"EventSystem '{es.name}' enabled={es.enabled} " +
                              $"module={(es.currentInputModule != null ? es.currentInputModule.GetType().Name : "NONE")}");
            }

            var canvases = Object.FindObjectsOfType<Canvas>();
            sb.AppendLine($"Canvases: {canvases.Length}");

            foreach (var canvas in canvases)
            {
                if (canvas == null) continue;

                var rect = canvas.GetComponent<RectTransform>();
                var scaler = canvas.GetComponent<CanvasScaler>();
                bool raycaster = canvas.GetComponent<GraphicRaycaster>() != null;

                sb.AppendLine(
                    $"  [{canvas.name}] mode={canvas.renderMode} root={canvas.isRootCanvas} " +
                    $"enabled={canvas.enabled} active={canvas.gameObject.activeInHierarchy} " +
                    $"raycaster={raycaster} cam={(canvas.worldCamera != null ? canvas.worldCamera.name : "null")} " +
                    $"scaleFactor={canvas.scaleFactor:F3} sortOrder={canvas.sortingOrder} " +
                    $"parent={(canvas.transform.parent != null ? canvas.transform.parent.name : "ROOT")}");

                if (rect != null)
                    sb.AppendLine($"      rect size={rect.rect.size} scale={rect.localScale} pos={rect.position}");

                if (scaler != null)
                    sb.AppendLine($"      scaler enabled={scaler.enabled} mode={scaler.uiScaleMode} " +
                                  $"match={scaler.matchWidthOrHeight:F2} refRes={scaler.referenceResolution} " +
                                  $"dynamicPPU={scaler.dynamicPixelsPerUnit}");

                // A TMP entry with meshVerts=0 but non-empty text is the
                // invisible-text failure
                var uiTexts = canvas.GetComponentsInChildren<Text>(true);
                var tmpTexts = canvas.GetComponentsInChildren<TMP_Text>(true);
                if (uiTexts.Length > 0 || tmpTexts.Length > 0)
                    sb.AppendLine($"      text: UI.Text={uiTexts.Length} TMP={tmpTexts.Length}");

                int shown = 0;
                foreach (var tmp in tmpTexts)
                {
                    if (tmp == null || shown >= 8) break;
                    if (!tmp.gameObject.activeInHierarchy) continue;

                    int chars = -1;
                    int verts = -1;
                    try
                    {
                        if (tmp.textInfo != null) chars = tmp.textInfo.characterCount;
                        if (tmp.mesh != null) verts = tmp.mesh.vertexCount;
                    }
                    catch { }

                    string content = tmp.text ?? "";
                    if (content.Length > 24) content = content.Substring(0, 24) + "...";

                    sb.AppendLine($"        TMP '{tmp.name}' enabled={tmp.enabled} " +
                                  $"chars={chars} meshVerts={verts} fontSize={tmp.fontSize:F1} " +
                                  $"color={tmp.color} raycast={tmp.raycastTarget} text=\"{content}\"");
                    shown++;
                }
            }

            sb.AppendLine("=== END VR UI DUMP ===");
            Plugin.Log.LogInfo(sb.ToString());
        }
    }
}

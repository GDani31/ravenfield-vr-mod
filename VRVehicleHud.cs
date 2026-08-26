using System.Collections.Generic;
using UnityEngine;

namespace RavenfieldVRMod
{
    /// <summary>
    /// Vehicle seat HUDs (tank gunner reticle) as a collimated sight: every HUD canvas
    /// and HUD-layer renderer of the vehicle is head-locked far out (no parallax) and
    /// drawn by a depth-cleared overlay camera so the barrel never occludes it.
    /// </summary>
    public static class VRVehicleHud
    {
        private const float DISTANCE = 40f;
        private const float SCREEN_CANVAS_HALF_ANGLE = 28f; // angular half-width given to screen-space canvases
        private const float NEAR_RENDERER_RANGE = 2.5f;     // vehicle renderers this close to the eye are HUD-like
        private const int HUD_LAYER = Seat.LAYER_HUD;
        private const int HUD_LAYER_MASK = 1 << HUD_LAYER;

        private class Entry
        {
            public Transform t;
            public string name;
            public Vector3 dirLocal;      // camera-space direction
            public Quaternion rotLocal;   // camera-relative rotation
            public Vector3 localScale;
        }

        private static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        private static readonly HashSet<int> processedHuds = new HashSet<int>();
        private static readonly HashSet<int> scannedVehicles = new HashSet<int>();
        private static int frame;

        private static Camera hudCamera;
        private static Camera maskedCamera;   // seat camera we removed the HUD layer from
        private static int maskedOriginal;

        /// <summary>Per frame from VRCameraManager, before the generic canvas converter.</summary>
        public static void Update()
        {
            frame++;
            var controller = FpsActorController.instance;
            Seat seat = controller != null && controller.actor != null ? controller.actor.seat : null;
            Camera cam = controller != null ? controller.GetActiveCamera() : null;

            if (seat == null || cam == null)
            {
                ReleaseHudCamera();
                return;
            }

            GameObject hud = seat.hud;
            if (hud != null && hud.activeInHierarchy && processedHuds.Add(hud.GetInstanceID()))
                ProcessHud(hud, cam);

            if (seat.vehicle != null && scannedVehicles.Add(seat.vehicle.GetInstanceID()))
                ScanVehicle(seat.vehicle, cam);

            if (entries.Count > 0) EnsureHudCamera(cam);
            else ReleaseHudCamera();

            if (frame % 600 == 0) Diagnostics(cam);
        }

        /// <summary>Claims root canvases under a Vehicle from the generic converter.</summary>
        public static bool TryClaimCanvas(Canvas canvas, Camera cam)
        {
            if (canvas == null || cam == null) return false;
            if (canvas.GetComponentInParent<Vehicle>() == null) return false;
            if (!entries.ContainsKey(canvas.transform.GetInstanceID()))
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("VR Vehicle HUD: claiming vehicle canvas");
                AddCanvas(canvas, cam, sb);
                Plugin.Log.LogInfo(sb.ToString().TrimEnd());
            }
            VRCameraManager.ProtectCanvas(canvas);
            return true;
        }

        /// <summary>From VRCameraManager.OnBeforeRender, after the final camera pose.</summary>
        public static void ApplyPose(Camera cam)
        {
            if (entries.Count == 0 || cam == null) return;
            Vector3 camPos = cam.transform.position;
            Quaternion camRot = cam.transform.rotation;
            List<int> dead = null;
            foreach (var kv in entries)
            {
                var e = kv.Value;
                if (e.t == null) { (dead ??= new List<int>()).Add(kv.Key); continue; }
                if (!e.t.gameObject.activeInHierarchy) continue;
                e.t.position = camPos + camRot * e.dirLocal * DISTANCE;
                e.t.rotation = camRot * e.rotLocal;
                e.t.localScale = e.localScale;
            }
            if (dead != null) foreach (int id in dead) entries.Remove(id);
        }

        // Overlay camera: child of the seat camera, HUD layer only, drawn after the world
        private static void EnsureHudCamera(Camera main)
        {
            if (hudCamera == null)
            {
                hudCamera = new GameObject("VR Vehicle HUD Camera").AddComponent<Camera>();
                Plugin.Log.LogInfo("VR Vehicle HUD: created overlay camera");
            }
            if (hudCamera.transform.parent != main.transform)
            {
                hudCamera.transform.SetParent(main.transform, false);
                hudCamera.transform.localPosition = Vector3.zero;
                hudCamera.transform.localRotation = Quaternion.identity;
                hudCamera.transform.localScale = Vector3.one;
            }

            hudCamera.CopyFrom(main);
            hudCamera.clearFlags = CameraClearFlags.Depth;
            hudCamera.cullingMask = HUD_LAYER_MASK;
            hudCamera.depth = main.depth + 1f;
            hudCamera.nearClipPlane = 0.05f;
            hudCamera.farClipPlane = Mathf.Max(main.farClipPlane, DISTANCE * 4f);
            hudCamera.useOcclusionCulling = false;
            hudCamera.allowHDR = false;
            hudCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            hudCamera.targetTexture = null;
            hudCamera.enabled = true;
            hudCamera.ResetProjectionMatrix();

            if (maskedCamera != main)
            {
                RestoreMainMask();
                maskedCamera = main;
                maskedOriginal = main.cullingMask;
            }
            main.cullingMask &= ~HUD_LAYER_MASK;
        }

        private static void ReleaseHudCamera()
        {
            if (hudCamera != null) hudCamera.enabled = false;
            RestoreMainMask();
        }

        private static void RestoreMainMask()
        {
            if (maskedCamera == null) return;
            if ((maskedOriginal & HUD_LAYER_MASK) != 0) maskedCamera.cullingMask |= HUD_LAYER_MASK;
            maskedCamera = null;
        }

        private static void ProcessHud(GameObject hud, Camera cam)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine($"VR Vehicle HUD: processing seat HUD '{hud.name}' (layer {hud.layer}) for camera '{cam.name}' cullingMask={cam.cullingMask:X}");
            foreach (Canvas cv in hud.GetComponentsInChildren<Canvas>(true))
                if (cv.isRootCanvas) AddCanvas(cv, cam, log);
            foreach (Renderer r in hud.GetComponentsInChildren<Renderer>(true))
                if (r.GetComponentInParent<Canvas>() == null) AddRenderer(r, cam, log, 20f);
            Plugin.Log.LogInfo(log.ToString().TrimEnd());
        }

        // Once per vehicle: cameras, near renderers (3D sights), canvases outside Seat.hud
        private static void ScanVehicle(Vehicle vehicle, Camera cam)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine($"VR Vehicle HUD: scanning vehicle '{vehicle.name}' from camera '{cam.name}' (stereo={cam.stereoEnabled} fov={cam.fieldOfView:F0} near={cam.nearClipPlane})");

            foreach (Camera vc in vehicle.GetComponentsInChildren<Camera>(true))
            {
                bool hudOnly = (vc.cullingMask & ~HUD_LAYER_MASK) == 0 && (vc.cullingMask & HUD_LAYER_MASK) != 0;
                log.AppendLine($"  camera '{vc.name}' enabled={vc.enabled} mask={vc.cullingMask:X} ortho={vc.orthographic} depth={vc.depth} hudOnly={hudOnly}");
                if (vc != cam && hudOnly && vc.targetTexture == null && vc.enabled)
                {
                    vc.enabled = false;
                    log.AppendLine($"    → disabled HUD-only camera '{vc.name}'");
                }
            }

            Vector3 eye = cam.transform.position;
            foreach (Renderer r in vehicle.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponentInParent<Canvas>() != null) continue;
                if (r.GetComponentInParent<Weapon>() != null && r.GetComponentInParent<MountedWeapon>() == null) continue; // carried weapons
                float d = Vector3.Distance(eye, r.bounds.center);
                if (d > NEAR_RENDERER_RANGE) continue;
                bool hudLike = r.gameObject.layer == HUD_LAYER || LooksLikeReticle(r.name);
                log.AppendLine($"  near renderer '{r.name}' dist={d:F2} layer={r.gameObject.layer} hudLike={hudLike}");
                if (hudLike) AddRenderer(r, cam, log, NEAR_RENDERER_RANGE);
            }

            foreach (Canvas cv in vehicle.GetComponentsInChildren<Canvas>(true))
                if (cv.isRootCanvas && !entries.ContainsKey(cv.transform.GetInstanceID())) AddCanvas(cv, cam, log);

            log.Append($"  → {entries.Count} head-locked element(s) at {DISTANCE}m");
            Plugin.Log.LogInfo(log.ToString());
        }

        private static bool LooksLikeReticle(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("reticle") || n.Contains("crosshair") || n.Contains("hud");
        }

        private static void AddCanvas(Canvas cv, Camera cam, System.Text.StringBuilder log)
        {
            var rt = cv.GetComponent<RectTransform>();
            float d0 = Vector3.Distance(cam.transform.position, cv.transform.position);
            log.AppendLine($"  canvas '{cv.name}' mode={cv.renderMode} rect={rt.rect.size} localScale={cv.transform.localScale} " +
                           $"dist={d0:F2} parent={(cv.transform.parent != null ? cv.transform.parent.name : "none")} layer={cv.gameObject.layer}");
            if (rt.rect.width < 1f || rt.rect.height < 1f)
            {
                log.AppendLine("    → skipped (empty rect)");
                return;
            }

            float parentScale = cv.transform.localScale.x > 1e-6f ? cv.transform.lossyScale.x / cv.transform.localScale.x : 1f;
            if (parentScale <= 1e-6f) parentScale = 1f;

            Vector3 newScale;
            bool wasWorld = cv.renderMode == RenderMode.WorldSpace;
            if (wasWorld && d0 > 0.05f && d0 < 20f && cv.transform.localScale.x > 1e-6f)
            {
                newScale = cv.transform.localScale * (DISTANCE / d0); // keep angular size
            }
            else
            {
                if (!wasWorld)
                {
                    cv.renderMode = RenderMode.WorldSpace;
                    cv.worldCamera = cam;
                }
                float worldWidth = 2f * DISTANCE * Mathf.Tan(SCREEN_CANVAS_HALF_ANGLE * Mathf.Deg2Rad);
                newScale = Vector3.one * (worldWidth / Mathf.Max(rt.rect.width, 1f) / parentScale);
            }

            SetLayerRecursive(cv.transform, HUD_LAYER);
            VRCameraManager.ProtectCanvas(cv);
            entries[cv.transform.GetInstanceID()] = new Entry
            {
                t = cv.transform, name = cv.name,
                dirLocal = Vector3.forward, rotLocal = Quaternion.identity, localScale = newScale
            };
            log.AppendLine($"    → head-locked, scale {newScale.x:F5}");
        }

        private static void AddRenderer(Renderer r, Camera cam, System.Text.StringBuilder log, float maxDist)
        {
            Transform camT = cam.transform;
            Vector3 off = r.transform.position - camT.position;
            float d0 = off.magnitude;
            log.AppendLine($"  renderer '{r.name}' dist={d0:F2} layer={r.gameObject.layer}");
            if (d0 < 0.02f || d0 > maxDist) return;
            Quaternion invCam = Quaternion.Inverse(camT.rotation);
            SetLayerRecursive(r.transform, HUD_LAYER);
            entries[r.transform.GetInstanceID()] = new Entry
            {
                t = r.transform, name = r.name,
                dirLocal = invCam * (off / d0),
                rotLocal = invCam * r.transform.rotation,
                localScale = r.transform.localScale * (DISTANCE / d0)
            };
            log.AppendLine("    → head-locked");
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }

        private static void Diagnostics(Camera cam)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"VR Vehicle HUD: cam '{cam.name}' mask={cam.cullingMask:X} hudCam={(hudCamera != null && hudCamera.enabled ? "on" : "off")}; ");
            foreach (var e in entries.Values)
            {
                if (e.t == null) continue;
                sb.Append($"[{e.name} active={e.t.gameObject.activeInHierarchy} layer={e.t.gameObject.layer} " +
                          $"dist={Vector3.Distance(cam.transform.position, e.t.position):F1} lossy={e.t.lossyScale.x:F5}] ");
            }
            Plugin.Log.LogInfo(sb.ToString());
        }
    }
}

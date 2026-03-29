using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace RavenfieldVRMod
{
    /// <summary>
    /// Core VR camera management:
    /// 1. Applies HMD pose to camera (head tracking)
    /// 2. Decouples head rotation from character body
    /// 3. Menu canvases → WorldSpace (static in world)
    /// 4. In-game HUD canvases → ScreenSpaceCamera (attached to head)
    /// 5. Cleans up WorldSpace canvases when entering gameplay
    /// </summary>
    public static class VRCameraManager
    {
        private static bool vrActive;

        // HMD tracking
        private static List<InputDevice> hmdDevices = new List<InputDevice>();
        private static Vector3 cameraOriginalLocalPos;
        private static bool savedOriginalPos;

        // Player body yaw — exposed for movement direction calculation
        public static float PlayerYaw => playerYaw;
        private static float playerYaw;
        private static Transform lastCharacterTransform;

        // Canvas tracking
        private static HashSet<int> convertedCanvasIds = new HashSet<int>();
        // Protected canvases are NEVER processed by ConvertCanvasesForVR (survives scene transitions)
        private static HashSet<int> protectedCanvasIds = new HashSet<int>();
        private static bool lastWasIngame;

        // Turret/vehicle aim tracking — for world-space crosshair
        internal static bool GameOverrodeCamera;
        internal static Quaternion GameAimWorldRotation;
        private static GameObject turretCrosshair;
        private static bool parentTiltedOnEntry; // tank turret 90° pitch fix

        /// <summary>
        /// Mark a canvas as protected — ConvertCanvasesForVR will never touch it.
        /// Used by menu patches to prevent the HUD converter from damaging menu content.
        /// </summary>
        public static void ProtectCanvas(Canvas canvas)
        {
            if (canvas != null)
                protectedCanvasIds.Add(canvas.GetInstanceID());
        }

        // VR world scale — shrinks the player to match game character height
        public const float VR_WORLD_SCALE = 0.65f;
        private static Vector3 hmdOriginPos;

        // Startup rotation
        private static bool didStartupRotation;
        private static int startupFrame;

        public static void OnVREnabled()
        {
            vrActive = true;
            savedOriginalPos = false;
            hmdOriginPos = Vector3.zero;
            convertedCanvasIds.Clear();
            didStartupRotation = false;
            startupFrame = 0;
            Application.onBeforeRender -= OnBeforeRender;
            Application.onBeforeRender += OnBeforeRender;
            Plugin.Log.LogInfo("VR camera manager activated.");
        }

        public static void OnVRDisabled()
        {
            vrActive = false;
            Application.onBeforeRender -= OnBeforeRender;
            if (turretCrosshair != null)
            {
                Object.Destroy(turretCrosshair);
                turretCrosshair = null;
            }
            convertedCanvasIds.Clear();

            foreach (var cam in Camera.allCameras)
            {
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.ResetProjectionMatrix();
            }
            Plugin.Log.LogInfo("All cameras restored to flat mode.");
        }

        public static void UpdateEveryFrame()
        {
            if (!vrActive || !XRSettings.isDeviceActive)
                return;

            GameOverrodeCamera = false;
            startupFrame++;

            Camera activeCam = GetActiveCamera();
            if (activeCam != null)
            {
                EnsureStereo(activeCam);
                ApplyHMDTracking(activeCam);
                ManageFallbackCamera(activeCam);
            }

            // Startup: rotate 180° briefly so SteamVR calibrates, then rotate back
            if (!didStartupRotation && startupFrame == 30)
            {
                playerYaw += 180f;
                Plugin.Log.LogInfo("Startup rotation: +180°");
            }
            if (!didStartupRotation && startupFrame == 60)
            {
                playerYaw -= 180f;
                didStartupRotation = true;
                Plugin.Log.LogInfo("Startup rotation: back to 0°");
            }

            ConvertCanvasesForVR();
        }

        private static Camera lastTrackedCamera;

        private static void ApplyHMDTracking(Camera cam)
        {
            hmdDevices.Clear();
            InputDevices.GetDevicesAtXRNode(XRNode.Head, hmdDevices);

            foreach (var hmd in hmdDevices)
            {
                if (!hmd.isValid) continue;

                bool hasPos = hmd.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 hmdPos);
                bool hasRot = hmd.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion hmdRot);

                // Re-save origin when camera changes (e.g. entering/exiting vehicle)
                if (!savedOriginalPos || cam != lastTrackedCamera)
                {
                    cameraOriginalLocalPos = cam.transform.localPosition;
                    if (hasPos) hmdOriginPos = hmdPos;
                    if (!savedOriginalPos && cam.transform.parent != null)
                        playerYaw = cam.transform.parent.eulerAngles.y;
                    savedOriginalPos = true;

                    // Check parent tilt on entry only (not every frame).
                    // Fixes tank turret with 90° pitch parent without
                    // breaking barrel rolls / loops in aircraft.
                    parentTiltedOnEntry = cam.transform.parent != null
                        && Vector3.Dot(cam.transform.parent.up, Vector3.up) < 0.7f;

                    lastTrackedCamera = cam;
                }

                if (hasRot)
                    cam.transform.localRotation = hmdRot;
                if (hasPos)
                {
                    // Use relative HMD movement from origin, scaled to match game character height
                    Vector3 adjustedPos = cameraOriginalLocalPos + (hmdPos - hmdOriginPos) * VR_WORLD_SCALE;
                    cam.transform.localPosition = adjustedPos;
                }

                // Ensure camera scale is always 1 (fixes "bigger" after vehicle exit)
                cam.transform.localScale = Vector3.one;

                // FOV is fixed by headset optics in VR — cannot be changed via software

                if (FpsActorController.instance != null)
                {
                    Transform charT = FpsActorController.instance.transform;
                    if (charT != lastCharacterTransform)
                    {
                        playerYaw = charT.eulerAngles.y;
                        lastCharacterTransform = charT;
                    }
                    Vector3 euler = charT.eulerAngles;
                    euler.y = playerYaw;
                    charT.eulerAngles = euler;
                }

                break;
            }
        }

        /// <summary>
        /// Re-applies HMD pose in LateUpdate, after all game systems
        /// (turret cameras, vehicle cameras, etc.) have run their updates.
        /// Without this, turret/vehicle code overwrites the HMD rotation.
        /// </summary>
        public static void ReapplyHMDPose()
        {
            if (!vrActive || !XRSettings.isDeviceActive || !savedOriginalPos) return;
            Camera cam = GetActiveCamera();
            // Only reapply to the camera we tracked in Update — avoids applying
            // stale position data to a different camera (which caused world to vanish)
            if (cam == null || cam != lastTrackedCamera) return;

            // Only reapply ROTATION (not position) — position from Update() survives
            // to rendering fine. It's only rotation that turret/vehicle systems override.
            hmdDevices.Clear();
            InputDevices.GetDevicesAtXRNode(XRNode.Head, hmdDevices);
            foreach (var hmd in hmdDevices)
            {
                if (!hmd.isValid) continue;
                if (hmd.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion hmdRot))
                    cam.transform.localRotation = hmdRot;
                break;
            }
        }

        public static void RotatePlayer(float yawDelta)
        {
            playerYaw += yawDelta;
        }

        /// <summary>
        /// Application.onBeforeRender — fires once per frame after ALL LateUpdates,
        /// before any camera begins rendering. Forces HMD head tracking as the
        /// absolute last transform before rendering, so no game script (vehicle,
        /// turret, death cam) can override it. Both eyes see the same rotation
        /// (unlike Camera.onPreCull which fires per-eye in multi-pass stereo).
        ///
        /// Only forces ROTATION — position is handled by ApplyHMDTracking in Update.
        /// </summary>
        private static void OnBeforeRender()
        {
            if (!vrActive) return;
            Camera cam = GetActiveCamera();
            if (cam == null) return;

            hmdDevices.Clear();
            InputDevices.GetDevicesAtXRNode(XRNode.Head, hmdDevices);
            foreach (var hmd in hmdDevices)
            {
                if (!hmd.isValid) continue;
                if (hmd.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion hmdRot))
                {
                    // Detect late-running overrides not caught by Harmony patch
                    // (vehicle scripts whose LateUpdate runs after ours)
                    if (!GameOverrodeCamera)
                    {
                        float angleDiff = Quaternion.Angle(cam.transform.localRotation, hmdRot);
                        if (angleDiff > 1f)
                        {
                            GameAimWorldRotation = cam.transform.rotation;
                            GameOverrodeCamera = true;
                        }
                    }

                    // Use world rotation only for cameras with tilted parents
                    // on entry (e.g. tank turret with 90° pitch offset).
                    // Checked once on entry, not every frame — so barrel rolls
                    // and loops in aircraft don't trigger it.
                    if (parentTiltedOnEntry)
                        cam.transform.rotation = Quaternion.Euler(0, playerYaw, 0) * hmdRot;
                    else
                        cam.transform.localRotation = hmdRot;
                }
                break;
            }

            // Position turret/vehicle crosshair marker
            UpdateTurretCrosshair(cam);
        }

        private static void UpdateTurretCrosshair(Camera cam)
        {
            if (turretCrosshair == null)
            {
                turretCrosshair = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                turretCrosshair.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
                Object.Destroy(turretCrosshair.GetComponent<Collider>());
                var rend = turretCrosshair.GetComponent<Renderer>();
                rend.material = new Material(Shader.Find("Sprites/Default"));
                rend.material.color = new Color(0f, 1f, 0f, 0.9f);
                Object.DontDestroyOnLoad(turretCrosshair);
            }

            if (GameOverrodeCamera && cam != null)
            {
                Vector3 aimForward = GameAimWorldRotation * Vector3.forward;
                turretCrosshair.transform.position = cam.transform.position + aimForward * 50f;
                turretCrosshair.SetActive(true);
            }
            else
            {
                turretCrosshair.SetActive(false);
            }
        }

        /// <summary>
        /// Menu: WorldSpace (static). Gameplay HUD: ScreenSpaceCamera (head-locked).
        /// On transition to gameplay, reset ALL canvases back to ScreenSpaceOverlay first,
        /// then re-convert only the ones in the new scene.
        /// </summary>
        private static void ConvertCanvasesForVR()
        {
            Camera cam = GetActiveCamera();
            if (cam == null) return;

            bool inGameplay = GameManager.IsIngame();

            // Scene transition: reset everything so old WorldSpace canvases don't persist
            if (inGameplay != lastWasIngame)
            {
                // Revert any WorldSpace canvases back to Overlay (they'll be destroyed with the scene anyway,
                // but if they're DontDestroyOnLoad they need resetting)
                foreach (var canvas in Object.FindObjectsOfType<Canvas>())
                {
                    if (!canvas.isRootCanvas) continue;
                    if (convertedCanvasIds.Contains(canvas.GetInstanceID()))
                    {
                        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    }
                }
                convertedCanvasIds.Clear();
                lastWasIngame = inGameplay;
                Plugin.Log.LogInfo($"Scene transition: inGameplay={inGameplay}, reset canvases.");
            }

            foreach (var canvas in Object.FindObjectsOfType<Canvas>())
            {
                int id = canvas.GetInstanceID();

                if (convertedCanvasIds.Contains(id))
                    continue;
                if (protectedCanvasIds.Contains(id))
                    continue;
                if (!canvas.isRootCanvas)
                    continue;
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    // Also convert ScreenSpaceCamera canvases the game creates
                    // (kill feed, edge indicators, etc. may use ScreenSpaceCamera)
                    if (canvas.renderMode == RenderMode.ScreenSpaceCamera && inGameplay)
                    {
                        // Ensure it has the VR viewport treatment
                        if (canvas.transform.Find("VR_HUD_Viewport") == null)
                        {
                            canvas.worldCamera = cam;
                            canvas.planeDistance = 3f;
                            var viewportGO2 = new GameObject("VR_HUD_Viewport");
                            var viewport2 = viewportGO2.AddComponent<RectTransform>();
                            viewport2.SetParent(canvas.transform, false);
                            viewport2.anchorMin = new Vector2(0.25f, 0.25f);
                            viewport2.anchorMax = new Vector2(0.75f, 0.75f);
                            viewport2.offsetMin = Vector2.zero;
                            viewport2.offsetMax = Vector2.zero;
                            var children2 = new List<Transform>();
                            for (int i2 = canvas.transform.childCount - 1; i2 >= 0; i2--)
                            {
                                var ch = canvas.transform.GetChild(i2);
                                if (ch.gameObject != viewportGO2)
                                    children2.Add(ch);
                            }
                            foreach (var ch in children2)
                                ch.SetParent(viewport2, false);
                            convertedCanvasIds.Add(id);
                        }
                    }
                    // Ensure WorldSpace canvases have raycasters
                    if (canvas.renderMode == RenderMode.WorldSpace)
                    {
                        if (canvas.GetComponent<GraphicRaycaster>() == null)
                            canvas.gameObject.AddComponent<GraphicRaycaster>();
                    }
                    continue;
                }

                if (inGameplay)
                {
                    // Check if a menu is open — if so, make canvas WorldSpace (static)
                    bool menuOpen = LoadoutUi.IsOpen() || IngameMenuUi.IsOpen();

                    if (menuOpen)
                    {
                        // Menu canvases → WorldSpace (interactable with laser)
                        canvas.renderMode = RenderMode.WorldSpace;
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
                            Vector3 pos = cam.transform.position + forward * 2.5f;
                            pos.y = cam.transform.position.y;
                            rect.position = pos;
                            rect.rotation = Quaternion.LookRotation(forward, Vector3.up);
                        }

                        if (canvas.GetComponent<GraphicRaycaster>() == null)
                            canvas.gameObject.AddComponent<GraphicRaycaster>();

                        // Add body tracking so sub-panels reposition when re-opened
                        var bodyTracker = canvas.gameObject.GetComponent<VRBodyTrackedCanvas>();
                        if (bodyTracker == null)
                            bodyTracker = canvas.gameObject.AddComponent<VRBodyTrackedCanvas>();
                        bodyTracker.distance = 3f;
                        bodyTracker.enabled = true;
                    }
                    else
                    {
                        // Skip menu canvases — they're handled by their own Show patches
                        // and must NOT get the HUD viewport treatment (which reparents
                        // their children and breaks them when reopened)
                        string cname = canvas.name;
                        if (cname.Contains("Loadout") || cname.Contains("Strategy") ||
                            cname.Contains("Menu UI") || cname.Contains("Options") ||
                            cname.Contains("Scoreboard") || cname.Contains("Victory"))
                        {
                            convertedCanvasIds.Add(id);
                            continue;
                        }

                        // HUD → ScreenSpaceCamera with centered viewport for VR
                        // planeDistance=0.5 keeps HUD close to camera so it doesn't
                        // clip through the floor when looking down
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        canvas.worldCamera = cam;
                        canvas.planeDistance = 0.5f;

                        // Only create the viewport wrapper once
                        if (canvas.transform.Find("VR_HUD_Viewport") == null)
                        {
                            // Create a centered viewport to pull edge-anchored elements inward
                            // (health, ammo, flags are anchored to screen edges — invisible in VR)
                            var viewportGO = new GameObject("VR_HUD_Viewport");
                            var viewport = viewportGO.AddComponent<RectTransform>();
                            viewport.SetParent(canvas.transform, false);
                            viewport.anchorMin = new Vector2(0.25f, 0.25f);
                            viewport.anchorMax = new Vector2(0.75f, 0.75f);
                            viewport.offsetMin = Vector2.zero;
                            viewport.offsetMax = Vector2.zero;

                            // Reparent all existing children into the viewport
                            var children = new List<Transform>();
                            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
                            {
                                var child = canvas.transform.GetChild(i);
                                if (child.gameObject != viewportGO)
                                    children.Add(child);
                            }
                            foreach (var child in children)
                                child.SetParent(viewport, false);
                        }
                    }
                }
                else
                {
                    // Menu: static floating panel in world
                    canvas.renderMode = RenderMode.WorldSpace;
                    canvas.worldCamera = cam;

                    RectTransform rect = canvas.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        float scale = 0.0025f;
                        rect.localScale = new Vector3(scale, scale, scale);

                        Vector3 pos = cam.transform.position + Vector3.forward * 5.5f;
                        pos.y = cam.transform.position.y;
                        rect.position = pos;
                        rect.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                    }

                    if (canvas.GetComponent<GraphicRaycaster>() == null)
                        canvas.gameObject.AddComponent<GraphicRaycaster>();
                }

                convertedCanvasIds.Add(id);
            }

            // Ensure KillIndicatorUi canvas is converted for VR
            if (inGameplay)
            {
                try
                {
                    if (KillIndicatorUi.instance != null)
                    {
                        Canvas killCanvas = KillIndicatorUi.instance.GetComponent<Canvas>();
                        if (killCanvas != null && killCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
                        {
                            killCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                            killCanvas.worldCamera = cam;
                            killCanvas.planeDistance = 3f;
                        }
                        else if (killCanvas != null && killCanvas.renderMode == RenderMode.ScreenSpaceCamera)
                        {
                            if (killCanvas.worldCamera == null || !killCanvas.worldCamera.isActiveAndEnabled)
                                killCanvas.worldCamera = cam;
                        }
                    }
                }
                catch { }

                // Ensure KillCamera canvas (death screen) is visible in VR
                try
                {
                    if (KillCamera.instance != null)
                    {
                        Canvas killCamCanvas = KillCamera.instance.GetComponent<Canvas>()
                            ?? KillCamera.instance.GetComponentInChildren<Canvas>();
                        if (killCamCanvas != null)
                        {
                            if (killCamCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
                            {
                                killCamCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                                killCamCanvas.worldCamera = cam;
                                killCamCanvas.planeDistance = 3f;
                            }
                            else if (killCamCanvas.renderMode == RenderMode.ScreenSpaceCamera)
                            {
                                if (killCamCanvas.worldCamera == null || !killCamCanvas.worldCamera.isActiveAndEnabled)
                                    killCamCanvas.worldCamera = cam;
                            }
                        }
                    }
                }
                catch { }
            }

            // Keep worldCamera updated and reparent dynamic HUD children into viewport
            foreach (var canvas in Object.FindObjectsOfType<Canvas>())
            {
                if (!canvas.isRootCanvas) continue;
                if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
                {
                    if (canvas.worldCamera == null || !canvas.worldCamera.isActiveAndEnabled)
                        canvas.worldCamera = cam;
                    // Catch dynamically created elements (kill feed, notifications)
                    // and move them into the viewport container
                    if (inGameplay)
                    {
                        Transform viewport = canvas.transform.Find("VR_HUD_Viewport");
                        if (viewport != null)
                        {
                            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
                            {
                                Transform child = canvas.transform.GetChild(i);
                                if (child != viewport)
                                    child.SetParent(viewport, false);
                            }
                        }
                    }
                }
                else if (canvas.renderMode == RenderMode.WorldSpace)
                {
                    if (canvas.worldCamera == null || !canvas.worldCamera.isActiveAndEnabled)
                        canvas.worldCamera = cam;
                }
            }
        }

        private static Camera cachedCamera;
        private static GameObject fallbackCameraGO;

        private static Camera GetActiveCamera()
        {
            if (FpsActorController.instance != null)
            {
                Camera c = FpsActorController.instance.GetActiveCamera();
                if (c != null && c.isActiveAndEnabled) { cachedCamera = c; return c; }
            }
            if (Camera.main != null && Camera.main.isActiveAndEnabled)
            {
                cachedCamera = Camera.main;
                return Camera.main;
            }
            foreach (var cam in Camera.allCameras)
            {
                if (cam.isActiveAndEnabled) { cachedCamera = cam; return cam; }
            }

            if (fallbackCameraGO == null)
            {
                fallbackCameraGO = new GameObject("VR Fallback Camera");
                Object.DontDestroyOnLoad(fallbackCameraGO);
                var fallbackCam = fallbackCameraGO.AddComponent<Camera>();
                fallbackCam.clearFlags = CameraClearFlags.SolidColor;
                fallbackCam.backgroundColor = Color.black;
                fallbackCam.depth = -100;
                fallbackCam.stereoTargetEye = StereoTargetEyeMask.Both;
                fallbackCam.nearClipPlane = 0.05f;
                cachedCamera = fallbackCam;
                Plugin.Log.LogInfo("Created fallback VR camera.");
            }

            if (cachedCamera != null && cachedCamera.gameObject == fallbackCameraGO)
                fallbackCameraGO.SetActive(true);

            return cachedCamera;
        }

        private static void ManageFallbackCamera(Camera activeCam)
        {
            if (fallbackCameraGO != null && activeCam != null && activeCam.gameObject != fallbackCameraGO)
                fallbackCameraGO.SetActive(false);
        }

        private static void EnsureStereo(Camera cam)
        {
            if (cam.stereoTargetEye != StereoTargetEyeMask.Both)
            {
                cam.stereoTargetEye = StereoTargetEyeMask.Both;
                if (cam.nearClipPlane > 0.05f)
                    cam.nearClipPlane = 0.05f;
            }
        }

        public static void RecenterVR()
        {
            if (!XRSettings.isDeviceActive) return;

#pragma warning disable CS0618
            InputTracking.Recenter();
#pragma warning restore CS0618

            if (FpsActorController.instance != null)
                playerYaw = FpsActorController.instance.transform.eulerAngles.y;

            savedOriginalPos = false;
            convertedCanvasIds.Clear();
            Plugin.Log.LogInfo("VR view recentered.");
        }
    }

}

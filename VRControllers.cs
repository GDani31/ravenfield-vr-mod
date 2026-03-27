using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using Valve.VR;

namespace RavenfieldVRMod
{
    /// <summary>
    /// VR controller tracking using OpenVR API directly.
    ///
    /// Unity's InputDevice API goes stale and buttons don't work because
    /// there are no SteamVR action bindings configured. Instead we call
    /// OpenVR.System.GetDeviceToAbsoluteTrackingPose() directly for
    /// live pose data, and IVRSystem.GetControllerState() for buttons.
    /// </summary>
    public class VRControllers : MonoBehaviour
    {
        public static VRControllers Instance { get; private set; }

        private GameObject leftHand;
        private GameObject rightHand;
        private LineRenderer leftLaser;
        private LineRenderer rightLaser;

        private const float LASER_LENGTH = 15f;
        private const float LASER_WIDTH = 0.003f;
        private const float HAND_SIZE = 0.045f;

        // Menu open tracking — set by Harmony patches
        public static bool IsMapOpen;
        public static bool IsOptionsOpen;

        // Laser hit dot for menu interaction
        private GameObject laserDot;

        private int logCounter;

        // OpenVR device indices
        private uint leftIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
        private uint rightIndex = OpenVR.k_unTrackedDeviceIndexInvalid;

        // Pose buffer
        private TrackedDevicePose_t[] poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private TrackedDevicePose_t[] gamePoses = new TrackedDevicePose_t[0];

        // Expose thumbstick for Plugin.cs snap turn
        public Vector2 RightThumbstick => new Vector2(VRInput.RightStickX, VRInput.RightStickY);

        // Expose device indices for VRReload haptics
        public uint LeftDeviceIndex => leftIndex;
        public uint RightDeviceIndex => rightIndex;

        public GameObject LeftHand => leftHand;
        public GameObject RightHand => rightHand;

        public static void Create()
        {
            if (Instance != null) return;
            try
            {
                var go = new GameObject("VR Controllers");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<VRControllers>();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Failed to create VR controllers: {e}");
            }
        }

        public static void DestroyInstance()
        {
            if (Instance != null)
            {
                Object.Destroy(Instance.gameObject);
                Instance = null;
            }
        }

        private void Awake()
        {
            try
            {
                leftHand = CreateHand("VR Left Hand", new Color(0.2f, 0.5f, 1f, 0.3f));
                rightHand = CreateHand("VR Right Hand", new Color(1f, 0.3f, 0.3f, 0.3f));
                leftLaser = CreateLaser(leftHand, new Color(0.2f, 0.5f, 1f, 0.25f));
                rightLaser = CreateLaser(rightHand, new Color(1f, 0.3f, 0.3f, 0.25f));
                leftLaser.enabled = false;
                rightLaser.enabled = false;
                laserDot = CreateDot();
                Plugin.Log.LogInfo("VR Controllers created successfully.");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"VR Controllers Awake failed: {e}");
            }
        }

        private void Update()
        {
            if (!VRManager.IsVRActive || OpenVR.System == null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);

            // Find controller device indices
            FindControllerIndices();

            // Get device poses from OpenVR Compositor (most reliable method)
            // GetLastPoses returns the poses used for the current frame
            var compositor = OpenVR.Compositor;
            if (compositor != null)
            {
                compositor.GetLastPoses(poses, gamePoses);
            }
            else
            {
                // Fallback to System API
                OpenVR.System.GetDeviceToAbsoluteTrackingPose(
                    ETrackingUniverseOrigin.TrackingUniverseStanding, 0f, poses);
            }

            // Get HMD pose for offset calculation
            Vector3 headTrackingPos = Vector3.zero;
            Quaternion headTrackingRot = Quaternion.identity;
            if (poses[0].bPoseIsValid)
            {
                GetPoseFromMatrix(poses[0].mDeviceToAbsoluteTracking, out headTrackingPos, out headTrackingRot);
            }

            // Update hand positions
            bool leftOk = UpdateHandFromPose(leftHand, leftIndex, headTrackingPos, headTrackingRot);
            bool rightOk = UpdateHandFromPose(rightHand, rightIndex, headTrackingPos, headTrackingRot);

            // Show lasers only in menus, not during active gameplay
            bool showLasers = ShouldShowLasers();
            if (showLasers)
            {
                // Make lasers clearly visible in menus
                if (leftLaser != null)
                {
                    leftLaser.startColor = new Color(0.2f, 0.5f, 1f, 0.8f);
                    leftLaser.endColor = new Color(0.2f, 0.5f, 1f, 0.4f);
                }
                if (rightLaser != null)
                {
                    rightLaser.startColor = new Color(1f, 0.3f, 0.3f, 0.8f);
                    rightLaser.endColor = new Color(1f, 0.3f, 0.3f, 0.4f);
                }
                UpdateLaser(leftHand, leftLaser);
                UpdateLaser(rightHand, rightLaser);
            }
            else
            {
                if (leftLaser != null) leftLaser.enabled = false;
                if (rightLaser != null) rightLaser.enabled = false;
            }

            // Read ALL input from OpenVR
            VRInput.Update(leftIndex, rightIndex);

            // Laser dot + UI clicking — dominant hand, only when lasers visible
            bool lh = VRManager.LeftHanded;
            bool pointerOk = lh ? leftOk : rightOk;
            GameObject pointerHand = lh ? leftHand : rightHand;
            bool pointerTrigger = lh ? VRInput.LeftTriggerDown : VRInput.RightTriggerDown;

            if (pointerOk && showLasers)
            {
                bool hitUI = UpdateUIPointer(pointerHand.transform, pointerTrigger);
                UpdateLaserDot(pointerHand.transform, hitUI, laserDot != null ? laserDot.transform.position : Vector3.zero);
            }
            else
            {
                UpdateLaserDot(null, false, Vector3.zero);
            }

            // VR gesture reload update
            if (VRReload.Instance != null && VRReload.Enabled)
            {
                bool leftH = VRManager.LeftHanded;
                GameObject dominant = leftH ? leftHand : rightHand;
                GameObject offhand2 = leftH ? rightHand : leftHand;
                uint offIdx = leftH ? rightIndex : leftIndex;
                uint domIdx = leftH ? leftIndex : rightIndex;
                VRReload.Instance.UpdateReload(dominant, offhand2, offIdx, domIdx);
            }

            // Weapon positioning is done in LateUpdate (after game's own updates are patched out)

            // Debug
            if (++logCounter % 300 == 1)
            {
                // Log RAW tracking data
                Vector3 rawL = Vector3.zero, rawR = Vector3.zero;
                bool rawLValid = false, rawRValid = false;
                if (leftIndex < (uint)poses.Length)
                {
                    rawLValid = poses[leftIndex].bPoseIsValid;
                    if (rawLValid) GetPoseFromMatrix(poses[leftIndex].mDeviceToAbsoluteTracking, out rawL, out _);
                }
                if (rightIndex < (uint)poses.Length)
                {
                    rawRValid = poses[rightIndex].bPoseIsValid;
                    if (rawRValid) GetPoseFromMatrix(poses[rightIndex].mDeviceToAbsoluteTracking, out rawR, out _);
                }

                Plugin.Log.LogInfo($"Raw L[{leftIndex}]:{(rawLValid ? rawL.ToString("F3") : "invalid")} " +
                                   $"R[{rightIndex}]:{(rawRValid ? rawR.ToString("F3") : "invalid")} " +
                                   $"Head:{headTrackingPos.ToString("F3")} " +
                                   $"Compositor:{(compositor != null ? "ok" : "NULL")}");
                Camera debugCam = GetVRCamera();
                Plugin.Log.LogInfo($"World L:{(leftOk ? leftHand.transform.position.ToString("F3") : "none")} " +
                                   $"R:{(rightOk ? rightHand.transform.position.ToString("F3") : "none")} " +
                                   $"Cam:{(debugCam != null ? debugCam.name : "NULL")} " +
                                   $"CamPos:{(debugCam != null ? debugCam.transform.position.ToString("F3") : "?")} " +
                                   $"RTrig:{VRInput.RightTrigger} RGrip:{VRInput.RightGrip} RA:{VRInput.RightA} RB:{VRInput.RightB} " +
                                   $"LGrip:{VRInput.LeftGrip} LA:{VRInput.LeftA} LB:{VRInput.LeftB} " +
                                   $"LStkClk:{VRInput.LeftStickClick} RStkClk:{VRInput.RightStickClick}");
            }
        }

        private void FindControllerIndices()
        {
            // Re-find every few seconds in case controllers reconnect
            if (logCounter % 120 != 0 &&
                leftIndex != OpenVR.k_unTrackedDeviceIndexInvalid &&
                rightIndex != OpenVR.k_unTrackedDeviceIndexInvalid)
                return;

            leftIndex = OpenVR.System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);
            rightIndex = OpenVR.System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
        }

        private bool UpdateHandFromPose(GameObject hand, uint deviceIndex,
            Vector3 headTrackingPos, Quaternion headTrackingRot)
        {
            if (deviceIndex == OpenVR.k_unTrackedDeviceIndexInvalid || deviceIndex >= poses.Length)
            {
                hand.transform.position = Vector3.one * 9999f;
                return false;
            }

            var pose = poses[deviceIndex];
            if (!pose.bPoseIsValid || !pose.bDeviceIsConnected)
            {
                hand.transform.position = Vector3.one * 9999f;
                return false;
            }

            GetPoseFromMatrix(pose.mDeviceToAbsoluteTracking, out Vector3 handPos, out Quaternion handRot);

            Camera cam = GetVRCamera();
            if (cam != null)
            {
                // Compute tracking-to-world rotation from camera and HMD:
                // cam.worldRotation = trackingToWorld * headTrackingRot
                // trackingToWorld = cam.worldRotation * inverse(headTrackingRot)
                Quaternion trackingToWorld = cam.transform.rotation * Quaternion.Inverse(headTrackingRot);

                // Position: camera pos + rotated (hand - head) offset
                // Horizontal axes scaled by VR_WORLD_SCALE to match game world.
                // Vertical (Y) at 1:1 — no compression, so hands stay at real height.
                // Small rightward correction for tracking-to-camera alignment.
                Vector3 offset = handPos - headTrackingPos;
                // Hands at 85% of real distance from head (not 65% world scale)
                // so they don't appear too close to the face
                const float HAND_SCALE = 0.85f;
                Vector3 scaledOffset = new Vector3(
                    offset.x * HAND_SCALE,
                    offset.y,
                    offset.z * HAND_SCALE
                );
                hand.transform.position = cam.transform.position + trackingToWorld * scaledOffset;

                // Rotation: map from tracking space to world space + 60° pitch for natural hold
                hand.transform.rotation = trackingToWorld * handRot * Quaternion.Euler(60f, 0f, 0f);
            }
            return true;
        }

        /// <summary>
        /// Converts an OpenVR HmdMatrix34_t to Unity position + rotation.
        /// OpenVR is right-handed (z-forward), Unity is left-handed (z-forward).
        /// </summary>
        private void GetPoseFromMatrix(HmdMatrix34_t mat, out Vector3 pos, out Quaternion rot)
        {
            // Position: negate Z for handedness conversion
            pos = new Vector3(mat.m3, mat.m7, -mat.m11);

            // Rotation matrix → quaternion with handedness flip
            // OpenVR matrix is row-major 3x4
            Matrix4x4 m = new Matrix4x4();
            m[0, 0] = mat.m0; m[0, 1] = mat.m1; m[0, 2] = -mat.m2; m[0, 3] = mat.m3;
            m[1, 0] = mat.m4; m[1, 1] = mat.m5; m[1, 2] = -mat.m6; m[1, 3] = mat.m7;
            m[2, 0] = -mat.m8; m[2, 1] = -mat.m9; m[2, 2] = mat.m10; m[2, 3] = -mat.m11;
            m[3, 0] = 0; m[3, 1] = 0; m[3, 2] = 0; m[3, 3] = 1;

            rot = m.rotation;
        }

        // Hover tracking for proper enter/exit events
        private GameObject lastHoveredObject;

        /// <summary>
        /// Raycasts from controller against WorldSpace canvas planes directly,
        /// positions the dot at the hit, sends hover/click events through
        /// Unity's EventSystem for proper button highlighting.
        /// </summary>
        private bool UpdateUIPointer(Transform hand, bool triggerDown)
        {
            Camera cam = GetVRCamera();
            if (cam == null) return false;

            Vector3 rayOrigin = hand.position;
            Vector3 rayDir = hand.forward;

            // Find the closest WorldSpace canvas the laser intersects
            Canvas hitCanvas = null;
            Vector3 hitWorldPoint = Vector3.zero;
            float closestDist = LASER_LENGTH;

            foreach (var canvas in Object.FindObjectsOfType<Canvas>())
            {
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    continue;
                if (!canvas.isRootCanvas)
                    continue;

                // Ray-plane intersection against the canvas surface
                Vector3 canvasNormal = canvas.transform.forward;
                Vector3 canvasPoint = canvas.transform.position;
                float denom = Vector3.Dot(canvasNormal, rayDir);
                if (Mathf.Abs(denom) < 0.0001f) continue;

                float dist = Vector3.Dot(canvasPoint - rayOrigin, canvasNormal) / denom;
                if (dist <= 0 || dist >= closestDist) continue;

                Vector3 worldHit = rayOrigin + rayDir * dist;

                // Check if hit point is within canvas bounds
                RectTransform cRect = canvas.GetComponent<RectTransform>();
                if (cRect != null)
                {
                    // Convert world hit to canvas local coordinates
                    Vector3 localHit = cRect.InverseTransformPoint(worldHit);
                    if (cRect.rect.Contains(new Vector2(localHit.x, localHit.y)))
                    {
                        hitCanvas = canvas;
                        hitWorldPoint = worldHit;
                        closestDist = dist;
                    }
                }
            }

            if (hitCanvas == null)
            {
                // No hit — clear hover and hide dot
                ClearHover();
                if (laserDot != null) laserDot.SetActive(false);
                return false;
            }

            // Ensure canvas camera matches for correct EventSystem raycasting
            if (hitCanvas.renderMode == RenderMode.WorldSpace && hitCanvas.worldCamera != cam)
                hitCanvas.worldCamera = cam;

            // Position dot at hit point — offset toward controller so it's in front of canvas
            if (laserDot != null)
            {
                laserDot.SetActive(true);
                float dotOffset = 0.015f; // 15mm in front of canvas
                laserDot.transform.position = hitWorldPoint - rayDir * dotOffset;
                // Scale dot based on distance so it's always visible
                float dotScale = Mathf.Max(0.015f, closestDist * 0.005f);
                laserDot.transform.localScale = Vector3.one * dotScale;
                // Face the dot toward the controller
                laserDot.transform.rotation = Quaternion.LookRotation(-rayDir);
            }

            // Convert hit point to screen coordinates for EventSystem
            Vector3 screenPoint = cam.WorldToScreenPoint(hitWorldPoint);
            if (screenPoint.z < 0)
            {
                ClearHover();
                return true;
            }

            // Raycast through EventSystem to find the UI element
            if (EventSystem.current == null) return true;

            var pointerData = new PointerEventData(EventSystem.current);
            pointerData.position = new Vector2(screenPoint.x, screenPoint.y);

            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            // Fallback: if EventSystem found nothing, manually check Graphics on the canvas
            if (results.Count == 0)
            {
                var allGraphics = hitCanvas.GetComponentsInChildren<Graphic>();
                for (int i = allGraphics.Length - 1; i >= 0; i--)
                {
                    var g = allGraphics[i];
                    if (!g.raycastTarget || !g.gameObject.activeInHierarchy) continue;
                    Vector3 lp = g.rectTransform.InverseTransformPoint(hitWorldPoint);
                    if (g.rectTransform.rect.Contains(new Vector2(lp.x, lp.y)))
                    {
                        results.Add(new RaycastResult { gameObject = g.gameObject });
                        break;
                    }
                }
            }

            if (results.Count > 0)
            {
                GameObject hitObj = results[0].gameObject;

                // Send hover events for button highlighting
                if (hitObj != lastHoveredObject)
                {
                    ClearHover();
                    lastHoveredObject = hitObj;

                    // Send PointerEnter to trigger highlight
                    ExecuteEvents.Execute(hitObj, pointerData, ExecuteEvents.pointerEnterHandler);

                    // Also walk up parents for Selectable highlight
                    var selectable = hitObj.GetComponentInParent<Selectable>();
                    if (selectable != null)
                    {
                        selectable.OnPointerEnter(pointerData);
                    }
                }

                // Click on trigger press
                if (triggerDown)
                {
                    // Send full pointer event chain: down → click → up
                    pointerData.pointerPressRaycast = results[0];
                    pointerData.pointerPress = hitObj;
                    ExecuteEvents.Execute(hitObj, pointerData, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(hitObj, pointerData, ExecuteEvents.pointerClickHandler);
                    ExecuteEvents.Execute(hitObj, pointerData, ExecuteEvents.pointerUpHandler);

                    // Also bubble up to find clickable parent (Button, Toggle, etc.)
                    // ExecuteEvents.GetEventHandler walks up the hierarchy
                    GameObject clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitObj);
                    if (clickHandler != null && clickHandler != hitObj)
                    {
                        ExecuteEvents.Execute(clickHandler, pointerData, ExecuteEvents.pointerClickHandler);
                    }

                    // Bubble up for IPointerDownHandler (map picker entries use OnPointerDown, not OnClick)
                    GameObject downHandler = ExecuteEvents.GetEventHandler<IPointerDownHandler>(hitObj);
                    if (downHandler != null && downHandler != hitObj)
                    {
                        ExecuteEvents.Execute(downHandler, pointerData, ExecuteEvents.pointerDownHandler);
                    }

                    // Log what was clicked — ExecuteEvents above already handles
                    // the actual click via pointerClickHandler + bubble-up.
                    // Do NOT manually invoke btn.onClick here — that double-fires
                    // and causes toggle settings to flip ON then immediately back OFF.
                    Toggle toggle = hitObj.GetComponentInParent<Toggle>();
                    Button btn = hitObj.GetComponentInParent<Button>();
                    if (toggle != null && toggle.interactable)
                    {
                        Plugin.Log.LogInfo($"VR click: Toggle {toggle.name}");
                    }
                    else if (btn != null && btn.interactable)
                    {
                        Plugin.Log.LogInfo($"VR click: Button {btn.name}");
                    }
                    else
                    {
                        // Fallback: select and submit for non-standard UI (e.g. map entries)
                        Selectable sel = hitObj.GetComponentInParent<Selectable>();
                        if (sel != null)
                        {
                            sel.Select();
                            ExecuteEvents.Execute(sel.gameObject,
                                new BaseEventData(EventSystem.current),
                                ExecuteEvents.submitHandler);
                            Plugin.Log.LogInfo($"VR submit: {sel.name}");
                        }
                    }

                    Plugin.Log.LogInfo($"VR pointer hit: {hitObj.name} parent:{hitObj.transform.parent?.name}");
                }
            }
            else
            {
                ClearHover();
            }

            return true;
        }

        private void ClearHover()
        {
            if (lastHoveredObject != null && EventSystem.current != null)
            {
                var exitData = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(lastHoveredObject, exitData, ExecuteEvents.pointerExitHandler);

                var selectable = lastHoveredObject.GetComponentInParent<Selectable>();
                if (selectable != null)
                    selectable.OnPointerExit(exitData);

                lastHoveredObject = null;
            }
        }

        /// <summary>
        /// LateUpdate runs AFTER all game updates.
        /// PlayerFpParent.LateUpdate is patched out (Prefix returns false),
        /// so we have full control over weapon positioning here.
        /// </summary>
        private void LateUpdate()
        {
            if (!VRManager.IsVRActive) return;
            if (FpsActorController.instance == null) return;

            bool wlh = VRManager.LeftHanded;
            GameObject dominant = wlh ? leftHand : rightHand;
            GameObject offhand = wlh ? rightHand : leftHand;

            if (dominant == null || dominant.transform.position.x > 9000f) return;

            Transform wp = FpsActorController.instance.weaponParent;
            if (wp == null) return;

            PlayerFpParent fpParent = FpsActorController.instance.fpParent;
            bool twoHanded = (wlh ? VRInput.RightGrip : VRInput.LeftGrip) &&
                             offhand != null && offhand.transform.position.x < 9000f;

            Quaternion weaponRot;
            if (twoHanded)
            {
                Vector3 aimDir = (offhand.transform.position - dominant.transform.position).normalized;
                if (aimDir.sqrMagnitude > 0.01f)
                    weaponRot = Quaternion.LookRotation(aimDir, dominant.transform.up);
                else
                    weaponRot = dominant.transform.rotation;
            }
            else
            {
                weaponRot = dominant.transform.rotation;
            }

            Vector3 wpOffset = -(weaponRot * Vector3.right) * 0.10f
                             + (weaponRot * Vector3.up) * 0.09f
                             - (weaponRot * Vector3.forward) * 0.20f;
            wp.position = dominant.transform.position + wpOffset;
            wp.rotation = weaponRot;

            // Move the shoulder/arm model to match
            if (fpParent != null && fpParent.shoulderParent != null)
            {
                fpParent.shoulderParent.position = wp.position;
                fpParent.shoulderParent.rotation = wp.rotation;
            }
        }

        private static Camera cachedCamera;

        private Camera GetVRCamera()
        {
            // Try gameplay camera
            if (FpsActorController.instance != null)
            {
                Camera c = FpsActorController.instance.GetActiveCamera();
                if (c != null && c.isActiveAndEnabled) { cachedCamera = c; return c; }
            }

            // Try main camera
            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled) { cachedCamera = main; return main; }

            // Try ANY active camera (menu scenes often don't tag their camera as MainCamera)
            foreach (var cam in Camera.allCameras)
            {
                if (cam.isActiveAndEnabled) { cachedCamera = cam; return cam; }
            }

            // Last resort: use cached camera from before scene transition
            if (cachedCamera != null) return cachedCamera;

            return null;
        }

        private void UpdateLaser(GameObject hand, LineRenderer laser)
        {
            if (hand.transform.position.x > 9000f) { laser.enabled = false; return; }
            laser.enabled = true;
            Vector3 origin = hand.transform.position;
            Vector3 end = origin + hand.transform.forward * LASER_LENGTH;

            // Raycast to find hit point for laser shortening
            if (Physics.Raycast(origin, hand.transform.forward, out RaycastHit hit, LASER_LENGTH))
            {
                end = hit.point;
            }

            laser.SetPosition(0, origin);
            laser.SetPosition(1, end);
        }

        private void UpdateLaserDot(Transform hand, bool hitUI, Vector3 hitPoint)
        {
            if (laserDot == null) return;

            if (hitUI)
            {
                laserDot.SetActive(true);
                laserDot.transform.position = hitPoint;
            }
            else
            {
                laserDot.SetActive(false);
            }
        }

        private bool ShouldShowLasers()
        {
            try
            {
                // In gameplay — only show when a menu is explicitly open
                if (FpsActorController.instance != null || GameManager.IsIngame())
                {
                    if (IngameMenuUi.IsOpen()) return true;
                    if (LoadoutUi.IsOpen()) return true;
                    if (IsMapOpen) return true;
                    if (IsOptionsOpen) return true;
                    return false;
                }
                // Not in gameplay — main menu
                return true;
            }
            catch { }
            return false;
        }

        private void SetVisible(bool visible)
        {
            if (leftHand != null) leftHand.SetActive(visible);
            if (rightHand != null) rightHand.SetActive(visible);
        }

        private GameObject CreateHand(string name, Color color)
        {
            var hand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hand.name = name;
            hand.transform.SetParent(transform, false);
            hand.transform.localScale = Vector3.one * HAND_SIZE;
            var col = hand.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var renderer = hand.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("UI/Default");
                if (shader != null) renderer.material = new Material(shader) { color = color };
            }
            return hand;
        }

        private GameObject CreateDot()
        {
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "VR Laser Dot";
            dot.transform.SetParent(transform, false);
            dot.transform.localScale = Vector3.one * 0.015f; // Small dot

            var col = dot.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            var renderer = dot.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("UI/Default");
                if (shader != null)
                {
                    var mat = new Material(shader) { color = Color.white };
                    mat.renderQueue = 5000; // Render on top of UI
                    renderer.material = mat;
                    renderer.sortingOrder = 32767; // Above all canvas sorting orders
                }
            }
            dot.SetActive(false);
            return dot;
        }

        private LineRenderer CreateLaser(GameObject hand, Color color)
        {
            var laserObj = new GameObject("Laser");
            laserObj.transform.SetParent(hand.transform, false);
            var lr = laserObj.AddComponent<LineRenderer>();
            lr.startWidth = LASER_WIDTH;
            lr.endWidth = LASER_WIDTH * 0.5f;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            if (shader != null) lr.material = new Material(shader) { color = color };
            lr.startColor = color;
            lr.endColor = new Color(color.r, color.g, color.b, 0.1f);
            return lr;
        }
    }
}

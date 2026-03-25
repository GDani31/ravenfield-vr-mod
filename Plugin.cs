using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace RavenfieldVRMod
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.ravenfield.vrmod";
        public const string PluginName = "Ravenfield VR Mod";
        public const string PluginVersion = "1.0.3";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        private Harmony harmony;
        private bool controllersCreated;
        private int frameCount;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            VRManager.Initialize();

            harmony = new Harmony(PluginGUID);
            harmony.PatchAll();

            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void Update()
        {
            frameCount++;

            // Core VR camera + HMD tracking + canvas conversion
            VRCameraManager.UpdateEveryFrame();

            bool vrActive = VRManager.IsVRActive;

            // Log VR state periodically
            if (frameCount % 600 == 1)
            {
                Logger.LogInfo($"Frame {frameCount}: VRActive={vrActive}, " +
                               $"XREnabled={XRSettings.enabled}, " +
                               $"DeviceActive={XRSettings.isDeviceActive}, " +
                               $"Controllers={controllersCreated}");
            }

            if (vrActive)
            {
                if (!controllersCreated)
                {
                    Logger.LogInfo("Creating VR controllers...");
                    VRControllers.Create();
                    controllersCreated = true;
                    Logger.LogInfo("VR controllers create call done.");
                }

                // F12 to recenter
                if (Input.GetKeyDown(KeyCode.F11))
                {
                    VRCameraManager.RecenterVR();
                }

                // Right thumbstick for snap turning (45 degree increments)
                HandleSnapTurn();
            }
            else if (controllersCreated)
            {
                VRControllers.DestroyInstance();
                controllersCreated = false;
            }
        }

        private bool snapTurnReady = true;

        private void HandleSnapTurn()
        {
            if (VRControllers.Instance == null) return;

            // Left-handed: turn with left stick, right-handed: turn with right stick
            float turnAxis = VRManager.LeftHanded ? VRInput.LeftStickX : VRInput.RightStickX;

            if (VRManager.TurnMode == 0)
            {
                // Free rotation (default)
                if (Mathf.Abs(turnAxis) > 0.5f)
                {
                    float turnSpeed = 90f * Mathf.Sign(turnAxis);
                    VRCameraManager.RotatePlayer(turnSpeed * Time.deltaTime);
                }
            }
            else
            {
                // Snap turn
                if (Mathf.Abs(turnAxis) > 0.7f && snapTurnReady)
                {
                    VRCameraManager.RotatePlayer(VRManager.SnapAngle * Mathf.Sign(turnAxis));
                    snapTurnReady = false;
                }
                if (Mathf.Abs(turnAxis) < 0.3f)
                    snapTurnReady = true;
            }
        }

        private void LateUpdate()
        {
            // Re-apply HMD rotation AFTER all game systems (turret cameras,
            // vehicle cameras, etc.) so head tracking always wins
            if (VRManager.IsVRActive)
                VRCameraManager.ReapplyHMDPose();
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            VRManager.Shutdown();
            VRControllers.DestroyInstance();
        }
    }
}

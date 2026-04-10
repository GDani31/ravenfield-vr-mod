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
                    VRReload.Create();
                    controllersCreated = true;
                    Logger.LogInfo("VR controllers create call done.");
                }

                // F11 to recenter
                if (Input.GetKeyDown(KeyCode.F11))
                {
                    VRCameraManager.RecenterVR();
                }

                // Both joysticks pressed simultaneously = recenter
                if (VRInput.LeftStickClickDown && VRInput.RightStickClick ||
                    VRInput.RightStickClickDown && VRInput.LeftStickClick)
                {
                    VRCameraManager.RecenterVR();
                    Logger.LogInfo("VR: Recentered (both sticks)");
                }

                // Dominant A button = also SwitchFireMode (alongside Use)
                HandleFireModeSwitch();

                // Right grip = turret zoom when on turret
                HandleTurretZoom();

                // Right thumbstick for snap turning (45 degree increments)
                HandleSnapTurn();
            }
            else if (controllersCreated)
            {
                VRControllers.DestroyInstance();
                VRReload.DestroyInstance();
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
                    float turnSpeed = VRManager.SmoothTurnSpeed * Mathf.Sign(turnAxis);
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

        // ── Turret zoom via grip ──
        private static bool turretZoomReflectionDone;
        private static System.Reflection.MethodInfo setAimingMethod;   // Weapon.SetAiming(bool)
        private static System.Reflection.FieldInfo weaponAimingField;  // Weapon.aiming
        private static System.Reflection.FieldInfo actorFieldCached;
        private static System.Reflection.FieldInfo activeWeaponFieldCached;

        private void HandleTurretZoom()
        {
            if (!VRCameraManager.IsOnTurret || FpsActorController.instance == null) return;

            bool grip = VRManager.LeftHanded ? VRInput.LeftGrip : VRInput.RightGrip;

            if (!turretZoomReflectionDone)
            {
                turretZoomReflectionDone = true;
                var bf = System.Reflection.BindingFlags.Instance |
                         System.Reflection.BindingFlags.Public |
                         System.Reflection.BindingFlags.NonPublic;

                actorFieldCached = typeof(FpsActorController).GetField("actor", bf);
                if (actorFieldCached != null)
                {
                    activeWeaponFieldCached = actorFieldCached.FieldType.GetField("activeWeapon", bf);
                    if (activeWeaponFieldCached != null)
                    {
                        setAimingMethod = activeWeaponFieldCached.FieldType.GetMethod("SetAiming", bf);
                        weaponAimingField = activeWeaponFieldCached.FieldType.GetField("aiming", bf);
                        Logger.LogInfo($"VR Turret Zoom: SetAiming={setAimingMethod != null} aiming={weaponAimingField != null}");
                    }
                }
            }

            // Call Weapon.SetAiming(grip) every frame — true while held, false on release
            try
            {
                object actor = actorFieldCached?.GetValue(FpsActorController.instance);
                if (actor != null)
                {
                    object weapon = activeWeaponFieldCached?.GetValue(actor);
                    if (weapon != null && grip)
                    {
                        if (setAimingMethod != null)
                            setAimingMethod.Invoke(weapon, new object[] { true });
                        else if (weaponAimingField != null)
                            weaponAimingField.SetValue(weapon, true);

                    }
                }
            }
            catch { }
        }

        private static System.Reflection.MethodInfo switchFireModeMethod;
        private static bool fireModeReflectionDone;

        private void HandleFireModeSwitch()
        {
            // Dominant A button (same as Use) — also calls SwitchFireMode on weapon
            bool aDown = VRManager.LeftHanded ? VRInput.LeftADown : VRInput.RightADown;
            if (!aDown || FpsActorController.instance == null) return;

            // Don't switch fire mode when in menus or vehicles
            if (LoadoutUi.IsOpen() || IngameMenuUi.IsOpen()) return;

            try
            {
                if (!fireModeReflectionDone)
                {
                    fireModeReflectionDone = true;
                    var actorField = typeof(FpsActorController).GetField("actor",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);
                    if (actorField != null)
                    {
                        var weaponField = actorField.FieldType.GetField("activeWeapon",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic);
                        if (weaponField != null)
                            switchFireModeMethod = weaponField.FieldType.GetMethod("SwitchFireMode",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                                System.Reflection.BindingFlags.NonPublic);
                    }
                }

                if (switchFireModeMethod != null)
                {
                    var actorField = typeof(FpsActorController).GetField("actor",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);
                    object actor = actorField?.GetValue(FpsActorController.instance);
                    if (actor != null)
                    {
                        var weaponField = actor.GetType().GetField("activeWeapon",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic);
                        object weapon = weaponField?.GetValue(actor);
                        if (weapon != null)
                            switchFireModeMethod.Invoke(weapon, null);
                    }
                }
            }
            catch { }
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
            VRReload.DestroyInstance();
            VRControllers.DestroyInstance();
        }
    }
}

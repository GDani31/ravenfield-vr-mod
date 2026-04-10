using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using Unity.XR.OpenVR;

namespace RavenfieldVRMod
{
    /// <summary>
    /// Manages VR state using Unity XR Plugin Management + OpenVR XR Plugin.
    /// </summary>
    public static class VRManager
    {
        private const string VR_PREF_KEY = "vr_enabled";

        // Mirror the native struct that XRSDKOpenVR.dll expects.
        // The managed OpenVRLoader only calls SetUserDefinedSettings in UNITY_EDITOR,
        // so we must call it ourselves before initialization.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct UserDefinedSettings
        {
            public ushort stereoRenderingMode;
            public ushort initializationType;
            public ushort mirrorViewMode;
            [MarshalAs(UnmanagedType.LPStr)]
            public string editorAppKey;
            [MarshalAs(UnmanagedType.LPStr)]
            public string actionManifestPath;
            [MarshalAs(UnmanagedType.LPStr)]
            public string applicationName;
        }

        [DllImport("XRSDKOpenVR", CharSet = CharSet.Auto)]
        private static extern void SetUserDefinedSettings(UserDefinedSettings settings);

        public static bool IsVREnabled { get; private set; }
        public static bool IsVRActive => XRSettings.isDeviceActive;
        public static bool IsTransitioning { get; private set; }

        // VR Settings (persisted via PlayerPrefs)
        public static int TurnMode
        {
            get => PlayerPrefs.GetInt("vr_turn_mode", 0);
            set { PlayerPrefs.SetInt("vr_turn_mode", value); PlayerPrefs.Save(); }
        }
        public static int SnapAngle
        {
            get => PlayerPrefs.GetInt("vr_snap_angle", 45);
            set { PlayerPrefs.SetInt("vr_snap_angle", value); PlayerPrefs.Save(); }
        }
        public static int VRFieldOfView
        {
            get => PlayerPrefs.GetInt("vr_fov", 90);
            set { PlayerPrefs.SetInt("vr_fov", value); PlayerPrefs.Save(); }
        }
        public static bool LeftHanded
        {
            get => PlayerPrefs.GetInt("vr_left_handed", 0) == 1;
            set { PlayerPrefs.SetInt("vr_left_handed", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        public static int SmoothTurnSpeed
        {
            get => PlayerPrefs.GetInt("vr_smooth_turn_speed", 90);
            set { PlayerPrefs.SetInt("vr_smooth_turn_speed", value); PlayerPrefs.Save(); }
        }
        public static int HandOpacity
        {
            get => PlayerPrefs.GetInt("vr_hand_opacity", 30);
            set { PlayerPrefs.SetInt("vr_hand_opacity", value); PlayerPrefs.Save(); }
        }

        private static XRGeneralSettings generalSettings;
        private static XRManagerSettings xrManager;
        private static OpenVRLoader openVRLoader;

        public static void Initialize()
        {
            IsVREnabled = PlayerPrefs.GetInt(VR_PREF_KEY, 0) == 1;
            Plugin.Log.LogInfo($"VR preference on startup: {(IsVREnabled ? "enabled" : "disabled")}");

            if (IsVREnabled)
            {
                Plugin.Instance.StartCoroutine(EnableVRCoroutine());
            }
        }

        public static void Shutdown()
        {
            if (IsVRActive)
            {
                StopXR();
            }
        }

        public static void ToggleVR()
        {
            SetVREnabled(!IsVREnabled);
        }

        public static void SetVREnabled(bool enabled)
        {
            if (enabled == IsVREnabled && !IsTransitioning)
                return;

            IsVREnabled = enabled;
            PlayerPrefs.SetInt(VR_PREF_KEY, enabled ? 1 : 0);
            PlayerPrefs.Save();

            if (enabled)
            {
                Plugin.Instance.StartCoroutine(EnableVRCoroutine());
            }
            else
            {
                StopXR();
            }
        }

        private static void SetupXRManagement()
        {
            if (xrManager != null)
                return;

            // Create XRGeneralSettings
            generalSettings = ScriptableObject.CreateInstance<XRGeneralSettings>();
            generalSettings.name = "VRMod XR General Settings";

            var runtimeField = typeof(XRGeneralSettings).GetField("s_RuntimeSettingsInstance",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (runtimeField != null)
            {
                runtimeField.SetValue(null, generalSettings);
                Plugin.Log.LogInfo("Set XRGeneralSettings runtime instance.");
            }

            // Create XRManagerSettings
            xrManager = ScriptableObject.CreateInstance<XRManagerSettings>();
            xrManager.name = "VRMod XR Manager";
            xrManager.automaticLoading = false;
            xrManager.automaticRunning = false;

            generalSettings.Manager = xrManager;

            var initOnStartField = typeof(XRGeneralSettings).GetField("m_InitManagerOnStart",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (initOnStartField != null)
            {
                initOnStartField.SetValue(generalSettings, false);
            }

            // Create managed OpenVR settings
            var openVRSettings = ScriptableObject.CreateInstance<OpenVRSettings>();
            openVRSettings.InitializationType = OpenVRSettings.InitializationTypes.Scene;
            openVRSettings.StereoRenderingMode = OpenVRSettings.StereoRenderingModes.MultiPass;
            openVRSettings.MirrorView = OpenVRSettings.MirrorViewModes.Right;
            OpenVRSettings.s_Settings = openVRSettings;
            Object.DontDestroyOnLoad(openVRSettings);

            // CRITICAL: Push settings to the NATIVE plugin via P/Invoke.
            // The managed OpenVRLoader only does this in #if UNITY_EDITOR,
            // so without this call the native plugin gets initializationType=0 (Unknown)
            // and refuses to render frames.
            var nativeSettings = new UserDefinedSettings
            {
                stereoRenderingMode = (ushort)OpenVRSettings.StereoRenderingModes.MultiPass,
                initializationType = (ushort)OpenVRSettings.InitializationTypes.Scene, // 1 = Scene
                mirrorViewMode = (ushort)OpenVRSettings.MirrorViewModes.Right,
                editorAppKey = "",
                actionManifestPath = "",
                applicationName = "Ravenfield"
            };
            SetUserDefinedSettings(nativeSettings);
            Plugin.Log.LogInfo("Pushed settings to native XRSDKOpenVR: initType=Scene(1), stereo=MultiPass, mirror=Right");

            // Create the OpenVR loader
            openVRLoader = ScriptableObject.CreateInstance<OpenVRLoader>();

            // Add loader to manager
            var currentLoadersProperty = typeof(XRManagerSettings).GetProperty("currentLoaders",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (currentLoadersProperty != null)
            {
                var loadersList = new List<XRLoader> { openVRLoader };
                currentLoadersProperty.SetValue(xrManager, loadersList);
                Plugin.Log.LogInfo("OpenVR loader registered with XR Manager.");
            }

            Object.DontDestroyOnLoad(generalSettings);
            Object.DontDestroyOnLoad(xrManager);
            Object.DontDestroyOnLoad(openVRLoader);

            Plugin.Log.LogInfo("XR Management framework initialized.");
        }

        private static IEnumerator EnableVRCoroutine()
        {
            if (IsTransitioning)
                yield break;

            IsTransitioning = true;
            Plugin.Log.LogInfo("Starting XR via OpenVR XR Plugin...");

            SetupXRManagement();

            // Initialize the loader (connects to SteamVR, starts display subsystem)
            xrManager.InitializeLoaderSync();

            yield return null;

            if (xrManager.activeLoader != null)
            {
                Plugin.Log.LogInfo($"XR Loader active: {xrManager.activeLoader.GetType().Name}");

                xrManager.StartSubsystems();
                yield return null;

                // Initialize SteamVR action system for controller bindings
                InitializeActionSystem();

                if (XRSettings.isDeviceActive)
                {
                    Plugin.Log.LogInfo($"VR is running! Device: {XRSettings.loadedDeviceName}, " +
                                       $"Eye: {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}");
                    XRSettings.eyeTextureResolutionScale = 1.0f;
                    VRCameraManager.OnVREnabled();
                }
                else
                {
                    Plugin.Log.LogWarning("XR subsystems started but device not active yet.");
                    VRCameraManager.OnVREnabled();
                }
            }
            else
            {
                Plugin.Log.LogError("XR loader failed to initialize. Check SteamVR is installed and headset is connected.");
                IsVREnabled = false;
                PlayerPrefs.SetInt(VR_PREF_KEY, 0);
            }

            IsTransitioning = false;
        }

        private static void StopXR()
        {
            Plugin.Log.LogInfo("Stopping XR...");

            VRCameraManager.OnVRDisabled();

            if (xrManager != null)
            {
                xrManager.StopSubsystems();
                xrManager.DeinitializeLoader();
                Plugin.Log.LogInfo("XR stopped.");
            }
        }

        /// <summary>
        /// Locate actions.json next to the plugin DLL and register it with SteamVR.
        /// This enables user-customizable controller bindings via SteamVR Settings.
        /// </summary>
        private static void InitializeActionSystem()
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                string actionsPath = Path.GetFullPath(Path.Combine(pluginDir, "actions.json"));

                if (!File.Exists(actionsPath))
                {
                    Plugin.Log.LogWarning($"VR: actions.json not found at {actionsPath} — controller bindings disabled. " +
                                          "Place actions.json and bindings_*.json next to RavenfieldVRMod.dll.");
                    return;
                }

                if (VRInput.InitializeActions(actionsPath))
                {
                    Plugin.Log.LogInfo("VR: SteamVR controller bindings enabled. " +
                                       "Customize in SteamVR Settings → Controller Bindings.");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"VR: Action system init failed: {e.Message}");
            }
        }
    }
}

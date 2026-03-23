using Valve.VR;

namespace RavenfieldVRMod
{
    /// <summary>
    /// Reads VR controller state from OpenVR and provides it
    /// for injection into SteelInput via Harmony patches.
    ///
    /// Button mapping (Quest 3):
    /// - Left thumbstick → WASD (Horizontal/Vertical)
    /// - Right trigger → Fire (left mouse)
    /// - Right B → Reload
    /// - Left B → Aim/zoom toggle
    /// - Left trigger → Jump
    /// - Left grip → Crouch
    /// - Right grip → Sprint
    /// - Right A → Use/interact
    /// </summary>
    public static class VRInput
    {
        // Button states
        public static float LeftStickX { get; private set; }
        public static float LeftStickY { get; private set; }
        public static float RightStickX { get; private set; }
        public static float RightStickY { get; private set; }

        public static bool RightTrigger { get; private set; }
        public static bool RightTriggerDown { get; private set; }
        public static bool RightTriggerUp { get; private set; }
        public static float RightTriggerAnalog { get; private set; }

        public static bool LeftTrigger { get; private set; }
        public static bool LeftTriggerDown { get; private set; }
        public static bool LeftTriggerUp { get; private set; }
        public static float LeftTriggerAnalog { get; private set; }

        public static bool RightGrip { get; private set; }
        public static bool LeftGrip { get; private set; }

        // B button = ApplicationMenu (EVRButtonId 1)
        public static bool RightB { get; private set; }
        public static bool RightBDown { get; private set; }
        public static bool LeftB { get; private set; }
        public static bool LeftBDown { get; private set; }

        // A button (EVRButtonId 7)
        public static bool RightA { get; private set; }
        public static bool RightADown { get; private set; }
        public static bool LeftA { get; private set; }
        public static bool LeftADown { get; private set; }

        // Thumbstick clicks (EVRButtonId 32)
        public static bool LeftStickClick { get; private set; }
        public static bool LeftStickClickDown { get; private set; }
        public static bool RightStickClick { get; private set; }
        public static bool RightStickClickDown { get; private set; }

        private static bool prevRightTrigger;
        private static bool prevLeftTrigger;
        private static bool prevRightB;
        private static bool prevLeftB;
        private static bool prevRightA;
        private static bool prevLeftA;
        private static bool prevLeftStickClick;
        private static bool prevRightStickClick;

        // Aim toggle removed — not needed in VR

        public static void Update(uint leftIndex, uint rightIndex)
        {
            if (OpenVR.System == null) return;

            // Previous frame states
            prevRightTrigger = RightTrigger;
            prevLeftTrigger = LeftTrigger;
            prevRightB = RightB;
            prevLeftB = LeftB;
            prevRightA = RightA;
            prevLeftA = LeftA;
            prevLeftStickClick = LeftStickClick;
            prevRightStickClick = RightStickClick;

            // Right controller
            if (rightIndex != OpenVR.k_unTrackedDeviceIndexInvalid)
            {
                VRControllerState_t state = default;
                uint size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRControllerState_t));
                if (OpenVR.System.GetControllerState(rightIndex, ref state, size))
                {
                    RightStickX = state.rAxis0.x;
                    RightStickY = state.rAxis0.y;
                    RightTriggerAnalog = state.rAxis1.x;
                    RightTrigger = state.rAxis1.x > 0.5f;
                    RightGrip = (state.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_Grip)) != 0;
                    RightB = (state.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_ApplicationMenu)) != 0;
                    RightA = (state.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_A)) != 0;
                    RightStickClick = (state.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_SteamVR_Touchpad)) != 0;
                }
            }

            // Left controller
            if (leftIndex != OpenVR.k_unTrackedDeviceIndexInvalid)
            {
                VRControllerState_t state = default;
                uint size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRControllerState_t));
                if (OpenVR.System.GetControllerState(leftIndex, ref state, size))
                {
                    LeftStickX = state.rAxis0.x;
                    LeftStickY = state.rAxis0.y;
                    LeftTriggerAnalog = state.rAxis1.x;
                    LeftTrigger = state.rAxis1.x > 0.5f;
                    LeftGrip = (state.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_Grip)) != 0;
                    LeftB = (state.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_ApplicationMenu)) != 0;
                    LeftA = (state.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_A)) != 0;
                    LeftStickClick = (state.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_SteamVR_Touchpad)) != 0;
                }
            }

            // Edge detection
            RightTriggerDown = RightTrigger && !prevRightTrigger;
            RightTriggerUp = !RightTrigger && prevRightTrigger;
            LeftTriggerDown = LeftTrigger && !prevLeftTrigger;
            LeftTriggerUp = !LeftTrigger && prevLeftTrigger;
            RightBDown = RightB && !prevRightB;
            LeftBDown = LeftB && !prevLeftB;
            RightADown = RightA && !prevRightA;
            LeftADown = LeftA && !prevLeftA;
            LeftStickClickDown = LeftStickClick && !prevLeftStickClick;
            RightStickClickDown = RightStickClick && !prevRightStickClick;

        }
    }
}

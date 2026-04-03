using System.Runtime.InteropServices;
using Valve.VR;

namespace RavenfieldVRMod
{
    /// <summary>
    /// Reads VR controller input via the SteamVR action system (IVRInput).
    ///
    /// Actions are defined in actions.json and bound per-controller in
    /// bindings_*.json. Users can rebind controls in SteamVR Settings →
    /// Controller Bindings → Ravenfield VR.
    ///
    /// Each action is queried per-hand via input source handles so the
    /// public API (LeftTrigger, RightGrip, etc.) stays per-hand.
    /// </summary>
    public static class VRInput
    {
        // ── Action handles ──
        private static ulong hTrigger;
        private static ulong hGrip;
        private static ulong hStick;
        private static ulong hButtonA;
        private static ulong hButtonB;
        private static ulong hStickClick;
        private static ulong hHaptic;

        // ── Input source handles ──
        private static ulong srcLeft;
        private static ulong srcRight;

        // ── Action set ──
        private static ulong hActionSet;

        private static bool initialized;

        // ── Public properties (API unchanged) ──

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
        public static float RightGripAnalog { get; private set; }
        public static bool LeftGrip { get; private set; }
        public static float LeftGripAnalog { get; private set; }

        // B button
        public static bool RightB { get; private set; }
        public static bool RightBDown { get; private set; }
        public static bool LeftB { get; private set; }
        public static bool LeftBDown { get; private set; }

        // A button
        public static bool RightA { get; private set; }
        public static bool RightADown { get; private set; }
        public static bool LeftA { get; private set; }
        public static bool LeftADown { get; private set; }

        // Thumbstick clicks
        public static bool LeftStickClick { get; private set; }
        public static bool LeftStickClickDown { get; private set; }
        public static bool RightStickClick { get; private set; }
        public static bool RightStickClickDown { get; private set; }

        // ── Previous frame states for edge detection ──
        private static bool prevRightTrigger;
        private static bool prevLeftTrigger;
        private static bool prevRightB;
        private static bool prevLeftB;
        private static bool prevRightA;
        private static bool prevLeftA;
        private static bool prevLeftStickClick;
        private static bool prevRightStickClick;

        // Cached struct sizes
        private static uint analogSize;
        private static uint digitalSize;
        private static uint actionSetSize;

        /// <summary>
        /// Initialize the SteamVR action system. Call once after OpenVR is running.
        /// </summary>
        public static bool InitializeActions(string actionManifestPath)
        {
            if (OpenVR.Input == null)
            {
                Plugin.Log.LogError("VRInput: OpenVR.Input is null — action system unavailable");
                return false;
            }

            // Register the action manifest with SteamVR
            var err = OpenVR.Input.SetActionManifestPath(actionManifestPath);
            if (err != EVRInputError.None)
            {
                Plugin.Log.LogError($"VRInput: SetActionManifestPath failed: {err} (path: {actionManifestPath})");
                return false;
            }
            Plugin.Log.LogInfo($"VRInput: Action manifest loaded from {actionManifestPath}");

            bool ok = true;

            // Action handles
            ok &= GetHandle("/actions/default/in/Trigger", ref hTrigger);
            ok &= GetHandle("/actions/default/in/Grip", ref hGrip);
            ok &= GetHandle("/actions/default/in/Stick", ref hStick);
            ok &= GetHandle("/actions/default/in/ButtonA", ref hButtonA);
            ok &= GetHandle("/actions/default/in/ButtonB", ref hButtonB);
            ok &= GetHandle("/actions/default/in/StickClick", ref hStickClick);
            ok &= GetHandle("/actions/default/out/Haptic", ref hHaptic);

            // Input source handles (for per-hand queries)
            err = OpenVR.Input.GetInputSourceHandle("/user/hand/left", ref srcLeft);
            if (err != EVRInputError.None) { Plugin.Log.LogError($"VRInput: left hand source: {err}"); ok = false; }

            err = OpenVR.Input.GetInputSourceHandle("/user/hand/right", ref srcRight);
            if (err != EVRInputError.None) { Plugin.Log.LogError($"VRInput: right hand source: {err}"); ok = false; }

            // Action set handle
            err = OpenVR.Input.GetActionSetHandle("/actions/default", ref hActionSet);
            if (err != EVRInputError.None) { Plugin.Log.LogError($"VRInput: action set handle: {err}"); ok = false; }

            // Cache struct sizes
            analogSize = (uint)Marshal.SizeOf(typeof(InputAnalogActionData_t));
            digitalSize = (uint)Marshal.SizeOf(typeof(InputDigitalActionData_t));
            actionSetSize = (uint)Marshal.SizeOf(typeof(VRActiveActionSet_t));

            initialized = ok;

            Plugin.Log.LogInfo($"VRInput: Action system {(ok ? "ready" : "FAILED")} — " +
                               $"triggers={hTrigger != 0} grips={hGrip != 0} sticks={hStick != 0} " +
                               $"A={hButtonA != 0} B={hButtonB != 0} click={hStickClick != 0} haptic={hHaptic != 0}");
            return ok;
        }

        private static bool GetHandle(string actionName, ref ulong handle)
        {
            var err = OpenVR.Input.GetActionHandle(actionName, ref handle);
            if (err != EVRInputError.None)
            {
                Plugin.Log.LogError($"VRInput: GetActionHandle({actionName}): {err}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Read all controller input for this frame. Call once per frame.
        /// Parameters kept for call-site compatibility but are no longer used
        /// (the action system addresses hands by input source, not device index).
        /// </summary>
        public static void Update(uint leftIndex, uint rightIndex)
        {
            if (!initialized || OpenVR.Input == null) return;

            // Save previous frame states
            prevRightTrigger = RightTrigger;
            prevLeftTrigger = LeftTrigger;
            prevRightB = RightB;
            prevLeftB = LeftB;
            prevRightA = RightA;
            prevLeftA = LeftA;
            prevLeftStickClick = LeftStickClick;
            prevRightStickClick = RightStickClick;

            // Activate the action set for this frame
            var actionSets = new VRActiveActionSet_t[1];
            actionSets[0].ulActionSet = hActionSet;
            actionSets[0].ulRestrictedToDevice = 0;
            var err = OpenVR.Input.UpdateActionState(actionSets, actionSetSize);
            if (err != EVRInputError.None) return;

            // ── Analog: triggers ──
            RightTriggerAnalog = ReadAnalog1D(hTrigger, srcRight);
            RightTrigger = RightTriggerAnalog > 0.5f;
            LeftTriggerAnalog = ReadAnalog1D(hTrigger, srcLeft);
            LeftTrigger = LeftTriggerAnalog > 0.5f;

            // ── Analog: grips ──
            RightGripAnalog = ReadAnalog1D(hGrip, srcRight);
            RightGrip = RightGripAnalog > 0.5f;
            LeftGripAnalog = ReadAnalog1D(hGrip, srcLeft);
            LeftGrip = LeftGripAnalog > 0.5f;

            // ── Analog: thumbsticks ──
            ReadAnalog2D(hStick, srcLeft, out float lsx, out float lsy);
            LeftStickX = lsx;
            LeftStickY = lsy;
            ReadAnalog2D(hStick, srcRight, out float rsx, out float rsy);
            RightStickX = rsx;
            RightStickY = rsy;

            // ── Digital: face buttons ──
            RightA = ReadDigital(hButtonA, srcRight);
            LeftA = ReadDigital(hButtonA, srcLeft);
            RightB = ReadDigital(hButtonB, srcRight);
            LeftB = ReadDigital(hButtonB, srcLeft);

            // ── Digital: stick clicks ──
            RightStickClick = ReadDigital(hStickClick, srcRight);
            LeftStickClick = ReadDigital(hStickClick, srcLeft);

            // ── Edge detection ──
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

        // ── Analog readers ──

        private static float ReadAnalog1D(ulong action, ulong source)
        {
            InputAnalogActionData_t data = default;
            var err = OpenVR.Input.GetAnalogActionData(action, ref data, analogSize, source);
            if (err != EVRInputError.None || !data.bActive) return 0f;
            return data.x;
        }

        private static void ReadAnalog2D(ulong action, ulong source, out float x, out float y)
        {
            InputAnalogActionData_t data = default;
            var err = OpenVR.Input.GetAnalogActionData(action, ref data, analogSize, source);
            if (err != EVRInputError.None || !data.bActive) { x = 0f; y = 0f; return; }
            x = data.x;
            y = data.y;
        }

        // ── Digital reader ──

        private static bool ReadDigital(ulong action, ulong source)
        {
            InputDigitalActionData_t data = default;
            var err = OpenVR.Input.GetDigitalActionData(action, ref data, digitalSize, source);
            if (err != EVRInputError.None || !data.bActive) return false;
            return data.bState;
        }

        // ── Haptic output ──

        /// <summary>
        /// Trigger a haptic vibration pulse on the specified hand.
        /// </summary>
        /// <param name="leftHand">True for left hand, false for right hand.</param>
        /// <param name="durationSeconds">Duration in seconds.</param>
        /// <param name="frequency">Vibration frequency in Hz (0 = default).</param>
        /// <param name="amplitude">Vibration strength 0.0–1.0.</param>
        public static void TriggerHaptic(bool leftHand, float durationSeconds, float frequency, float amplitude)
        {
            if (!initialized || OpenVR.Input == null) return;
            ulong source = leftHand ? srcLeft : srcRight;
            OpenVR.Input.TriggerHapticVibrationAction(hHaptic, 0f, durationSeconds, frequency, amplitude, source);
        }
    }
}

using HarmonyLib;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;

namespace RavenfieldVRMod
{
    [HarmonyPatch(typeof(MouseLook), "LookRotation")]
    static class MouseLookPatch
    {
        static bool Prefix() { return !VRManager.IsVRActive; }
    }

    [HarmonyPatch(typeof(FpsActorController), "Start")]
    static class DisablePlayerFpParentPatch
    {
        static void Postfix(FpsActorController __instance)
        {
            if (!VRManager.IsVRActive) return;
            __instance.FirstPersonCamera();
            if (__instance.fpParent != null)
            {
                __instance.fpParent.enabled = false;
                Plugin.Log.LogInfo("VR: Disabled PlayerFpParent.");
            }
        }
    }

    [HarmonyPatch(typeof(Input), "get_anyKeyDown")]
    static class InputAnyKeyDownPatch
    {
        static void Postfix(ref bool __result)
        {
            if (!VRManager.IsVRActive || __result) return;
            if (VRInput.RightTriggerDown || VRInput.LeftTriggerDown ||
                VRInput.RightADown || VRInput.LeftADown ||
                VRInput.RightBDown || VRInput.LeftBDown)
                __result = true;
        }
    }

    // Pause menu → WorldSpace
    [HarmonyPatch(typeof(IngameMenuUi), "Show")]
    static class IngameMenuUiShowPatch
    {
        static void Postfix()
        {
            if (!VRManager.IsVRActive || IngameMenuUi.instance == null) return;
            VRCanvasHelper.ConvertCanvasToWorldSpace(IngameMenuUi.instance.GetComponent<Canvas>());
        }
    }

    // Fix: revert pause menu canvas back to ScreenSpaceOverlay (invisible)
    // so it doesn't block raycasts/shooting as an invisible WorldSpace panel
    [HarmonyPatch(typeof(IngameMenuUi), "Hide")]
    static class IngameMenuUiHidePatch
    {
        static void Postfix()
        {
            if (!VRManager.IsVRActive) return;

            // Revert canvas to ScreenSpaceOverlay so it's truly hidden
            // (WorldSpace canvas with enabled=false still blocks raycasts)
            if (IngameMenuUi.instance != null)
            {
                Canvas canvas = IngameMenuUi.instance.GetComponent<Canvas>();
                if (canvas != null)
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            // Also revert Options canvas if it was shown
            if (Options.instance != null)
            {
                Canvas optCanvas = Options.instance.GetComponent<Canvas>();
                if (optCanvas != null)
                    optCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            VRControllers.IsOptionsOpen = false;
            VROptionsUI.HideVRSettingsPanel();

            // Force unpause
            Time.timeScale = 1f;
            MouseLook.paused = false;
            Plugin.Log.LogInfo("VR: Menu closed, canvases reverted, force unpaused.");
        }
    }

    // Map (StrategyUi) → body-tracked WorldSpace
    [HarmonyPatch(typeof(StrategyUi), "Show")]
    static class StrategyUiShowPatch
    {
        static void Postfix()
        {
            VRControllers.IsMapOpen = true;
            if (!VRManager.IsVRActive || StrategyUi.instance == null) return;
            Canvas canvas = StrategyUi.instance.GetComponent<Canvas>();
            if (canvas == null)
                canvas = StrategyUi.instance.GetComponentInChildren<Canvas>();
            if (canvas == null) return;

            VRCanvasHelper.ConvertCanvasToBodyTracked(canvas, 2.5f);
            Plugin.Log.LogInfo("VR: Map body-tracked.");
        }
    }

    [HarmonyPatch(typeof(StrategyUi), "Hide")]
    static class StrategyUiHidePatch
    {
        static void Postfix()
        {
            VRControllers.IsMapOpen = false;
            if (!VRManager.IsVRActive || StrategyUi.instance == null) return;
            Canvas canvas = StrategyUi.instance.GetComponent<Canvas>();
            if (canvas == null)
                canvas = StrategyUi.instance.GetComponentInChildren<Canvas>();
            if (canvas != null)
            {
                VRCanvasHelper.StopBodyTracking(canvas);
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
        }
    }

    // Loadout/weapon selection → body-tracked WorldSpace
    [HarmonyPatch(typeof(LoadoutUi), "Show")]
    static class LoadoutUiShowPatch
    {
        static void Postfix()
        {
            if (!VRManager.IsVRActive || LoadoutUi.instance == null) return;
            Canvas canvas = LoadoutUi.instance.GetComponent<Canvas>();
            if (canvas == null)
                canvas = LoadoutUi.instance.GetComponentInChildren<Canvas>();
            if (canvas == null) return;

            VRCanvasHelper.ConvertCanvasToBodyTracked(canvas, 3f);
            Plugin.Log.LogInfo("VR: Loadout body-tracked.");
        }
    }

    [HarmonyPatch(typeof(LoadoutUi), "Hide")]
    static class LoadoutUiHidePatch
    {
        static void Postfix()
        {
            if (!VRManager.IsVRActive || LoadoutUi.instance == null) return;
            Canvas canvas = LoadoutUi.instance.GetComponent<Canvas>();
            if (canvas == null)
                canvas = LoadoutUi.instance.GetComponentInChildren<Canvas>();
            if (canvas != null)
            {
                VRCanvasHelper.StopBodyTracking(canvas);
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
        }
    }

    [HarmonyPatch(typeof(Options), "Show")]
    static class OptionsShowPatch
    {
        static void Postfix()
        {
            VRControllers.IsOptionsOpen = true;
            VROptionsUI.RefreshToggleState();
            if (!VRManager.IsVRActive || Options.instance == null) return;

            // Don't convert Options to WorldSpace — tab content doesn't render
            // and the dark canvas panel blocks raycasts to the VR settings panel.
            // Keep Options as ScreenSpaceOverlay (invisible in VR) and show
            // the standalone VR settings panel instead.
            VROptionsUI.ShowVRSettingsPanel();
        }
    }

    [HarmonyPatch(typeof(Options), "Hide")]
    static class OptionsHidePatch
    {
        static void Postfix()
        {
            VRControllers.IsOptionsOpen = false;
            VROptionsUI.HideVRSettingsPanel();
        }
    }

    // ========================================================
    // VR INPUT
    //
    // Right trigger = Fire
    // Right grip    = Sprint
    // Right B       = Reload
    // Right A       = Use (enter vehicles)
    // Right stick click = Open loadout
    // Left trigger  = Jump
    // Left grip     = Two-handed weapon grip (NO game binding — handled in VRControllers)
    // Left B        = NextWeapon
    // Left A        = Map (SquadLeaderKit)
    // Left stick click = Pause menu
    // Left stick up/down = weapon scroll (NextWeapon/PreviousWeapon via Y axis)
    // ========================================================
    [HarmonyPatch(typeof(SteelInput), "GetButton")]
    static class SteelInputGetButtonPatch
    {
        static void Postfix(SteelInput.KeyBinds input, ref bool __result)
        {
            if (!VRManager.IsVRActive) return;
            bool lh = VRManager.LeftHanded;
            switch (input)
            {
                case SteelInput.KeyBinds.Fire: if (lh ? VRInput.LeftTrigger : VRInput.RightTrigger) __result = true; break;
                case SteelInput.KeyBinds.Reload: if (lh ? VRInput.LeftB : VRInput.RightB) __result = true; break;
                case SteelInput.KeyBinds.Jump: if (lh ? VRInput.RightTrigger : VRInput.LeftTrigger) __result = true; break;
                case SteelInput.KeyBinds.Sprint: if (lh ? VRInput.LeftGrip : VRInput.RightGrip) __result = true; break;
                case SteelInput.KeyBinds.Use: if (lh ? VRInput.LeftA : VRInput.RightA) __result = true; break;
                case SteelInput.KeyBinds.SquadLeaderKit: if (lh ? VRInput.LeftStickClick : VRInput.RightStickClick) __result = true; break;
                case SteelInput.KeyBinds.OpenLoadout: if (lh ? VRInput.RightA : VRInput.LeftA) __result = true; break;
                case SteelInput.KeyBinds.TogglePauseMenu: if (lh ? VRInput.RightStickClick : VRInput.LeftStickClick) __result = true; break;
                case SteelInput.KeyBinds.Countermeasures: if (lh ? VRInput.RightB : VRInput.LeftB) __result = true; break;
                case SteelInput.KeyBinds.AutoHover: if (lh ? VRInput.RightA : VRInput.LeftA) __result = true; break;
                case SteelInput.KeyBinds.NextWeapon: if (lh ? VRInput.RightB : VRInput.LeftB) __result = true; break;
                case SteelInput.KeyBinds.Seat1: if (lh ? VRInput.RightA : VRInput.LeftA) __result = true; break;
                case SteelInput.KeyBinds.Seat2: if (lh ? VRInput.RightB : VRInput.LeftB) __result = true; break;
            }
        }
    }

    [HarmonyPatch(typeof(SteelInput), "GetButtonDown")]
    static class SteelInputGetButtonDownPatch
    {
        static void Postfix(SteelInput.KeyBinds input, ref bool __result)
        {
            if (!VRManager.IsVRActive) return;
            bool lh = VRManager.LeftHanded;
            switch (input)
            {
                case SteelInput.KeyBinds.Fire: if (lh ? VRInput.LeftTriggerDown : VRInput.RightTriggerDown) __result = true; break;
                case SteelInput.KeyBinds.Reload: if (lh ? VRInput.LeftBDown : VRInput.RightBDown) __result = true; break;
                case SteelInput.KeyBinds.Jump: if (lh ? VRInput.RightTriggerDown : VRInput.LeftTriggerDown) __result = true; break;
                case SteelInput.KeyBinds.Use: if (lh ? VRInput.LeftADown : VRInput.RightADown) __result = true; break;
                case SteelInput.KeyBinds.SquadLeaderKit: if (lh ? VRInput.LeftStickClickDown : VRInput.RightStickClickDown) __result = true; break;
                case SteelInput.KeyBinds.OpenLoadout: if (lh ? VRInput.RightADown : VRInput.LeftADown) __result = true; break;
                case SteelInput.KeyBinds.TogglePauseMenu: if (lh ? VRInput.RightStickClickDown : VRInput.LeftStickClickDown) __result = true; break;
                case SteelInput.KeyBinds.NextWeapon: if (lh ? VRInput.RightBDown : VRInput.LeftBDown) __result = true; break;
            }
        }
    }

    [HarmonyPatch(typeof(SteelInput), "GetButtonUp")]
    static class SteelInputGetButtonUpPatch
    {
        static void Postfix(SteelInput.KeyBinds input, ref bool __result)
        {
            if (!VRManager.IsVRActive) return;
            if (input == SteelInput.KeyBinds.Fire && (VRManager.LeftHanded ? VRInput.LeftTriggerUp : VRInput.RightTriggerUp)) __result = true;
        }
    }

    [HarmonyPatch(typeof(SteelInput), "GetAxis")]
    static class SteelInputGetAxisPatch
    {
        static void Postfix(SteelInput.KeyBinds input, ref float __result)
        {
            if (!VRManager.IsVRActive) return;

            // Left-handed: movement on right stick, turn on left; right-handed: vice versa
            bool lh = VRManager.LeftHanded;
            float lx = lh ? VRInput.RightStickX : VRInput.LeftStickX;
            float ly = lh ? VRInput.RightStickY : VRInput.LeftStickY;
            float rx = lh ? VRInput.LeftStickX : VRInput.RightStickX;

            switch (input)
            {
                case SteelInput.KeyBinds.Horizontal:
                case SteelInput.KeyBinds.Vertical:
                    if (Mathf.Abs(lx) > 0.1f || Mathf.Abs(ly) > 0.1f)
                    {
                        float headYaw = 0f;
                        float bodyYaw = VRCameraManager.PlayerYaw;
                        Camera cam = Camera.main;
                        if (cam == null && FpsActorController.instance != null)
                            cam = FpsActorController.instance.GetActiveCamera();
                        if (cam != null) headYaw = cam.transform.eulerAngles.y;

                        float delta = (headYaw - bodyYaw) * Mathf.Deg2Rad;
                        float cos = Mathf.Cos(delta);
                        float sin = Mathf.Sin(delta);
                        float newStrafe = lx * cos + ly * sin;
                        float newForward = -lx * sin + ly * cos;

                        if (input == SteelInput.KeyBinds.Horizontal) __result = -newStrafe;
                        if (input == SteelInput.KeyBinds.Vertical) __result = newForward;
                    }
                    break;

                case SteelInput.KeyBinds.CarSteer:
                    if (Mathf.Abs(lx) > 0.1f) __result = -lx; break;
                case SteelInput.KeyBinds.CarThrottle:
                    if (Mathf.Abs(ly) > 0.1f) __result = ly; break;
                case SteelInput.KeyBinds.HeliRoll:
                    if (Mathf.Abs(lx) > 0.1f) __result = lx; break;
                case SteelInput.KeyBinds.HeliPitch:
                    if (Mathf.Abs(ly) > 0.1f) __result = ly; break;
                case SteelInput.KeyBinds.HeliYaw:
                    if (Mathf.Abs(rx) > 0.1f) __result = rx; break;
                case SteelInput.KeyBinds.HeliThrottle:
                    float ht = VRInput.RightTriggerAnalog - VRInput.LeftTriggerAnalog;
                    if (Mathf.Abs(ht) > 0.1f) __result = ht; break;
                case SteelInput.KeyBinds.PlaneRoll:
                    if (Mathf.Abs(lx) > 0.1f) __result = lx; break;
                case SteelInput.KeyBinds.PlanePitch:
                    if (Mathf.Abs(ly) > 0.1f) __result = ly; break;
                case SteelInput.KeyBinds.PlaneYaw:
                    if (Mathf.Abs(rx) > 0.1f) __result = rx; break;
                case SteelInput.KeyBinds.PlaneThrottle:
                    float pt = VRInput.RightTriggerAnalog - VRInput.LeftTriggerAnalog;
                    if (Mathf.Abs(pt) > 0.1f) __result = pt; break;

                // Turret aiming: joystick only, slower sensitivity
                // Head is free to look around independently of turret direction
                case SteelInput.KeyBinds.AimX:
                    if (Mathf.Abs(rx) > 0.1f) __result = -rx * 0.15f; break;
                case SteelInput.KeyBinds.AimY:
                    float ry = lh ? VRInput.LeftStickY : VRInput.RightStickY;
                    if (Mathf.Abs(ry) > 0.1f) __result = -ry * 0.15f; break;
            }
        }
    }

    [HarmonyPatch(typeof(Options), "Awake")]
    static class OptionsAwakePatch
    {
        static void Postfix(Options __instance) { VROptionsUI.CreateOptionsToggle(__instance); }
    }
    [HarmonyPatch(typeof(MainMenu), "Awake")]
    static class MainMenuAwakePatch
    {
        static void Postfix(MainMenu __instance)
        {
            VROptionsUI.CreateMainMenuButton(__instance);
            VROptionsUI.CreateStatusOverlay();
        }
    }
    [HarmonyPatch(typeof(PlayerFpParent), "SetupHorizontalFov")]
    static class PlayerFpParentFovPatch
    {
        static bool Prefix() { return !VRManager.IsVRActive; }
    }
    [HarmonyPatch(typeof(PlayerFpParent), "SetAimFov")]
    static class PlayerFpParentAimFovPatch
    {
        static bool Prefix() { return !VRManager.IsVRActive; }
    }
    [HarmonyPatch(typeof(GameManager), "Update")]
    static class GameManagerUpdatePatch
    {
        private static int fc;
        static void Postfix()
        {
            if (++fc % 30 == 0) VROptionsUI.UpdateStatusOverlay();
            // One-time canvas dump during gameplay
            if (fc == 300 && GameManager.IsIngame())
            {
                Plugin.Log.LogInfo("=== CANVAS DUMP (gameplay) ===");
                foreach (var c in Object.FindObjectsOfType<Canvas>())
                {
                    Plugin.Log.LogInfo($"  [{c.name}] mode={c.renderMode} isRoot={c.isRootCanvas} enabled={c.enabled} active={c.gameObject.activeInHierarchy} parent={c.transform.parent?.name ?? "ROOT"}");
                }
                // Check KillIndicatorUi
                if (KillIndicatorUi.instance != null)
                {
                    Plugin.Log.LogInfo($"  KillIndicatorUi: exists, go={KillIndicatorUi.instance.gameObject.name}");
                    var kc = KillIndicatorUi.instance.GetComponent<Canvas>();
                    if (kc != null) Plugin.Log.LogInfo($"    canvas: mode={kc.renderMode} enabled={kc.enabled}");
                    else Plugin.Log.LogInfo("    NO Canvas component on KillIndicatorUi");
                    var kcc = KillIndicatorUi.instance.GetComponentInChildren<Canvas>();
                    if (kcc != null && kcc != kc) Plugin.Log.LogInfo($"    child canvas: {kcc.name} mode={kcc.renderMode} enabled={kcc.enabled}");
                }
                else Plugin.Log.LogInfo("  KillIndicatorUi.instance is NULL");
                Plugin.Log.LogInfo("=== END CANVAS DUMP ===");
            }
        }
    }
}

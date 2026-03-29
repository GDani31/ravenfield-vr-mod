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

    // Loadout/weapon selection → body-tracked WorldSpace.
    // Canvas converts ONCE and stays WorldSpace permanently.
    // The game uses uiCanvas.enabled for visibility — no renderMode cycling.
    // renderMode cycling caused unfixable progressive anchoredPosition drift.
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

            if (canvas.renderMode != RenderMode.WorldSpace)
            {
                // First-ever open: one-time conversion.
                // Save ALL child localPositions BEFORE conversion — Unity
                // recalculates positions to preserve world coordinates during
                // renderMode change. Using localPosition (not anchoredPosition)
                // because anchors/pivot may also change during conversion.
                var rects = canvas.GetComponentsInChildren<RectTransform>(true);
                var savedPos = new Vector3[rects.Length];
                var savedScale = new Vector3[rects.Length];
                for (int i = 0; i < rects.Length; i++)
                {
                    savedPos[i] = rects[i].localPosition;
                    savedScale[i] = rects[i].localScale;
                }

                // Move canvas to origin before conversion so Unity's world-position
                // preservation is consistent regardless of where the player is standing
                canvas.transform.position = Vector3.zero;
                canvas.transform.rotation = Quaternion.identity;

                VRCanvasHelper.ConvertCanvasToBodyTracked(canvas, 3f);

                // Disable CanvasScaler — in WorldSpace mode it continuously
                // recalculates layout and overrides our position restores.
                var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler != null) scaler.enabled = false;

                // Restore all child local positions (skip index 0 = canvas itself,
                // which is now managed by body tracking)
                for (int i = 1; i < rects.Length; i++)
                {
                    if (rects[i] != null)
                    {
                        rects[i].localPosition = savedPos[i];
                        rects[i].localScale = savedScale[i];
                    }
                }

                // Also restore after a delay to catch any deferred recalculation
                Plugin.Instance.StartCoroutine(DelayedPositionRestore(rects, savedPos, savedScale));

                Plugin.Log.LogInfo($"VR: Loadout → WorldSpace, restored {rects.Length} positions.");
            }
            else
            {
                // Re-open: just refresh camera + body tracking
                Camera cam = Camera.main;
                if (cam == null && FpsActorController.instance != null)
                    cam = FpsActorController.instance.GetActiveCamera();
                if (cam == null)
                    foreach (var c in Camera.allCameras)
                        if (c.isActiveAndEnabled) { cam = c; break; }
                if (cam != null) canvas.worldCamera = cam;
                var gr = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                if (gr != null) gr.enabled = true;
                var tracker = canvas.GetComponent<VRBodyTrackedCanvas>();
                if (tracker != null) { tracker.distance = 3f; tracker.enabled = true; }
                Plugin.Log.LogInfo("VR: Loadout re-opened.");
            }
        }

        private static System.Collections.IEnumerator DelayedPositionRestore(
            RectTransform[] rects, Vector3[] savedPos, Vector3[] savedScale)
        {
            yield return null;
            for (int i = 1; i < rects.Length; i++)
                if (rects[i] != null) { rects[i].localPosition = savedPos[i]; rects[i].localScale = savedScale[i]; }
            yield return null;
            for (int i = 1; i < rects.Length; i++)
                if (rects[i] != null) { rects[i].localPosition = savedPos[i]; rects[i].localScale = savedScale[i]; }
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
                // Keep WorldSpace — game's HideCanvas sets canvas.enabled=false
                var gr = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                if (gr != null) gr.enabled = false;
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

            // Hide the game's Options canvas entirely — ScreenSpaceOverlay renders
            // as a flat duplicate on top of everything in VR. Disable the root canvas
            // and all child tab canvases so nothing from the original Options shows.
            Canvas optCanvas = Options.instance.GetComponent<Canvas>();
            if (optCanvas != null)
                optCanvas.enabled = false;
            foreach (var cc in Options.instance.GetComponentsInChildren<Canvas>(true))
                cc.enabled = false;

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
                case SteelInput.KeyBinds.Reload:
                    if (VRReload.Enabled)
                    {
                        // Gesture reload: trigger from gesture completion OR fallback for melee/thrown
                        if (VRReload.TriggerReload || VRReload.FallbackButtonReload) __result = true;
                    }
                    else
                    {
                        // Classic button reload
                        if (lh ? VRInput.LeftB : VRInput.RightB) __result = true;
                    }
                    break;
                case SteelInput.KeyBinds.Jump:
                    // Suppress jump while VR reload active (off-hand trigger used for grab)
                    if (!VRReload.SuppressOffhandTrigger)
                    {
                        if (lh ? VRInput.RightTrigger : VRInput.LeftTrigger) __result = true;
                    }
                    break;
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
                case SteelInput.KeyBinds.Reload:
                    if (VRReload.Enabled)
                    {
                        if (VRReload.TriggerReload || VRReload.FallbackButtonReload) __result = true;
                    }
                    else
                    {
                        if (lh ? VRInput.LeftBDown : VRInput.RightBDown) __result = true;
                    }
                    break;
                case SteelInput.KeyBinds.Jump:
                    if (!VRReload.SuppressOffhandTrigger)
                    {
                        if (lh ? VRInput.RightTriggerDown : VRInput.LeftTriggerDown) __result = true;
                    }
                    break;
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
                    // Block movement when loadout is open (stick used for UI scrolling)
                    if (LoadoutUi.IsOpen()) { __result = 0f; break; }
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
                    if (Mathf.Abs(lx) > 0.1f) __result = -lx; break;
                case SteelInput.KeyBinds.HeliPitch:
                    if (Mathf.Abs(ly) > 0.1f) __result = ly; break;
                case SteelInput.KeyBinds.HeliYaw:
                    if (Mathf.Abs(rx) > 0.1f) __result = -rx; break;
                case SteelInput.KeyBinds.HeliThrottle:
                    float ht = VRInput.RightGripAnalog - VRInput.LeftGripAnalog;
                    if (Mathf.Abs(ht) > 0.1f) __result = ht; break;
                case SteelInput.KeyBinds.PlaneRoll:
                    if (Mathf.Abs(lx) > 0.1f) __result = -lx; break;
                case SteelInput.KeyBinds.PlanePitch:
                    if (Mathf.Abs(ly) > 0.1f) __result = ly; break;
                case SteelInput.KeyBinds.PlaneYaw:
                    if (Mathf.Abs(rx) > 0.1f) __result = -rx; break;
                case SteelInput.KeyBinds.PlaneThrottle:
                    float pt = VRInput.RightGripAnalog - VRInput.LeftGripAnalog;
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

    // Save HMD rotation before FpsActorController.LateUpdate (which overrides it
    // for turret/vehicle cameras), then restore it after. Application.onBeforeRender
    // provides the final guarantee — it re-applies HMD pose after ALL LateUpdates.
    [HarmonyPatch(typeof(FpsActorController), "LateUpdate")]
    static class FpsActorControllerLateUpdatePatch
    {
        private static Quaternion savedLocalRot;
        private static Vector3 savedLocalPos;
        private static bool hasSaved;

        static void Prefix(FpsActorController __instance)
        {
            if (!VRManager.IsVRActive) return;
            Camera cam = __instance.GetActiveCamera();
            if (cam != null)
            {
                savedLocalRot = cam.transform.localRotation;
                savedLocalPos = cam.transform.localPosition;
                hasSaved = true;
            }
        }

        static void Postfix(FpsActorController __instance)
        {
            if (!VRManager.IsVRActive || !hasSaved) return;
            hasSaved = false;
            Camera cam = __instance.GetActiveCamera();
            if (cam != null)
            {
                // Save the game's aim direction (turret/vehicle) BEFORE restoring
                // HMD rotation — used for the world-space crosshair marker
                float angleDiff = Quaternion.Angle(cam.transform.localRotation, savedLocalRot);
                if (angleDiff > 0.5f)
                {
                    VRCameraManager.GameAimWorldRotation = cam.transform.rotation;
                    VRCameraManager.GameOverrodeCamera = true;
                }

                // Restore the HMD-driven rotation/position that was set in Update.
                // Application.onBeforeRender will re-apply HMD pose as the absolute
                // last step before rendering, catching any overrides we miss here.
                cam.transform.localRotation = savedLocalRot;
                cam.transform.localPosition = savedLocalPos;
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
    // Block PlayerFpParent.LateUpdate — it sets camera rotation/position
    // for weapon bob, lean, recoil etc. which overrides HMD head tracking
    [HarmonyPatch(typeof(PlayerFpParent), "LateUpdate")]
    static class PlayerFpParentLateUpdatePatch
    {
        static bool Prefix() { return !VRManager.IsVRActive; }
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
    // Block sprint "tuck" animation — Weapon.Update sets animator "tuck" = true
    // when sprinting, which lowers the weapon. In VR the weapon is held by
    // controllers so the animation fights the hand position.
    [HarmonyPatch(typeof(Weapon), "Update")]
    static class WeaponTuckPatch
    {
        static void Postfix(Weapon __instance)
        {
            if (!VRManager.IsVRActive) return;
            if (__instance.animator != null && __instance.UserIsPlayer())
                __instance.animator.SetBool(Weapon.TUCK_PARAMETER_HASH, false);
        }
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

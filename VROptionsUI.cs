using UnityEngine;
using UnityEngine.UI;

namespace RavenfieldVRMod
{
    /// <summary>
    /// Creates and manages the VR toggle UI elements:
    /// 1. A toggle in the Options > Video tab
    /// 2. A quick-toggle button on the main menu
    /// 3. A VR status indicator overlay
    /// </summary>
    public static class VROptionsUI
    {
        private static Toggle vrToggle;
        private static GameObject vrToggleObj;
        private static Toggle snapTurnToggle;
        private static Toggle leftHandedToggle;
        private static Text snapAngleText;
        private static Text fovText;
        private static GameObject mainMenuVRButton;
        private static Text mainMenuVRButtonText;
        private static GameObject vrStatusOverlay;

        private static readonly int[] snapAngles = { 15, 30, 45, 60, 90 };
        private static readonly int[] fovValues = { 15, 20, 25, 30, 40, 50, 60 };
        private static Text vrStatusText;

        /// <summary>
        /// Injects the VR toggle into the Options video tab.
        /// </summary>
        public static void CreateOptionsToggle(Options options)
        {
            if (vrToggleObj != null)
                return;

            Canvas videoCanvas = options.videoOptions;
            if (videoCanvas == null)
            {
                Plugin.Log.LogError("Could not find video options canvas!");
                return;
            }

            // Find an existing toggle to clone for consistent styling
            OptionToggle existingToggle = videoCanvas.GetComponentInChildren<OptionToggle>();
            if (existingToggle == null)
            {
                Plugin.Log.LogWarning("No existing toggle in video options.");
                return;
            }

            // Clone it
            vrToggleObj = Object.Instantiate(existingToggle.gameObject, existingToggle.transform.parent);
            vrToggleObj.name = "VR Mode Toggle";

            // Remove the game's OptionToggle component (uses enum we can't extend)
            Object.Destroy(vrToggleObj.GetComponent<OptionToggle>());

            vrToggleObj.transform.SetAsLastSibling();

            // Update label
            Text label = vrToggleObj.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = "VR Mode";
            }

            // Replace the Toggle event entirely — kills serialized persistent listeners
            vrToggle = vrToggleObj.GetComponentInChildren<Toggle>();
            if (vrToggle != null)
            {
                vrToggle.onValueChanged = new Toggle.ToggleEvent();
                vrToggle.isOn = VRManager.IsVREnabled;
                vrToggle.onValueChanged.AddListener(OnVRToggleChanged);
            }

            Plugin.Log.LogInfo("VR toggle added to Video Options.");

            // Additional VR settings
            CreateVRSettings(existingToggle.gameObject, existingToggle.transform.parent);
        }

        private static void CreateVRSettings(GameObject template, Transform parent)
        {
            // Snap Turn toggle
            var snapObj = Object.Instantiate(template, parent);
            snapObj.name = "VR Snap Turn";
            Object.Destroy(snapObj.GetComponent<OptionToggle>());
            snapObj.transform.SetAsLastSibling();
            var snapLabel = snapObj.GetComponentInChildren<Text>();
            if (snapLabel != null) snapLabel.text = "Snap Turn";
            snapTurnToggle = snapObj.GetComponentInChildren<Toggle>();
            if (snapTurnToggle != null)
            {
                snapTurnToggle.onValueChanged = new Toggle.ToggleEvent();
                snapTurnToggle.isOn = VRManager.TurnMode == 1;
                snapTurnToggle.onValueChanged.AddListener((val) => {
                    VRManager.TurnMode = val ? 1 : 0;
                });
            }

            // Snap Angle (cycles on click)
            var angleObj = Object.Instantiate(template, parent);
            angleObj.name = "VR Snap Angle";
            Object.Destroy(angleObj.GetComponent<OptionToggle>());
            angleObj.transform.SetAsLastSibling();
            snapAngleText = angleObj.GetComponentInChildren<Text>();
            UpdateSnapAngleText();
            var angleTrigger = angleObj.GetComponentInChildren<Toggle>();
            if (angleTrigger != null)
            {
                angleTrigger.onValueChanged = new Toggle.ToggleEvent();
                angleTrigger.onValueChanged.AddListener((_) => CycleSnapAngle());
            }

            // FOV (cycles on click)
            var fovObj = Object.Instantiate(template, parent);
            fovObj.name = "VR FOV";
            Object.Destroy(fovObj.GetComponent<OptionToggle>());
            fovObj.transform.SetAsLastSibling();
            fovText = fovObj.GetComponentInChildren<Text>();
            UpdateFovText();
            var fovTrigger = fovObj.GetComponentInChildren<Toggle>();
            if (fovTrigger != null)
            {
                fovTrigger.onValueChanged = new Toggle.ToggleEvent();
                fovTrigger.onValueChanged.AddListener((_) => CycleFov());
            }

            // Left Handed toggle
            var lhObj = Object.Instantiate(template, parent);
            lhObj.name = "VR Left Handed";
            Object.Destroy(lhObj.GetComponent<OptionToggle>());
            lhObj.transform.SetAsLastSibling();
            var lhLabel = lhObj.GetComponentInChildren<Text>();
            if (lhLabel != null) lhLabel.text = "Left Handed";
            leftHandedToggle = lhObj.GetComponentInChildren<Toggle>();
            if (leftHandedToggle != null)
            {
                leftHandedToggle.onValueChanged = new Toggle.ToggleEvent();
                leftHandedToggle.isOn = VRManager.LeftHanded;
                leftHandedToggle.onValueChanged.AddListener((val) => {
                    VRManager.LeftHanded = val;
                });
            }
        }

        private static void CycleSnapAngle()
        {
            int current = VRManager.SnapAngle;
            int nextIdx = 0;
            for (int i = 0; i < snapAngles.Length; i++)
            {
                if (snapAngles[i] == current) { nextIdx = (i + 1) % snapAngles.Length; break; }
            }
            VRManager.SnapAngle = snapAngles[nextIdx];
            UpdateSnapAngleText();
        }

        private static void CycleFov()
        {
            int current = VRManager.VRFieldOfView;
            int nextIdx = 0;
            for (int i = 0; i < fovValues.Length; i++)
            {
                if (fovValues[i] == current) { nextIdx = (i + 1) % fovValues.Length; break; }
            }
            VRManager.VRFieldOfView = fovValues[nextIdx];
            UpdateFovText();
        }

        private static void UpdateSnapAngleText()
        {
            if (snapAngleText != null)
                snapAngleText.text = $"Snap Angle: {VRManager.SnapAngle}\u00B0";
        }

        private static void UpdateFovText()
        {
            if (fovText != null)
                fovText.text = $"FOV: {VRManager.VRFieldOfView}";
        }

        /// <summary>
        /// Creates a VR quick-toggle button on the main menu.
        /// </summary>
        public static void CreateMainMenuButton(MainMenu mainMenu)
        {
            if (mainMenuVRButton != null)
                return;

            GameObject menuPage = mainMenu.mainMenu;
            if (menuPage == null)
            {
                Plugin.Log.LogError("Could not find main menu page!");
                return;
            }

            // Find an existing button to clone for style
            Button existingButton = menuPage.GetComponentInChildren<Button>();
            if (existingButton == null)
            {
                Plugin.Log.LogWarning("No button found to clone on main menu.");
                return;
            }

            mainMenuVRButton = Object.Instantiate(existingButton.gameObject, existingButton.transform.parent);
            mainMenuVRButton.name = "VR Toggle Button";
            mainMenuVRButton.transform.SetAsLastSibling();

            // Update text
            mainMenuVRButtonText = mainMenuVRButton.GetComponentInChildren<Text>();
            if (mainMenuVRButtonText != null)
            {
                mainMenuVRButtonText.text = GetMainMenuButtonText();
            }

            // CRITICAL: Replace the entire onClick event object.
            // RemoveAllListeners() does NOT remove serialized persistent listeners
            // baked into the prefab by Unity editor — that's why the old code
            // was navigating to gamemode selection.
            Button btn = mainMenuVRButton.GetComponent<Button>();
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(OnMainMenuVRButtonClicked);

            Plugin.Log.LogInfo("VR quick-toggle button added to main menu.");
        }

        /// <summary>
        /// Creates a persistent VR status overlay (top-right corner).
        /// </summary>
        public static void CreateStatusOverlay()
        {
            if (vrStatusOverlay != null)
                return;

            vrStatusOverlay = new GameObject("VR Status Overlay");
            Object.DontDestroyOnLoad(vrStatusOverlay);

            Canvas canvas = vrStatusOverlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;

            CanvasScaler scaler = vrStatusOverlay.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // No GraphicRaycaster — overlay must not block clicks
            GameObject textObj = new GameObject("VR Status Text");
            textObj.transform.SetParent(vrStatusOverlay.transform, false);

            vrStatusText = textObj.AddComponent<Text>();
            vrStatusText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            vrStatusText.fontSize = 18;
            vrStatusText.alignment = TextAnchor.UpperRight;
            vrStatusText.raycastTarget = false;

            Outline outline = textObj.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.8f);
            outline.effectDistance = new Vector2(1, -1);

            RectTransform rect = textObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-10, -10);
            rect.sizeDelta = new Vector2(400, 30);

            UpdateStatusOverlay();
        }

        public static void UpdateStatusOverlay()
        {
            if (vrStatusText == null)
                return;

            if (VRManager.IsTransitioning)
            {
                vrStatusText.text = "[VR: Starting...]";
                vrStatusText.color = new Color(1f, 1f, 0.2f, 0.8f);
            }
            else if (VRManager.IsVRActive)
            {
                vrStatusText.text = "[VR: Active]";
                vrStatusText.color = new Color(0.2f, 1f, 0.2f, 0.8f);
            }
            else if (VRManager.IsVREnabled)
            {
                vrStatusText.text = "[VR: Enabled - no headset]";
                vrStatusText.color = new Color(1f, 0.5f, 0.2f, 0.8f);
            }
            else
            {
                vrStatusText.text = "";
            }
        }

        public static void RefreshToggleState()
        {
            if (vrToggle != null)
            {
                vrToggle.onValueChanged = new Toggle.ToggleEvent();
                vrToggle.isOn = VRManager.IsVREnabled;
                vrToggle.onValueChanged.AddListener(OnVRToggleChanged);
            }
            UpdateMainMenuButtonText();
            UpdateStatusOverlay();
        }

        private static void OnVRToggleChanged(bool value)
        {
            VRManager.SetVREnabled(value);
            UpdateMainMenuButtonText();
            UpdateStatusOverlay();
        }

        private static void OnMainMenuVRButtonClicked()
        {
            VRManager.ToggleVR();
            UpdateMainMenuButtonText();
            UpdateStatusOverlay();

            // Sync options toggle
            if (vrToggle != null)
            {
                vrToggle.onValueChanged = new Toggle.ToggleEvent();
                vrToggle.isOn = VRManager.IsVREnabled;
                vrToggle.onValueChanged.AddListener(OnVRToggleChanged);
            }
        }

        private static void UpdateMainMenuButtonText()
        {
            if (mainMenuVRButtonText == null)
                return;
            mainMenuVRButtonText.text = GetMainMenuButtonText();
        }

        private static string GetMainMenuButtonText()
        {
            if (VRManager.IsTransitioning)
                return "VR MODE: Starting...";
            if (VRManager.IsVRActive)
                return "VR MODE: ON";
            return "VR MODE: OFF";
        }
    }
}

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

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
        private static readonly int[] fovValues = { 70, 80, 90, 100, 110 };
        private static Text vrStatusText;

        // Standalone VR settings panel (WorldSpace, overlays Options content area in VR)
        private static GameObject vrSettingsPanel;
        private static Text panelSnapTurnVal;
        private static Text panelAngleVal;
        private static Text panelFovVal;
        private static Text panelLeftHandVal;

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

            vrToggleObj.transform.SetAsFirstSibling();

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
            snapObj.transform.SetSiblingIndex(1);
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
            angleObj.transform.SetSiblingIndex(2);
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
            fovObj.transform.SetSiblingIndex(3);
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
            lhObj.transform.SetSiblingIndex(4);
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

        // ===== Standalone VR Settings Panel =====
        // Rendered as its own WorldSpace canvas so it's always visible in VR,
        // independent of the game's Options tab canvas system which breaks in WorldSpace.

        public static void ShowVRSettingsPanel()
        {
            if (!VRManager.IsVRActive) return;
            if (vrSettingsPanel == null)
                BuildVRSettingsPanel();
            vrSettingsPanel.SetActive(true);
            PositionSettingsPanel();
            RefreshPanelValues();
        }

        public static void HideVRSettingsPanel()
        {
            if (vrSettingsPanel != null)
                vrSettingsPanel.SetActive(false);
        }

        private static void BuildVRSettingsPanel()
        {
            vrSettingsPanel = new GameObject("VR Settings Panel");
            Object.DontDestroyOnLoad(vrSettingsPanel);

            var canvas = vrSettingsPanel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 50; // Above the Options panel
            vrSettingsPanel.AddComponent<GraphicRaycaster>();

            var panelRect = vrSettingsPanel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(400, 360);
            panelRect.localScale = Vector3.one * 0.002f;

            // Dark background
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(vrSettingsPanel.transform, false);
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Title
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(vrSettingsPanel.transform, false);
            var titleText = titleGO.AddComponent<Text>();
            titleText.text = "VR SETTINGS";
            titleText.font = font;
            titleText.fontSize = 24;
            titleText.color = new Color(1f, 0.85f, 0.3f);
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.raycastTarget = false;
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchoredPosition = new Vector2(0, 150);
            titleRect.sizeDelta = new Vector2(380, 40);

            // Setting rows
            panelSnapTurnVal = CreatePanelRow(vrSettingsPanel.transform, font, "Snap Turn", 90, () => {
                VRManager.TurnMode = VRManager.TurnMode == 0 ? 1 : 0;
                RefreshPanelValues();
            });
            panelAngleVal = CreatePanelRow(vrSettingsPanel.transform, font, "Snap Angle", 40, () => {
                CycleSnapAngle();
                RefreshPanelValues();
            });
            panelFovVal = CreatePanelRow(vrSettingsPanel.transform, font, "FOV", -10, () => {
                CycleFov();
                RefreshPanelValues();
            });
            panelLeftHandVal = CreatePanelRow(vrSettingsPanel.transform, font, "Left Handed", -60, () => {
                VRManager.LeftHanded = !VRManager.LeftHanded;
                RefreshPanelValues();
            });

            // Close button
            var closeGO = new GameObject("Close");
            closeGO.transform.SetParent(vrSettingsPanel.transform, false);
            var closeRect = closeGO.AddComponent<RectTransform>();
            closeRect.anchoredPosition = new Vector2(0, -130);
            closeRect.sizeDelta = new Vector2(160, 42);
            var closeImg = closeGO.AddComponent<Image>();
            closeImg.color = new Color(0.3f, 0.12f, 0.12f, 0.95f);
            var closeBtn = closeGO.AddComponent<Button>();
            var closeColors = closeBtn.colors;
            closeColors.normalColor = new Color(0.3f, 0.12f, 0.12f, 0.95f);
            closeColors.highlightedColor = new Color(0.5f, 0.2f, 0.2f);
            closeColors.pressedColor = new Color(0.6f, 0.3f, 0.3f);
            closeBtn.colors = closeColors;
            closeBtn.onClick.AddListener(() => {
                // Navigate back: MainMenu.GoBack on main menu, Options.Hide in-game
                var mm = Object.FindObjectOfType<MainMenu>();
                if (mm != null)
                    mm.GoBack();
                else
                    Options.Hide();
            });
            var closeLabelGO = new GameObject("Text");
            closeLabelGO.transform.SetParent(closeGO.transform, false);
            var closeTxt = closeLabelGO.AddComponent<Text>();
            closeTxt.text = "CLOSE";
            closeTxt.font = font;
            closeTxt.fontSize = 20;
            closeTxt.color = Color.white;
            closeTxt.alignment = TextAnchor.MiddleCenter;
            closeTxt.raycastTarget = false;
            var closeTxtRect = closeLabelGO.GetComponent<RectTransform>();
            closeTxtRect.anchorMin = Vector2.zero;
            closeTxtRect.anchorMax = Vector2.one;
            closeTxtRect.offsetMin = Vector2.zero;
            closeTxtRect.offsetMax = Vector2.zero;

            RefreshPanelValues();
            Plugin.Log.LogInfo("VR settings panel created.");
        }

        private static Text CreatePanelRow(Transform parent, Font font, string label, float yPos, UnityAction onClick)
        {
            var rowGO = new GameObject($"Row_{label}");
            rowGO.transform.SetParent(parent, false);
            var rowRect = rowGO.AddComponent<RectTransform>();
            rowRect.anchoredPosition = new Vector2(0, yPos);
            rowRect.sizeDelta = new Vector2(360, 42);

            var rowImg = rowGO.AddComponent<Image>();
            rowImg.color = new Color(0.15f, 0.15f, 0.22f, 0.9f);

            var btn = rowGO.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = new Color(0.15f, 0.15f, 0.22f, 0.9f);
            colors.highlightedColor = new Color(0.25f, 0.25f, 0.4f);
            colors.pressedColor = new Color(0.35f, 0.35f, 0.55f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            // Label (left)
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(rowGO.transform, false);
            var labelT = labelGO.AddComponent<Text>();
            labelT.text = label;
            labelT.font = font;
            labelT.fontSize = 20;
            labelT.color = Color.white;
            labelT.alignment = TextAnchor.MiddleLeft;
            labelT.raycastTarget = false;
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0, 0);
            labelRect.anchorMax = new Vector2(0.6f, 1);
            labelRect.offsetMin = new Vector2(20, 0);
            labelRect.offsetMax = Vector2.zero;

            // Value (right)
            var valueGO = new GameObject("Value");
            valueGO.transform.SetParent(rowGO.transform, false);
            var valueT = valueGO.AddComponent<Text>();
            valueT.font = font;
            valueT.fontSize = 20;
            valueT.color = new Color(0.4f, 0.9f, 1f);
            valueT.alignment = TextAnchor.MiddleRight;
            valueT.raycastTarget = false;
            var valueRect = valueGO.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.6f, 0);
            valueRect.anchorMax = new Vector2(1, 1);
            valueRect.offsetMin = Vector2.zero;
            valueRect.offsetMax = new Vector2(-20, 0);

            return valueT;
        }

        private static void PositionSettingsPanel()
        {
            if (vrSettingsPanel == null) return;
            Camera cam = null;
            if (FpsActorController.instance != null)
                cam = FpsActorController.instance.GetActiveCamera();
            if (cam == null) cam = Camera.main;
            if (cam == null)
            {
                foreach (var c in Camera.allCameras)
                    if (c.isActiveAndEnabled) { cam = c; break; }
            }
            if (cam == null) return;

            vrSettingsPanel.GetComponent<Canvas>().worldCamera = cam;
            var rect = vrSettingsPanel.GetComponent<RectTransform>();
            Vector3 forward = cam.transform.forward;
            forward.y = 0;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();
            // Slightly closer than the Options panel so it renders in front
            float dist = GameManager.IsIngame() ? 2.4f : 2.9f;
            Vector3 pos = cam.transform.position + forward * dist;
            pos.y = cam.transform.position.y;
            rect.position = pos;
            rect.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private static void RefreshPanelValues()
        {
            if (panelSnapTurnVal != null)
                panelSnapTurnVal.text = VRManager.TurnMode == 1 ? "ON" : "OFF";
            if (panelAngleVal != null)
                panelAngleVal.text = $"{VRManager.SnapAngle}\u00B0";
            if (panelFovVal != null)
                panelFovVal.text = $"{VRManager.VRFieldOfView}";
            if (panelLeftHandVal != null)
                panelLeftHandVal.text = VRManager.LeftHanded ? "ON" : "OFF";
        }
    }
}

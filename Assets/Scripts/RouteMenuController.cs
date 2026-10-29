using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Builds an always-available, world-space destination menu for the 360 route.
/// It supports XR ray/trigger UI interaction and a controller-button fallback.
/// </summary>
public class RouteMenuController : MonoBehaviour
{
    private static readonly string[] RouteNames =
    {
        "Auditorium Exit",
        "Cafeteria Entry from Lifts 4, 5, 6",
        "Cafeteria Entry from Lifts 7, 8, 9",
        "Inside Cafeteria",
        "Cafeteria Entry from seating area in right side",
        "Cafeteria Exit to seating area in right side",
        "Cafeteria Entry from seating area in left side",
        "Cafeteria Exit to seating area in right side",
    };

    [SerializeField] private RouteManager routeManager;

    private GameObject menuPanel;
    private Button[] routeButtons;
    private UnityEngine.XR.InputDevice leftController;
    private UnityEngine.XR.InputDevice rightController;
    private int selectedRoute;
    private int heldVerticalDirection;
    private bool heldMenuButton;

    private void Awake()
    {
        if (routeManager == null)
            routeManager = GetComponent<RouteManager>();
    }

    private void Start()
    {
        if (routeManager == null)
        {
            Debug.LogError("RouteMenuController: RouteManager is required.");
            enabled = false;
            return;
        }

        CreateXrUiSupport();
        CreateMenu();
        ShowMenu();
    }

    private void Update()
    {
        EnsureControllers();
        ReadMenuToggle();

        if (menuPanel != null && menuPanel.activeSelf)
            ReadMenuFallbackInput();
    }

    private void EnsureControllers()
    {
        if (!leftController.isValid)
            leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!rightController.isValid)
            rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    private void ReadMenuToggle()
    {
#if UNITY_EDITOR
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.mKey.wasPressedThisFrame)
        {
            if (menuPanel != null && menuPanel.activeSelf)
                HideMenu();
            else
                ShowMenu();
            return;
        }
#endif

        bool menuPressed = (leftController.isValid && leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool leftPressed) && leftPressed) ||
                           (rightController.isValid && rightController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool rightPressed) && rightPressed);

        if (!menuPressed)
        {
            heldMenuButton = false;
            return;
        }

        if (heldMenuButton)
            return;

        heldMenuButton = true;
        if (menuPanel.activeSelf)
            HideMenu();
        else
            ShowMenu();
    }

    private void ReadMenuFallbackInput()
    {
#if UNITY_EDITOR
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)
            {
                selectedRoute = (selectedRoute - 1 + RouteNames.Length) % RouteNames.Length;
                HighlightSelectedRoute();
            }
            else if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)
            {
                selectedRoute = (selectedRoute + 1) % RouteNames.Length;
                HighlightSelectedRoute();
            }

            if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            {
                SelectRoute(selectedRoute);
                return;
            }
        }
#endif

        Vector2 stick = Vector2.zero;
        if (rightController.isValid && rightController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 rStick) && rStick.sqrMagnitude > 0.2f)
        {
            stick = rStick;
        }
        else if (leftController.isValid && leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 lStick))
        {
            stick = lStick;
        }

        int direction = stick.y >= 0.65f ? -1 : stick.y <= -0.65f ? 1 : 0;
        if (direction == 0)
            heldVerticalDirection = 0;
        else if (direction != heldVerticalDirection)
        {
            heldVerticalDirection = direction;
            selectedRoute = (selectedRoute + direction + RouteNames.Length) % RouteNames.Length;
            HighlightSelectedRoute();
        }

        bool triggerPressed = (rightController.isValid && rightController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool rTrig) && rTrig) ||
                              (leftController.isValid && leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool lTrig) && lTrig);

        if (triggerPressed)
        {
            SelectRoute(selectedRoute);
        }
    }

    private void CreateXrUiSupport()
    {
        EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
            eventSystem = eventSystemObject.GetComponent<EventSystem>();
        }
        else
        {
            InputSystemUIInputModule standardModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (standardModule != null)
            {
                standardModule.enabled = false;
                Destroy(standardModule);
            }

            StandaloneInputModule legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (legacyModule != null)
            {
                legacyModule.enabled = false;
                Destroy(legacyModule);
            }

            if (eventSystem.GetComponent<XRUIInputModule>() == null)
                eventSystem.gameObject.AddComponent<XRUIInputModule>();
        }

        CreateRayInteractor("Left XR UI Ray", "{LeftHand}");
        CreateRayInteractor("Right XR UI Ray", "{RightHand}");
    }

    private void CreateRayInteractor(string objectName, string handUsage)
    {
        GameObject rayObject = new GameObject(objectName);

        Unity.XR.CoreUtils.XROrigin xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null && xrOrigin.CameraFloorOffsetObject != null)
            rayObject.transform.SetParent(xrOrigin.CameraFloorOffsetObject.transform, false);
        else if (Camera.main != null && Camera.main.transform.parent != null)
            rayObject.transform.SetParent(Camera.main.transform.parent, false);

        TrackedPoseDriver poseDriver = rayObject.AddComponent<TrackedPoseDriver>();
        poseDriver.positionInput = new InputActionProperty(CreateAction("Position", "<XRController>" + handUsage + "/devicePosition", "Vector3", InputActionType.PassThrough));
        poseDriver.rotationInput = new InputActionProperty(CreateAction("Rotation", "<XRController>" + handUsage + "/deviceRotation", "Quaternion", InputActionType.PassThrough));
        poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;

        XRRayInteractor rayInteractor = rayObject.AddComponent<XRRayInteractor>();
        rayInteractor.enableUIInteraction = true;

        InputAction selectAction = CreateAction("UI Press", "<XRController>" + handUsage + "/triggerPressed", "Button");
        rayInteractor.uiPressInput = new XRInputButtonReader("UI Press")
        {
            inputSourceMode = XRInputButtonReader.InputSourceMode.InputAction,
            inputActionPerformed = selectAction
        };
        rayInteractor.uiPressInput.EnableDirectActionIfModeUsed();

        rayInteractor.selectInput = new XRInputButtonReader("Select")
        {
            inputSourceMode = XRInputButtonReader.InputSourceMode.InputAction,
            inputActionPerformed = selectAction
        };
        rayInteractor.selectInput.EnableDirectActionIfModeUsed();
    }

    private static InputAction CreateAction(string name, string binding, string expectedControlType, InputActionType type = InputActionType.Button)
    {
        InputAction action = new InputAction(name, type, binding, expectedControlType: expectedControlType);
        action.Enable();
        return action;
    }

    private void CreateMenu()
    {
        Camera headCamera = Camera.main;
        if (headCamera == null)
        {
            Debug.LogError("RouteMenuController: Main Camera is required.");
            enabled = false;
            return;
        }

        GameObject canvasObject = new GameObject("VR Route Menu", typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = headCamera;
        canvasObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 20f;
        canvasObject.AddComponent<GraphicRaycaster>();
        canvasObject.AddComponent<TrackedDeviceGraphicRaycaster>().ignoreReversedGraphics = true;
        canvasObject.transform.SetParent(headCamera.transform, false);
        canvasObject.transform.localPosition = new Vector3(0f, -0.05f, 2.2f);
        canvasObject.transform.localRotation = Quaternion.identity;

        RectTransform root = canvasObject.GetComponent<RectTransform>();
        root.sizeDelta = new Vector2(1200f, 900f);
        root.localScale = Vector3.one * 0.001f;

        Image background = canvasObject.AddComponent<Image>();
        background.color = new Color(0.025f, 0.04f, 0.075f, 0.96f);

        AddText(canvasObject.transform, "Choose a destination", 54, new Vector2(0f, 370f), new Vector2(1050f, 100f), TextAnchor.MiddleCenter, Color.white);
        AddText(canvasObject.transform, "Point and press trigger, or use stick + trigger", 26, new Vector2(0f, 315f), new Vector2(1050f, 60f), TextAnchor.MiddleCenter, new Color(0.75f, 0.85f, 1f));

        routeButtons = new Button[RouteNames.Length];
        for (int i = 0; i < RouteNames.Length; i++)
        {
            int routeIndex = i;
            float y = 215f - i * 70f;
            routeButtons[i] = AddButton(canvasObject.transform, RouteNames[i], new Vector2(0f, y));
            routeButtons[i].onClick.AddListener(() => SelectRoute(routeIndex));
        }

        Button closeButton = AddButton(canvasObject.transform, "Close Menu", new Vector2(0f, -350f), new Vector2(330f, 60f));
        closeButton.onClick.AddListener(HideMenu);
        menuPanel = canvasObject;
        CreatePersistentMenuButton(headCamera);
        HighlightSelectedRoute();
    }

    private void CreatePersistentMenuButton(Camera headCamera)
    {
        GameObject canvasObject = new GameObject("VR Menu Button", typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = headCamera;
        canvasObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 20f;
        canvasObject.AddComponent<GraphicRaycaster>();
        canvasObject.AddComponent<TrackedDeviceGraphicRaycaster>().ignoreReversedGraphics = true;
        canvasObject.transform.SetParent(headCamera.transform, false);
        canvasObject.transform.localPosition = new Vector3(0f, -0.58f, 1.6f);
        canvasObject.transform.localRotation = Quaternion.identity;

        RectTransform root = canvasObject.GetComponent<RectTransform>();
        root.sizeDelta = new Vector2(260f, 76f);
        root.localScale = Vector3.one * 0.001f;

        Button menuButton = AddButton(canvasObject.transform, "Menu", Vector2.zero, root.sizeDelta);
        menuButton.onClick.AddListener(ShowMenu);
    }

    private Button AddButton(Transform parent, string label, Vector2 position, Vector2? size = null)
    {
        GameObject buttonObject = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size ?? new Vector2(1050f, 58f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.10f, 0.24f, 0.43f, 1f);
        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.55f, 0.85f, 1f, 1f);
        colors.pressedColor = new Color(0.35f, 0.7f, 1f, 1f);
        button.colors = colors;

        AddText(buttonObject.transform, label, 27, Vector2.zero, rect.sizeDelta, TextAnchor.MiddleCenter, Color.white);
        return button;
    }

    private static void AddText(Transform parent, string text, int fontSize, Vector2 position, Vector2 size, TextAnchor alignment, Color color)
    {
        GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Text label = textObject.GetComponent<Text>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = color;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
    }

    private void SelectRoute(int routeIndex)
    {
        routeManager.SelectRoute(routeIndex, true);
        HideMenu();
    }

    private void ShowMenu()
    {
        if (menuPanel == null)
            return;

        routeManager.SetControlsEnabled(false);
        menuPanel.SetActive(true);
        HighlightSelectedRoute();
    }

    private void HideMenu()
    {
        if (menuPanel == null)
            return;

        menuPanel.SetActive(false);
        routeManager.SetControlsEnabled(true);
    }

    private void HighlightSelectedRoute()
    {
        if (routeButtons == null)
            return;

        for (int i = 0; i < routeButtons.Length; i++)
        {
            Image image = routeButtons[i].GetComponent<Image>();
            image.color = i == selectedRoute
                ? new Color(0.08f, 0.55f, 0.95f, 1f)
                : new Color(0.10f, 0.24f, 0.43f, 1f);
        }
    }
}

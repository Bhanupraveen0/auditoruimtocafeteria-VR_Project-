using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;

[InitializeOnLoad]
public static class ProjectValidation
{
    private static readonly string[] ExpectedClipNames =
    {
        "GS010089",
        "GS010093",
        "GS010101",
        "GS010095",
        "GS010096",
        "GS010097",
        "GS010100",
        "GS010094"
    };

    private static readonly string[] ExpectedRouteLabels =
    {
        "Auditorium Exit",
        "Cafeteria Entry from Lifts 4, 5, 6",
        "Cafeteria Entry from Lifts 7, 8, 9",
        "Inside Cafeteria",
        "Cafeteria Entry from seating area in right side",
        "Cafeteria Exit to seating area in right side",
        "Cafeteria Entry from seating area in left side",
        "Cafeteria Exit to seating area in right side"
    };

    static ProjectValidation()
    {
        Debug.Log("[ProjectValidation] Assembly compilation completed cleanly.");
    }

    [MenuItem("Tools/Run Automated Project Verification")]
    public static void RunAllValidationTests()
    {
        Debug.Log("==================================================");
        Debug.Log("[ProjectValidation] STARTING VERIFICATION SUITE");
        Debug.Log("==================================================");

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError("[FAIL] Failed to open Assets/Scenes/SampleScene.unity");
            return;
        }
        Debug.Log("[PASS] Opened SampleScene.unity successfully.");

        // 1. Verify RouteManager
        var routeManager = UnityEngine.Object.FindFirstObjectByType<RouteManager>();
        if (routeManager == null)
        {
            Debug.LogError("[FAIL] RouteManager component missing from scene.");
            return;
        }
        Debug.Log("[PASS] Found RouteManager component in scene.");

        if (routeManager.videoPlayer == null)
        {
            Debug.LogError("[FAIL] RouteManager.videoPlayer is null.");
            return;
        }
        Debug.Log($"[PASS] RouteManager.videoPlayer assigned: {routeManager.videoPlayer.gameObject.name}");

        if (routeManager.routeClips == null || routeManager.routeClips.Length != 8)
        {
            Debug.LogError($"[FAIL] RouteManager.routeClips length is {routeManager.routeClips?.Length ?? 0}, expected 8.");
            return;
        }
        Debug.Log("[PASS] RouteManager has exactly 8 clips configured.");

        // Verify each clip name
        bool clipsValid = true;
        for (int i = 0; i < ExpectedClipNames.Length; i++)
        {
            VideoClip clip = routeManager.routeClips[i];
            if (clip == null)
            {
                Debug.LogError($"[FAIL] routeClips[{i}] is null!");
                clipsValid = false;
            }
            else if (clip.name != ExpectedClipNames[i])
            {
                Debug.LogError($"[FAIL] routeClips[{i}] name '{clip.name}' does not match expected '{ExpectedClipNames[i]}'");
                clipsValid = false;
            }
            else
            {
                Debug.Log($"[PASS] Destination {i + 1}: {clip.name} -> {ExpectedRouteLabels[i]}");
            }
        }
        if (!clipsValid) return;

        // 2. Verify RouteMenuController
        var menuController = UnityEngine.Object.FindFirstObjectByType<RouteMenuController>();
        if (menuController == null)
        {
            Debug.LogError("[FAIL] RouteMenuController missing from scene.");
            return;
        }
        Debug.Log("[PASS] Found RouteMenuController component in scene.");

        // 3. Verify XR Origin (VR)
        var xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
        if (xrOrigin == null)
        {
            Debug.LogError("[FAIL] XROrigin missing from scene.");
            return;
        }
        Debug.Log($"[PASS] Found XROrigin: {xrOrigin.gameObject.name}, Camera: {xrOrigin.Camera?.name ?? "null"}");

        // 4. Verify XRInteractionManager
        var interactionManager = UnityEngine.Object.FindFirstObjectByType<XRInteractionManager>();
        if (interactionManager == null)
        {
            Debug.LogError("[FAIL] XRInteractionManager missing from scene.");
            return;
        }
        Debug.Log("[PASS] Found XRInteractionManager in scene.");

        // 5. Verify EventSystem
        var eventSystem = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (eventSystem == null)
        {
            Debug.LogError("[FAIL] EventSystem missing from scene.");
            return;
        }
        Debug.Log("[PASS] Found EventSystem in scene.");

        // 6. Test RouteManager Next/Previous indexing logic
        int initialIndex = routeManager.currentClipIndex;
        routeManager.NextClip();
        int nextIndex = routeManager.currentClipIndex;
        if (nextIndex != (initialIndex + 1) % routeManager.routeClips.Length)
        {
            Debug.LogError($"[FAIL] NextClip() index expected {(initialIndex + 1) % 8}, got {nextIndex}");
            return;
        }
        routeManager.PreviousClip();
        if (routeManager.currentClipIndex != initialIndex)
        {
            Debug.LogError($"[FAIL] PreviousClip() index expected {initialIndex}, got {routeManager.currentClipIndex}");
            return;
        }
        Debug.Log("[PASS] RouteManager NextClip/PreviousClip indexing verified.");

        // 7. Test SelectRoute logic
        routeManager.SelectRoute(3, false);
        if (routeManager.currentClipIndex != 3)
        {
            Debug.LogError($"[FAIL] SelectRoute(3) index expected 3, got {routeManager.currentClipIndex}");
            return;
        }
        Debug.Log($"[PASS] SelectRoute(3) successfully selected '{routeManager.routeClips[3].name}' ({ExpectedRouteLabels[3]}).");

        // Restore initial clip
        routeManager.SelectRoute(initialIndex, false);

        // 8. Test SetControlsEnabled
        routeManager.SetControlsEnabled(false);
        if (routeManager.ControlsEnabled)
        {
            Debug.LogError("[FAIL] SetControlsEnabled(false) failed to disable controls.");
            return;
        }
        routeManager.SetControlsEnabled(true);
        if (!routeManager.ControlsEnabled)
        {
            Debug.LogError("[FAIL] SetControlsEnabled(true) failed to enable controls.");
            return;
        }
        Debug.Log("[PASS] RouteManager.SetControlsEnabled verified (suspends stick inputs while menu is open).");

        // 9. Verify PanoramaLook configuration & target resolution
        var panoramaLook = routeManager.GetComponent<PanoramaLook>();
        if (panoramaLook == null)
        {
            panoramaLook = routeManager.gameObject.AddComponent<PanoramaLook>();
        }
        panoramaLook.ResolveTargetTransform();

        if (panoramaLook.TargetTransform == null)
        {
            Debug.LogError("[FAIL] PanoramaLook TargetTransform failed to resolve.");
            return;
        }

        // CRITICAL CHECK: Verify PanoramaLook does NOT target the Main Camera directly
        var mainCamera = Camera.main;
        if (mainCamera != null && panoramaLook.TargetTransform == mainCamera.transform)
        {
            Debug.LogError("[FAIL] PanoramaLook TargetTransform is Main Camera directly! Must rotate camera-parent (Camera Offset) to preserve head tracking.");
            return;
        }
        Debug.Log($"[PASS] PanoramaLook TargetTransform resolved correctly to '{panoramaLook.TargetTransform.name}' (camera-parent/offset), preserving head tracking.");

        // 10. Test Horizontal Rotation (Yaw): continuous, smooth, unconstrained 360 rotation
        panoramaLook.SetRotation(0f, 0f);
        // Push left stick right (x = 1): yaw must increase
        panoramaLook.ProcessStickInput(new Vector2(1f, 0f), 1f); // 1 sec at 60 deg/sec = 60 deg
        if (!Mathf.Approximately(panoramaLook.CurrentYaw, 60f))
        {
            Debug.LogError($"[FAIL] Yaw right expected 60 deg, got {panoramaLook.CurrentYaw}");
            return;
        }

        // Push left stick left (x = -1): yaw must decrease
        panoramaLook.ProcessStickInput(new Vector2(-1f, 0f), 1.5f); // -90 deg from 60 = -30 -> 330 deg
        if (Mathf.Abs(panoramaLook.CurrentYaw - 330f) > 0.1f)
        {
            Debug.LogError($"[FAIL] Yaw left expected 330 deg, got {panoramaLook.CurrentYaw}");
            return;
        }

        // Multi-rotation test: 10 full seconds of right turn (600 degrees) must wrap smoothly without stopping
        panoramaLook.ProcessStickInput(new Vector2(1f, 0f), 10f); // +600 deg: (330 + 600) % 360 = 210 deg
        if (Mathf.Abs(panoramaLook.CurrentYaw - 210f) > 0.1f)
        {
            Debug.LogError($"[FAIL] Multi-turn Yaw expected 210 deg, got {panoramaLook.CurrentYaw}");
            return;
        }

        // Neutral stick test: stick at (0, 0) must immediately stop rotating
        float yawBeforeNeutral = panoramaLook.CurrentYaw;
        panoramaLook.ProcessStickInput(Vector2.zero, 1f);
        if (!Mathf.Approximately(panoramaLook.CurrentYaw, yawBeforeNeutral))
        {
            Debug.LogError($"[FAIL] Yaw changed during neutral stick: was {yawBeforeNeutral}, became {panoramaLook.CurrentYaw}");
            return;
        }
        Debug.Log("[PASS] Left-stick horizontal rotation (Yaw) verified: smooth continuous 360, no arbitrary stop, immediate stop on neutral.");

        // 11. Test Vertical Rotation (Pitch): tilt up, tilt down, upper limit (+85), lower limit (-85)
        panoramaLook.SetRotation(0f, 0f);

        // Push stick up (y = 1): pitch must increase (tilt upwards)
        panoramaLook.ProcessStickInput(new Vector2(0f, 1f), 1f); // 1 sec at 45 deg/sec = 45 deg
        if (!Mathf.Approximately(panoramaLook.CurrentPitch, 45f))
        {
            Debug.LogError($"[FAIL] Pitch up expected 45 deg, got {panoramaLook.CurrentPitch}");
            return;
        }

        // Upper limit clamp test: push stick up for 5 seconds (225 deg) -> must clamp at maxPitch (+85 deg)
        panoramaLook.ProcessStickInput(new Vector2(0f, 1f), 5f);
        if (Mathf.Abs(panoramaLook.CurrentPitch - 85f) > 0.01f)
        {
            Debug.LogError($"[FAIL] Upper pitch limit clamp expected 85 deg, got {panoramaLook.CurrentPitch}");
            return;
        }

        // Push stick down (y = -1): pitch must decrease (tilt downwards)
        panoramaLook.ProcessStickInput(new Vector2(0f, -1f), 1f); // 85 - 45 = 40 deg
        if (Mathf.Abs(panoramaLook.CurrentPitch - 40f) > 0.1f)
        {
            Debug.LogError($"[FAIL] Pitch down expected 40 deg, got {panoramaLook.CurrentPitch}");
            return;
        }

        // Lower limit clamp test: push stick down for 5 seconds -> must clamp at minPitch (-85 deg)
        panoramaLook.ProcessStickInput(new Vector2(0f, -1f), 5f);
        if (Mathf.Abs(panoramaLook.CurrentPitch - (-85f)) > 0.01f)
        {
            Debug.LogError($"[FAIL] Lower pitch limit clamp expected -85 deg, got {panoramaLook.CurrentPitch}");
            return;
        }

        // Neutral stick test: stick at (0, 0) must immediately stop rotating
        float pitchBeforeNeutral = panoramaLook.CurrentPitch;
        panoramaLook.ProcessStickInput(Vector2.zero, 1f);
        if (!Mathf.Approximately(panoramaLook.CurrentPitch, pitchBeforeNeutral))
        {
            Debug.LogError($"[FAIL] Pitch changed during neutral stick: was {pitchBeforeNeutral}, became {panoramaLook.CurrentPitch}");
            return;
        }
        Debug.Log("[PASS] Left-stick vertical rotation (Pitch) verified: tilts up/down, strictly clamped to [-85, +85] (no flip), immediate stop on neutral.");

        // 12. Test Diagonal Operation: simultaneous horizontal and vertical stick deflection
        panoramaLook.SetRotation(0f, 0f);
        panoramaLook.ProcessStickInput(new Vector2(0.5f, 0.5f), 1f);
        if (panoramaLook.CurrentYaw <= 0f || panoramaLook.CurrentPitch <= 0f)
        {
            Debug.LogError($"[FAIL] Diagonal input failed to update both axes: Yaw={panoramaLook.CurrentYaw}, Pitch={panoramaLook.CurrentPitch}");
            return;
        }
        Debug.Log($"[PASS] Diagonal input verified: Yaw={panoramaLook.CurrentYaw:F1} deg, Pitch={panoramaLook.CurrentPitch:F1} deg.");

        // 13. Test Menu Look Suspension: look rotation suspended while menu is open
        routeManager.SetControlsEnabled(false);
        if (!panoramaLook.IsSuspended)
        {
            Debug.LogError("[FAIL] PanoramaLook.IsSuspended is false when routeManager.ControlsEnabled is false.");
            return;
        }
        routeManager.SetControlsEnabled(true);
        if (panoramaLook.IsSuspended)
        {
            Debug.LogError("[FAIL] PanoramaLook.IsSuspended is true when routeManager.ControlsEnabled is true.");
            return;
        }
        Debug.Log("[PASS] PanoramaLook suspension coordinated with destination menu open/close state.");

        // Reset rotation to neutral
        panoramaLook.SetRotation(0f, 0f);

        Debug.Log("==================================================");
        Debug.Log("[ProjectValidation] ALL 13 VERIFICATION TESTS PASSED!");
        Debug.Log("==================================================");
    }
}

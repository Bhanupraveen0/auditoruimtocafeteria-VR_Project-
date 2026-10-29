using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;
using UnityEngine.XR;

/// <summary>
/// Selects the 360-video route and handles playback on the Meta Quest Right Touch controller stick.
/// - Right stick forward (held): play current video (replays from start if reached end).
/// - Right stick backward (held): rewind current video.
/// - Right stick released: pause video.
/// - Right stick left/right: select previous/next clip (once per push).
/// - Right stick does NOT rotate the viewing direction.
/// (Left stick 360 viewing direction is handled exclusively by PanoramaLook).
/// </summary>
public class RouteManager : MonoBehaviour
{
    [Header("Route")]
    public VideoPlayer videoPlayer;
    public VideoClip[] routeClips;
    public int currentClipIndex = 0;

    [Header("Quest controller controls (Right Controller)")]
    [Range(0.1f, 1f)] public float stickThreshold = 0.6f;
    [Min(0.1f)] public float rewindSecondsPerSecond = 8f;

    private UnityEngine.XR.InputDevice rightController;
    private int heldRouteDirection;
    private bool pauseAfterFirstFrame;
    private bool clipReachedEnd;
    private bool playWhenPrepared;
    private bool controlsEnabled = true;

    public bool ControlsEnabled => controlsEnabled;

    private void Start()
    {
        if (videoPlayer == null)
        {
            Debug.LogError("RouteManager: Assign the Video Player.");
            enabled = false;
            return;
        }

        if (routeClips == null || routeClips.Length == 0)
        {
            Debug.LogError("RouteManager: Add your MP4 clips.");
            enabled = false;
            return;
        }

        currentClipIndex = Mathf.Clamp(
            currentClipIndex, 0, routeClips.Length - 1
        );

        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = false;
        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.frameReady += OnVideoFrameReady;
        videoPlayer.loopPointReached += OnVideoFinished;
        LoadCurrentClipPaused();

        // Ensure PanoramaLook component is available for 360 viewing rotation on the left controller
        if (GetComponent<PanoramaLook>() == null && FindFirstObjectByType<PanoramaLook>() == null)
        {
            gameObject.AddComponent<PanoramaLook>();
        }
    }

    private void OnDestroy()
    {
        if (videoPlayer == null)
            return;

        videoPlayer.prepareCompleted -= OnVideoPrepared;
        videoPlayer.frameReady -= OnVideoFrameReady;
        videoPlayer.loopPointReached -= OnVideoFinished;
    }

    private void Update()
    {
        if (!controlsEnabled)
            return;

        EnsureControllers();
        ReadRouteStick();
        ReadPlaybackStick();
    }

    public void NextClip()
    {
        if (routeClips == null || routeClips.Length == 0)
            return;

        currentClipIndex =
            (currentClipIndex + 1) % routeClips.Length;

        LoadCurrentClipPaused();
    }

    public void PreviousClip()
    {
        if (routeClips == null || routeClips.Length == 0)
            return;

        currentClipIndex =
            (currentClipIndex - 1 + routeClips.Length)
            % routeClips.Length;

        LoadCurrentClipPaused();
    }

    public void PlayCurrentClip()
    {
        if (videoPlayer == null ||
            routeClips == null ||
            routeClips.Length == 0)
            return;

        SelectRoute(currentClipIndex, true);
    }

    /// <summary>Loads a destination by its route-list index and optionally starts it.</summary>
    public void SelectRoute(int routeIndex, bool playImmediately)
    {
        if (videoPlayer == null || routeClips == null || routeIndex < 0 || routeIndex >= routeClips.Length)
            return;

        currentClipIndex = routeIndex;
        LoadCurrentClipPaused();
        playWhenPrepared = playImmediately;

        if (playImmediately && videoPlayer.isPrepared)
            StartPlayback();
    }

    /// <summary>Enables or suspends stick controls while the route menu is open.</summary>
    public void SetControlsEnabled(bool enabled)
    {
        controlsEnabled = enabled;
        heldRouteDirection = 0;

        if (!enabled && videoPlayer != null && videoPlayer.isPlaying)
            videoPlayer.Pause();

        var look = GetComponent<PanoramaLook>();
        if (look == null)
            look = FindFirstObjectByType<PanoramaLook>();
        if (look != null)
            look.SetSuspended(!enabled);
    }

    private void EnsureControllers()
    {
        if (!rightController.isValid)
            rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    private void ReadRouteStick()
    {
#if UNITY_EDITOR
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.wasPressedThisFrame)
            {
                PreviousClip();
                return;
            }

            if (keyboard.dKey.wasPressedThisFrame)
            {
                NextClip();
                return;
            }
        }
#endif

        float horizontal = 0f;
        // Right controller joystick only for route navigation
        if (rightController.isValid && rightController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 rightStick))
            horizontal = rightStick.x;

        int direction = horizontal >= stickThreshold ? 1 : horizontal <= -stickThreshold ? -1 : 0;
        if (direction == 0)
        {
            heldRouteDirection = 0;
            return;
        }

        if (direction == heldRouteDirection)
            return;

        heldRouteDirection = direction;
        if (direction > 0)
            NextClip();
        else
            PreviousClip();
    }

    private void ReadPlaybackStick()
    {
        bool playHeld = false;
        bool rewindHeld = false;

        // Right controller joystick only for video playback
        if (rightController.isValid && rightController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 stick))
        {
            playHeld = stick.y >= stickThreshold;
            rewindHeld = stick.y <= -stickThreshold;
        }

#if UNITY_EDITOR
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            playHeld |= keyboard.spaceKey.isPressed || keyboard.wKey.isPressed;
            rewindHeld |= keyboard.sKey.isPressed;
        }
#endif

        if (playHeld)
        {
            if (clipReachedEnd)
            {
                clipReachedEnd = false;
                videoPlayer.time = 0d;
                videoPlayer.Play();
            }
            else if (!videoPlayer.isPlaying)
            {
                videoPlayer.Play();
            }
        }
        else if (rewindHeld)
        {
            if (videoPlayer.isPlaying)
                videoPlayer.Pause();

            if (videoPlayer.isPrepared)
            {
                clipReachedEnd = false;
                videoPlayer.time = System.Math.Max(0d, videoPlayer.time - rewindSecondsPerSecond * Time.deltaTime);
            }
        }
        else if (videoPlayer.isPlaying)
        {
            videoPlayer.Pause();
        }
    }

    private void LoadCurrentClipPaused()
    {
        VideoClip clip = routeClips[currentClipIndex];
        if (clip == null)
        {
            Debug.LogError("RouteManager: Route clip " + currentClipIndex + " is missing.");
            return;
        }

        videoPlayer.Stop();
        videoPlayer.source = VideoSource.VideoClip;
        videoPlayer.clip = clip;
        videoPlayer.time = 0d;
        clipReachedEnd = false;
        playWhenPrepared = false;
        pauseAfterFirstFrame = true;
        videoPlayer.sendFrameReadyEvents = true;
        videoPlayer.Prepare();
        Debug.Log("Loaded route clip (paused): " + clip.name);
    }

    private void OnVideoPrepared(VideoPlayer source)
    {
        if (playWhenPrepared)
        {
            StartPlayback();
            return;
        }

        if (pauseAfterFirstFrame)
            source.Play();
    }

    private void StartPlayback()
    {
        playWhenPrepared = false;
        pauseAfterFirstFrame = false;
        videoPlayer.sendFrameReadyEvents = false;
        videoPlayer.Play();
    }

    private void OnVideoFrameReady(VideoPlayer source, long frameIndex)
    {
        if (!pauseAfterFirstFrame)
            return;

        source.Pause();
        source.sendFrameReadyEvents = false;
        pauseAfterFirstFrame = false;
    }

    private void OnVideoFinished(VideoPlayer source)
    {
        clipReachedEnd = true;
        source.Pause();
    }
}

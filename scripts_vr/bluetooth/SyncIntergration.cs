using UnityEngine;
using VIVE.OpenXR.Samples.EyeTracker;

/// <summary>
/// Integration script to connect BluetoothManager with EyeTrackingLogger,
/// UIScreenManager, and VideoPlaylistController.
/// Handles starting/stopping synced sessions and video sync messages.
/// </summary>
public class SyncIntegration : MonoBehaviour
{
    [Header("Quick Setup")]
    [SerializeField] private string watchMacAddress = "E4:92:82:88:DA:90";
    [SerializeField] private bool autoConnectOnStart = true;
    [SerializeField] private bool requireWatchForRecording = false;

    [Header("References (Auto-populated if empty)")]
    [SerializeField] private EyeTrackingLogger eyeLogger;
    [SerializeField] private UIScreenManager uiManager;
    [SerializeField] private VideoPlaylistController playlistController;

    // Bluetooth
    private BluetoothManager btManager;

    // State
    private bool isInitialized = false;
    private string currentVideoName = "";

    void Start()
    {
        InitializeSystem();
    }

    void InitializeSystem()
    {
        Debug.Log("🚀 Initializing SyncIntegration...");

        // Find existing components
        if (eyeLogger == null)
            eyeLogger = FindFirstObjectByType<EyeTrackingLogger>();

        if (uiManager == null)
            uiManager = FindFirstObjectByType<UIScreenManager>();

        if (playlistController == null)
            playlistController = FindFirstObjectByType<VideoPlaylistController>();

        // Bluetooth manager
        btManager = BluetoothManager.Instance;
        if (btManager != null && !string.IsNullOrEmpty(watchMacAddress))
        {
            btManager.SetWatchMacAddress(watchMacAddress);
        }

        // Subscribe to events
        SubscribeToEvents();

        // Auto-connect if configured
        if (autoConnectOnStart)
        {
            ConnectToWatch();
        }

        isInitialized = true;
        Debug.Log("✅ SyncIntegration initialized");
    }

    void SubscribeToEvents()
    {
        if (btManager != null)
        {
            btManager.OnConnectionStatusChanged += OnBluetoothConnectionChanged;
            btManager.OnMessageReceived += OnWatchMessageReceived;
        }

        if (playlistController != null)
        {
            playlistController.OnVideoChanged.AddListener(OnVideoChanged);
            playlistController.OnPlaybackStarted.AddListener(OnPlaybackStarted);
            playlistController.OnPlaybackStopped.AddListener(OnPlaybackStopped);
        }
    }

    #region Public Methods for UI Integration

    public void ConnectToWatch()
    {
        if (btManager == null) return;

        Debug.Log($"🔗 Connecting to watch: {watchMacAddress}");
        btManager.ConnectToWatch();
    }

    public void StartSyncedRecording(string videoName = "")
    {
        if (!btManager.IsConnected() && requireWatchForRecording)
        {
            Debug.LogError("❌ Cannot start recording - watch not connected");
            return;
        }

        currentVideoName = videoName;

        if (btManager.IsConnected())
        {
            Debug.Log($"🔄 Starting synced recording: {videoName}");
            var msg = new BluetoothManager.BluetoothMessage
            {
                type = "START",
                sessionId = System.Guid.NewGuid().ToString(),
                payload = videoName
            };
            btManager.SendStructuredMessage(msg);
        }

        if (eyeLogger != null)
        {
            eyeLogger.StartSession(videoName);
            if (!string.IsNullOrEmpty(videoName))
                eyeLogger.SetCurrentVideo(videoName);
        }
    }

    public void StopSyncedRecording()
    {
        if (btManager != null && btManager.IsConnected())
        {
            Debug.Log("🔄 Sending STOP command to watch");
            var msg = new BluetoothManager.BluetoothMessage
            {
                type = "STOP",
                sessionId = System.Guid.NewGuid().ToString(),
                payload = "USER_STOPPED"
            };
            btManager.SendStructuredMessage(msg);
        }

        if (eyeLogger != null)
        {
            Debug.Log("📝 Stopping local recording");
            eyeLogger.EndSession();
            eyeLogger.ClearCurrentVideo();
        }
    }

    public string GetSyncStatus()
    {
        if (btManager == null || !btManager.IsConnected())
            return "Watch: Disconnected";

        return "Watch: Connected";
    }

    #endregion

    #region Event Handlers

    void OnBluetoothConnectionChanged(bool connected)
    {
        Debug.Log($"📡 Bluetooth connection changed: {connected}");

        if (connected)
        {
            // Request time sync on connect
            btManager.RequestTimeSync();
        }
        else
        {
            if (requireWatchForRecording)
            {
                Debug.LogWarning("⚠️ Watch disconnected - stopping recording");
                StopSyncedRecording();
            }
            else
            {
                Debug.Log("⚠️ Watch disconnected - continuing local recording");
            }
        }
    }

    void OnWatchMessageReceived(string message)
    {
        Debug.Log($"📥 Watch message: {message}");
    }

    void OnVideoChanged(int index, string videoName)
    {
        currentVideoName = videoName;

        if (btManager != null && btManager.IsConnected())
        {
            var msg = new BluetoothManager.BluetoothMessage
            {
                type = "VIDEO_CHANGE",
                sessionId = System.Guid.NewGuid().ToString(),
                payload = videoName
            };
            btManager.SendStructuredMessage(msg);
        }
    }

    void OnPlaybackStarted()
    {
        if (!string.IsNullOrEmpty(currentVideoName))
        {
            StartSyncedRecording(currentVideoName);
        }
    }

    void OnPlaybackStopped()
    {
        // Optional: stop when video stops
        // StopSyncedRecording();
    }

    #endregion

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            StopSyncedRecording();
        }
        else
        {
            if (autoConnectOnStart && btManager != null && !btManager.IsConnected())
            {
                ConnectToWatch();
            }
        }
    }

    void OnDestroy()
    {
        if (btManager != null)
        {
            btManager.OnConnectionStatusChanged -= OnBluetoothConnectionChanged;
            btManager.OnMessageReceived -= OnWatchMessageReceived;
        }

        if (playlistController != null)
        {
            playlistController.OnVideoChanged.RemoveListener(OnVideoChanged);
            playlistController.OnPlaybackStarted.RemoveListener(OnPlaybackStarted);
            playlistController.OnPlaybackStopped.RemoveListener(OnPlaybackStopped);
        }
    }
}

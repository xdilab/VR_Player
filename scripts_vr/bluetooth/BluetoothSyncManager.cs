using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VIVE.OpenXR.Samples.EyeTracker;

/// <summary>
/// Manages synchronization between VR headset and smartwatch
/// Coordinates session starts/stops and ensures time alignment
/// </summary>
public class BluetoothSyncManager : MonoBehaviour
{
    #region Event Handlers
    void OnBluetoothConnectionChanged(bool connected)
    {
        if (!connected)
        {
            isWatchSynced = false;

            if (isSessionActive)
            {
                Debug.LogWarning("⚠️ Bluetooth disconnected during active session");

                if (requireWatchConfirmation)
                {
                    // End session if watch is required
                    EndSynchronizedSession("BLUETOOTH_DISCONNECTED");
                }
                else
                {
                    // Continue local recording
                    Debug.Log("📝 Continuing local recording without watch");
                }
            }
        }
        else
        {
            // Connection restored - request time sync
            RequestTimeSync();
        }
    }

    void OnBluetoothMessageReceived(BluetoothManager.BluetoothMessage message)
    {
        switch (message.type)
        {
            case "SYNC_ACK":
                HandleSyncAcknowledgment(message);
                break;

            case "SYNC_DATA":
                HandleSyncData(message);
                break;

            case "SYNC_ERROR":
                HandleSyncError(message);
                break;

            case "HEARTBEAT":
                HandleHeartbeat(message);
                break;
        }
    }

    void OnTimeSyncReceived(BluetoothManager.SyncTimeResponse syncResponse)
    {
        clockOffset = syncResponse.clockOffset;
        Debug.Log($"🕐 Clock offset updated: {clockOffset:F3} seconds");
        OnClockOffsetCalculated?.Invoke(clockOffset);

        // Check if offset is acceptable
        if (Math.Abs(clockOffset) > maxSyncDelay)
        {
            Debug.LogWarning($"⚠️ Large clock offset detected: {clockOffset:F3}s. Synchronization may be affected.");
        }
    }

    void OnLatencyMeasured(float latency)
    {
        // Store latest latency for monitoring
        lastSyncLatency = latency / 1000f; // Convert to seconds
    }

    void HandleSyncAcknowledgment(BluetoothManager.BluetoothMessage message)
    {
        try
        {
            var ack = JsonUtility.FromJson<SyncAcknowledgment>(message.payload);

            if (pendingAcknowledgments.ContainsKey(ack.sessionId))
            {
                float requestTime = pendingAcknowledgments[ack.sessionId];
                float ackLatency = Time.time - requestTime;

                pendingAcknowledgments.Remove(ack.sessionId);

                Debug.Log($"✅ Sync ACK received for session {ack.sessionId}");
                Debug.Log($"   Watch status: {ack.status}, Ready: {ack.ready}, Latency: {ackLatency:F3}s");

                if (!ack.ready)
                {
                    Debug.LogWarning($"⚠️ Watch not ready: {ack.status}");
                    OnSyncError?.Invoke($"Watch not ready: {ack.status}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to parse sync acknowledgment: {e.Message}");
        }
    }

    void HandleSyncData(BluetoothManager.BluetoothMessage message)
    {
        // Handle synchronized data from watch (heart rate, etc.)
        Debug.Log($"📊 Sync data received for session: {message.sessionId}");
    }

    void HandleSyncError(BluetoothManager.BluetoothMessage message)
    {
        Debug.LogError($"❌ Sync error from watch: {message.payload}");
        OnSyncError?.Invoke(message.payload);
    }

    void HandleHeartbeat(BluetoothManager.BluetoothMessage message)
    {
        // Update watch sync status
        if (isSessionActive && message.sessionId == currentSessionId)
        {
            isWatchSynced = true;
        }
    }
    #endregion

    #region Helper Methods
    void StartLocalSession(string videoName)
    {
        Debug.Log("📝 Starting local session (watch not synchronized)");

        isSessionActive = true;
        isWatchSynced = false;
        sessionStartTime = Time.time;
        currentSessionId = GenerateSessionId() + "_LOCAL";

        if (eyeLogger != null)
        {
            eyeLogger.StartSession(videoName);
            if (!string.IsNullOrEmpty(videoName))
            {
                eyeLogger.SetCurrentVideo(videoName);
            }
        }

        OnSessionStarted?.Invoke(currentSessionId);
    }

    string GenerateSessionId()
    {
        return $"VR_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{UnityEngine.Random.Range(1000, 9999)}";
    }

    IEnumerator ProcessPendingOperations()
    {
        while (true)
        {
            while (pendingOperations.Count > 0)
            {
                var operation = pendingOperations.Dequeue();
                operation?.Invoke();
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(0.5f);
        }
    }
    #endregion

    #region Public Status Methods
    public bool IsSessionActive() => isSessionActive;
    public bool IsWatchSynced() => isWatchSynced;
    public string GetCurrentSessionId() => currentSessionId;
    public float GetClockOffset() => clockOffset;
    public float GetSessionDuration() => isSessionActive ? Time.time - sessionStartTime : 0f;
    public int GetSuccessfulSyncCount() => successfulSyncs;
    public int GetFailedSyncCount() => failedSyncs;
    public List<SyncEvent> GetSyncHistory() => new List<SyncEvent>(syncHistory);

    public string GetSyncStatus()
    {
        if (!bluetoothManager.IsConnected())
            return "Disconnected";
        if (!isSessionActive)
            return "Idle";
        if (isWatchSynced)
            return "Synchronized";
        return "Local Only";
    }
    #endregion

    void OnDestroy()
    {
        if (isSessionActive)
        {
            EndSynchronizedSession("APPLICATION_QUIT");
        }

        if (bluetoothManager != null)
        {
            bluetoothManager.OnConnectionStatusChanged -= OnBluetoothConnectionChanged;
            bluetoothManager.OnStructuredMessageReceived -= OnBluetoothMessageReceived;
            bluetoothManager.OnTimeSyncReceived -= OnTimeSyncReceived;
            bluetoothManager.OnLatencyMeasured -= OnLatencyMeasured;
        }

        if (currentSyncOperation != null)
        {
            StopCoroutine(currentSyncOperation);
        }
    }
    private static BluetoothSyncManager _instance;
    public static BluetoothSyncManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<BluetoothSyncManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("BluetoothSyncManager");
                    _instance = go.AddComponent<BluetoothSyncManager>();
                }
            }
            return _instance;
        }
    }

    [Header("References")]
    [SerializeField] private EyeTrackingLogger eyeLogger;
    [SerializeField] private BluetoothManager bluetoothManager;

    [Header("Sync Settings")]
    [SerializeField] private float maxSyncDelay = 0.5f; // Maximum acceptable delay in seconds
    [SerializeField] private int syncRetryAttempts = 3;
    [SerializeField] private float syncTimeout = 3.0f;
    [SerializeField] private bool requireWatchConfirmation = true;

    [Header("Session State")]
    [SerializeField] private string currentSessionId = "";
    [SerializeField] private bool isSessionActive = false;
    [SerializeField] private bool isWatchSynced = false;
    [SerializeField] private float sessionStartTime = 0f;
    [SerializeField] private float clockOffset = 0f; // Time difference between devices

    [Header("Debug Info")]
    [SerializeField] private int successfulSyncs = 0;
    [SerializeField] private int failedSyncs = 0;
    [SerializeField] private float lastSyncLatency = 0f;
    [SerializeField] private List<SyncEvent> syncHistory = new List<SyncEvent>();

    // Events
    public event Action<string> OnSessionSynchronized;
    public event Action<string> OnSessionStarted;
    public event Action<string> OnSessionEnded;
    public event Action<float> OnClockOffsetCalculated;
    public event Action<string> OnSyncError;

    // Pending operations
    private Coroutine currentSyncOperation;
    private Queue<Action> pendingOperations = new Queue<Action>();
    private Dictionary<string, float> pendingAcknowledgments = new Dictionary<string, float>();

    #region Data Structures
    [Serializable]
    public class SyncEvent
    {
        public string eventType;
        public string sessionId;
        public float timestamp;
        public float latency;
        public bool success;
        public string details;

        public SyncEvent(string type, string id, float latency, bool success, string details = "")
        {
            this.eventType = type;
            this.sessionId = id;
            this.timestamp = Time.time;
            this.latency = latency;
            this.success = success;
            this.details = details;
        }
    }

    [Serializable]
    public class SyncStartCommand
    {
        public string command = "SYNC_START";
        public string sessionId;
        public long headsetTime;
        public string videoName;
        public Dictionary<string, object> metadata;
    }

    [Serializable]
    public class SyncStopCommand
    {
        public string command = "SYNC_STOP";
        public string sessionId;
        public long headsetTime;
        public string reason;
    }

    [Serializable]
    public class SyncAcknowledgment
    {
        public string command = "SYNC_ACK";
        public string sessionId;
        public long watchTime;
        public bool ready;
        public string status;
    }
    #endregion

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    void Start()
    {
        InitializeComponents();
        StartCoroutine(ProcessPendingOperations());
    }

    void InitializeComponents()
    {
        // Find components if not assigned
        if (eyeLogger == null)
            eyeLogger = FindFirstObjectByType<EyeTrackingLogger>();

        if (bluetoothManager == null)
            bluetoothManager = BluetoothManager.Instance;

        // Subscribe to Bluetooth events
        if (bluetoothManager != null)
        {
            bluetoothManager.OnConnectionStatusChanged += OnBluetoothConnectionChanged;
            bluetoothManager.OnStructuredMessageReceived += OnBluetoothMessageReceived;
            bluetoothManager.OnTimeSyncReceived += OnTimeSyncReceived;
            bluetoothManager.OnLatencyMeasured += OnLatencyMeasured;
        }

        Debug.Log("✅ BluetoothSyncManager initialized");
    }

    #region Public API
    /// <summary>
    /// Start a synchronized session between headset and watch
    /// </summary>
    public void StartSynchronizedSession(string videoName = "", Dictionary<string, object> metadata = null)
    {
        if (isSessionActive)
        {
            Debug.LogWarning("Session already active. Ending current session first.");
            EndSynchronizedSession("NEW_SESSION_REQUESTED");
            return;
        }

        if (!bluetoothManager.IsConnected())
        {
            Debug.LogError("❌ Cannot start synchronized session - Bluetooth not connected");
            OnSyncError?.Invoke("Bluetooth not connected");

            // Start local session anyway if watch not required
            if (!requireWatchConfirmation)
            {
                StartLocalSession(videoName);
            }
            return;
        }

        // Start sync process
        if (currentSyncOperation != null)
            StopCoroutine(currentSyncOperation);

        currentSyncOperation = StartCoroutine(SynchronizeSessionStart(videoName, metadata));
    }

    /// <summary>
    /// End the current synchronized session
    /// </summary>
    public void EndSynchronizedSession(string reason = "USER_REQUESTED")
    {
        if (!isSessionActive)
        {
            Debug.LogWarning("No active session to end");
            return;
        }

        if (currentSyncOperation != null)
            StopCoroutine(currentSyncOperation);

        currentSyncOperation = StartCoroutine(SynchronizeSessionEnd(reason));
    }

    /// <summary>
    /// Request time synchronization with watch
    /// </summary>
    public void RequestTimeSync()
    {
        if (!bluetoothManager.IsConnected())
        {
            Debug.LogWarning("Cannot sync time - not connected");
            return;
        }

        bluetoothManager.RequestTimeSync();
    }

    /// <summary>
    /// Get synchronized timestamp (adjusted for clock offset)
    /// </summary>
    public long GetSynchronizedTimestamp()
    {
        long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return currentTime + (long)(clockOffset * 1000);
    }

    /// <summary>
    /// Check if devices are properly synchronized
    /// </summary>
    public bool IsSynchronized()
    {
        return isWatchSynced && Math.Abs(clockOffset) < maxSyncDelay;
    }
    #endregion

    #region Synchronization Coroutines
    IEnumerator SynchronizeSessionStart(string videoName, Dictionary<string, object> metadata)
    {
        Debug.Log("🔄 Starting session synchronization...");
        float startTime = Time.time;

        // Generate session ID
        currentSessionId = GenerateSessionId();

        // Prepare sync command
        var syncCommand = new SyncStartCommand
        {
            sessionId = currentSessionId,
            headsetTime = GetSynchronizedTimestamp(),
            videoName = videoName ?? "",
            metadata = metadata ?? new Dictionary<string, object>()
        };

        // Send sync start command to watch
        string commandJson = JsonUtility.ToJson(syncCommand);
        var message = new BluetoothManager.BluetoothMessage
        {
            type = "SYNC_START",
            sessionId = currentSessionId,
            payload = commandJson
        };

        // Track pending acknowledgment
        pendingAcknowledgments[currentSessionId] = Time.time;

        // Try to sync with retries
        bool syncSuccess = false;
        int attempts = 0;

        while (attempts < syncRetryAttempts && !syncSuccess)
        {
            attempts++;
            Debug.Log($"📡 Sync attempt {attempts}/{syncRetryAttempts}");

            // Send command
            bluetoothManager.SendStructuredMessage(message);

            // Wait for acknowledgment
            float waitStart = Time.time;
            while (Time.time - waitStart < syncTimeout)
            {
                if (!pendingAcknowledgments.ContainsKey(currentSessionId))
                {
                    // Acknowledgment received
                    syncSuccess = true;
                    break;
                }
                yield return new WaitForSeconds(0.1f);
            }

            if (!syncSuccess && attempts < syncRetryAttempts)
            {
                yield return new WaitForSeconds(0.5f); // Brief delay before retry
            }
        }

        // Process result
        float syncLatency = Time.time - startTime;
        lastSyncLatency = syncLatency;

        if (syncSuccess)
        {
            // Start local session
            isSessionActive = true;
            isWatchSynced = true;
            sessionStartTime = Time.time;

            if (eyeLogger != null)
            {
                eyeLogger.StartSession(videoName);
                if (!string.IsNullOrEmpty(videoName))
                {
                    eyeLogger.SetCurrentVideo(videoName);
                }
            }

            successfulSyncs++;

            // Log sync event
            var syncEvent = new SyncEvent("SESSION_START", currentSessionId, syncLatency, true);
            syncHistory.Add(syncEvent);
            if (syncHistory.Count > 50) syncHistory.RemoveAt(0);

            Debug.Log($"✅ Session synchronized successfully! ID: {currentSessionId}, Latency: {syncLatency:F3}s");
            OnSessionSynchronized?.Invoke(currentSessionId);
            OnSessionStarted?.Invoke(currentSessionId);
        }
        else
        {
            // Sync failed
            failedSyncs++;
            pendingAcknowledgments.Remove(currentSessionId);

            var syncEvent = new SyncEvent("SESSION_START", currentSessionId, syncLatency, false, "Timeout");
            syncHistory.Add(syncEvent);

            Debug.LogError($"❌ Session synchronization failed after {attempts} attempts");
            OnSyncError?.Invoke($"Sync failed after {attempts} attempts");

            // Start local session if not requiring watch
            if (!requireWatchConfirmation)
            {
                StartLocalSession(videoName);
            }
            else
            {
                currentSessionId = "";
            }
        }

        currentSyncOperation = null;
    }

    IEnumerator SynchronizeSessionEnd(string reason)
    {
        Debug.Log($"🔄 Ending synchronized session: {currentSessionId}");

        // Send stop command to watch
        var stopCommand = new SyncStopCommand
        {
            sessionId = currentSessionId,
            headsetTime = GetSynchronizedTimestamp(),
            reason = reason
        };

        string commandJson = JsonUtility.ToJson(stopCommand);
        var message = new BluetoothManager.BluetoothMessage
        {
            type = "SYNC_STOP",
            sessionId = currentSessionId,
            payload = commandJson
        };

        bluetoothManager.SendStructuredMessage(message);

        // End local session
        if (eyeLogger != null)
        {
            eyeLogger.EndSession();
            eyeLogger.ClearCurrentVideo();
        }

        // Log sync event
        var syncEvent = new SyncEvent("SESSION_END", currentSessionId, 0, true, reason);
        syncHistory.Add(syncEvent);

        // Update state
        isSessionActive = false;
        isWatchSynced = false;

        string endedSessionId = currentSessionId;
        currentSessionId = "";

        Debug.Log($"✅ Session ended: {endedSessionId}");
        OnSessionEnded?.Invoke(endedSessionId);

        currentSyncOperation = null;
        yield return null;
    }
}
    #endregion
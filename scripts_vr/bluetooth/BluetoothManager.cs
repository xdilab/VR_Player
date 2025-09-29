using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enhanced Bluetooth Manager for bidirectional communication with smartwatch
/// Handles connection, message sending/receiving, and callback management
/// </summary>
public class BluetoothManager : MonoBehaviour
{
    #region Singleton
    private static BluetoothManager _instance;
    public static BluetoothManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<BluetoothManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("BluetoothManager");
                    _instance = go.AddComponent<BluetoothManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }
    #endregion

    [Header("Connection Settings")]
    [SerializeField] private string watchMacAddress = "E4:92:82:88:DA:90";
    [SerializeField] private float heartbeatInterval = 2.0f; // Send heartbeat every 2 seconds
    [SerializeField] private float connectionTimeout = 5.0f;
    [SerializeField] private int maxReconnectAttempts = 3;

    [Header("Status")]
    [SerializeField] private bool isConnected = false;
    [SerializeField] private bool isConnecting = false;
    [SerializeField] private float lastHeartbeatTime = 0f;
    [SerializeField] private float lastReceivedTime = 0f;
    [SerializeField] private int reconnectAttempts = 0;

    // Events for connection status
    public event Action<bool> OnConnectionStatusChanged;
    public event Action<string> OnMessageReceived;
    public event Action<BluetoothMessage> OnStructuredMessageReceived;
    public event Action<float> OnLatencyMeasured;
    public event Action<SyncTimeResponse> OnTimeSyncReceived;

    // Message queue for thread safety
    private Queue<string> incomingMessageQueue = new Queue<string>();
    private Queue<Action> mainThreadActions = new Queue<Action>();

    // Latency tracking
    private Dictionary<string, float> pendingPings = new Dictionary<string, float>();
    private float averageLatency = 0f;
    private List<float> latencyMeasurements = new List<float>();

    // Android plugin reference
    private AndroidJavaObject bluetoothPlugin;
    private AndroidJavaObject unityActivity;

    // Coroutines
    private Coroutine heartbeatCoroutine;
    private Coroutine reconnectCoroutine;

    #region Message Types
    [Serializable]
    public class BluetoothMessage
    {
        public string type;           // MESSAGE_TYPE: SYNC, DATA, COMMAND, etc.
        public string timestamp;       // ISO 8601 timestamp
        public string sessionId;       // Shared session identifier
        public string payload;         // JSON payload
        public string checksum;        // Optional message integrity check
    }

    [Serializable]
    public class SyncTimeResponse
    {
        public long deviceTimestamp;   // Watch's current timestamp (milliseconds)
        public long receivedTimestamp; // When watch received our request
        public long sentTimestamp;     // When watch sent response
        public float clockOffset;      // Calculated clock difference
    }

    [Serializable]
    public class SessionCommand
    {
        public string command;         // START, STOP, PAUSE, RESUME
        public string sessionId;       // Unique session identifier
        public long timestamp;         // Unix timestamp in milliseconds
        public Dictionary<string, string> metadata; // Additional session data
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
        DontDestroyOnLoad(gameObject);
        
        InitializeBluetoothPlugin();
    }

    void Start()
    {
        StartCoroutine(ProcessMessageQueue());
        StartCoroutine(ProcessMainThreadActions());
    }

    void InitializeBluetoothPlugin()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            // Get Unity activity
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }

            // Initialize Bluetooth plugin with callback support
            bluetoothPlugin = new AndroidJavaObject("com.example.btplugin.EnhancedBluetoothManager");
            
            // Set up Unity callback receiver
            bluetoothPlugin.Call("setUnityGameObject", gameObject.name);
            
            Debug.Log("✅ Bluetooth plugin initialized");
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Failed to initialize Bluetooth plugin: {e.Message}");
        }
#else
        Debug.LogWarning("⚠️ Bluetooth only works on Android device");
#endif
    }

    #region Connection Management
    public void ConnectToWatch()
    {
        if (isConnecting || isConnected)
        {
            Debug.LogWarning("Already connected or connecting");
            return;
        }

        StartCoroutine(ConnectAsync());
    }

    IEnumerator ConnectAsync()
    {
        isConnecting = true;
        Debug.Log($"🔗 Attempting to connect to watch: {watchMacAddress}");

#if UNITY_ANDROID && !UNITY_EDITOR
        if (bluetoothPlugin != null)
        {
            bluetoothPlugin.Call("connectToDevice", watchMacAddress);
            
            // Wait for connection with timeout
            float startTime = Time.time;
            while (!isConnected && Time.time - startTime < connectionTimeout)
            {
                yield return new WaitForSeconds(0.1f);
            }
            
            if (!isConnected)
            {
                Debug.LogError("❌ Connection timeout");
                OnConnectionFailed();
            }
        }
#else
        // Simulate connection in editor
        yield return new WaitForSeconds(1f);
        OnConnectionEstablished();
#endif

        isConnecting = false;
    }

    public void DisconnectFromWatch()
    {
        if (!isConnected) return;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (bluetoothPlugin != null)
        {
            bluetoothPlugin.Call("disconnect");
        }
#endif

        OnConnectionLost();
    }

    void OnConnectionEstablished()
    {
        isConnected = true;
        isConnecting = false;
        reconnectAttempts = 0;
        lastReceivedTime = Time.time;

        // Start heartbeat
        if (heartbeatCoroutine != null) StopCoroutine(heartbeatCoroutine);
        heartbeatCoroutine = StartCoroutine(HeartbeatLoop());

        // Perform initial time sync
        RequestTimeSync();

        Debug.Log("✅ Connected to watch");
        OnConnectionStatusChanged?.Invoke(true);

        // Kick off service discovery
        DiscoverServices();
    }

    void OnConnectionLost()
    {
        isConnected = false;
        isConnecting = false;

        if (heartbeatCoroutine != null)
        {
            StopCoroutine(heartbeatCoroutine);
            heartbeatCoroutine = null;
        }

        Debug.LogWarning("❌ Connection lost");
        OnConnectionStatusChanged?.Invoke(false);

        // Attempt reconnection
        if (reconnectAttempts < maxReconnectAttempts)
        {
            if (reconnectCoroutine != null) StopCoroutine(reconnectCoroutine);
            reconnectCoroutine = StartCoroutine(AttemptReconnect());
        }
    }

    IEnumerator AttemptReconnect()
    {
        reconnectAttempts++;
        Debug.Log($"🔄 Reconnection attempt {reconnectAttempts}/{maxReconnectAttempts}");
        
        yield return new WaitForSeconds(2f * reconnectAttempts); // Exponential backoff
        ConnectToWatch();
    }
    #endregion

    #region Message Sending
    public void SendMessage(string message)
    {
        if (!isConnected)
        {
            Debug.LogWarning("Cannot send message - not connected");
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (bluetoothPlugin != null)
        {
            bluetoothPlugin.Call("sendMessage", message);
        }
#else
        Debug.Log($"📤 [Editor] Would send: {message}");
#endif
    }

    public void SendStructuredMessage(BluetoothMessage message)
    {
        message.timestamp = DateTime.UtcNow.ToString("o");
        string json = JsonUtility.ToJson(message);
        SendMessage(json);
    }

    public void SendCommand(string command, string sessionId = null)
    {
        var cmd = new SessionCommand
        {
            command = command,
            sessionId = sessionId ?? GenerateSessionId(),
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            metadata = new Dictionary<string, string>()
        };

        string json = JsonUtility.ToJson(cmd);
        
        var message = new BluetoothMessage
        {
            type = "COMMAND",
            sessionId = cmd.sessionId,
            payload = json
        };

        SendStructuredMessage(message);
    }

    public void SendPing()
    {
        string pingId = Guid.NewGuid().ToString();
        pendingPings[pingId] = Time.time;

        var message = new BluetoothMessage
        {
            type = "PING",
            sessionId = pingId,
            payload = ""
        };

        SendStructuredMessage(message);
    }

    public void RequestTimeSync()
    {
        var message = new BluetoothMessage
        {
            type = "TIME_SYNC_REQUEST",
            sessionId = GenerateSessionId(),
            payload = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
        };

        SendStructuredMessage(message);
        Debug.Log("🕐 Requesting time sync from watch");
    }
    #endregion

    #region Message Receiving (Called from Android)
    // These methods are called from Android plugin via UnitySendMessage
    public void OnBluetoothConnected(string info)
    {
        mainThreadActions.Enqueue(() => OnConnectionEstablished());
    }

    public void OnBluetoothDisconnected(string reason)
    {
        mainThreadActions.Enqueue(() => OnConnectionLost());
    }

    public void OnBluetoothMessageReceived(string message)
    {
        incomingMessageQueue.Enqueue(message);
        lastReceivedTime = Time.time;
    }

    public void OnBluetoothError(string error)
    {
        Debug.LogError($"❌ Bluetooth error: {error}");
        mainThreadActions.Enqueue(() => OnConnectionFailed());
    }

    void OnConnectionFailed()
    {
        isConnecting = false;
        isConnected = false;
        OnConnectionStatusChanged?.Invoke(false);
    }
    #endregion

    #region Message Processing
    IEnumerator ProcessMessageQueue()
    {
        while (true)
        {
            while (incomingMessageQueue.Count > 0)
            {
                string message = incomingMessageQueue.Dequeue();
                ProcessIncomingMessage(message);
            }
            yield return new WaitForSeconds(0.01f); // Process at 100Hz
        }
    }

    IEnumerator ProcessMainThreadActions()
    {
        while (true)
        {
            while (mainThreadActions.Count > 0)
            {
                var action = mainThreadActions.Dequeue();
                action?.Invoke();
            }
            yield return null;
        }
    }

    void ProcessIncomingMessage(string rawMessage)
    {
        Debug.Log($"📥 Received: {rawMessage}");
        
        // Try to parse as structured message
        try
        {
            BluetoothMessage message = JsonUtility.FromJson<BluetoothMessage>(rawMessage);
            
            switch (message.type)
            {
                case "PONG":
                    HandlePongMessage(message);
                    break;
                    
                case "TIME_SYNC_RESPONSE":
                    HandleTimeSyncResponse(message);
                    break;
                    
                case "DATA":
                    HandleDataMessage(message);
                    break;
                    
                case "ACK":
                    HandleAcknowledgment(message);
                    break;
                    
                default:
                    OnStructuredMessageReceived?.Invoke(message);
                    break;
            }
        }
        catch
        {
            // Fallback for simple string messages
            OnMessageReceived?.Invoke(rawMessage);
        }
    }

    void HandlePongMessage(BluetoothMessage message)
    {
        if (pendingPings.ContainsKey(message.sessionId))
        {
            float pingTime = pendingPings[message.sessionId];
            float latency = (Time.time - pingTime) * 1000f; // Convert to milliseconds
            
            pendingPings.Remove(message.sessionId);
            
            // Update latency tracking
            latencyMeasurements.Add(latency);
            if (latencyMeasurements.Count > 10)
                latencyMeasurements.RemoveAt(0);
            
            averageLatency = CalculateAverageLatency();
            
            Debug.Log($"📡 Latency: {latency:F1}ms (avg: {averageLatency:F1}ms)");
            OnLatencyMeasured?.Invoke(latency);
        }
    }

    void HandleTimeSyncResponse(BluetoothMessage message)
    {
        try
        {
            SyncTimeResponse syncResponse = JsonUtility.FromJson<SyncTimeResponse>(message.payload);
            
            // Calculate clock offset
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long roundTripTime = currentTime - syncResponse.receivedTimestamp;
            syncResponse.clockOffset = (syncResponse.deviceTimestamp - currentTime + roundTripTime / 2) / 1000f;
            
            Debug.Log($"🕐 Time sync complete. Clock offset: {syncResponse.clockOffset:F3} seconds");
            OnTimeSyncReceived?.Invoke(syncResponse);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to parse time sync response: {e.Message}");
        }
    }

    void HandleDataMessage(BluetoothMessage message)
    {
        // Process data messages (heart rate, accelerometer, etc.)
        OnStructuredMessageReceived?.Invoke(message);
    }

    void HandleAcknowledgment(BluetoothMessage message)
    {
        Debug.Log($"✅ ACK received for session: {message.sessionId}");
    }
    #endregion

    #region Heartbeat & Health Monitoring
    IEnumerator HeartbeatLoop()
    {
        while (isConnected)
        {
            SendPing();
            lastHeartbeatTime = Time.time;
            
            yield return new WaitForSeconds(heartbeatInterval);
            
            // Check for connection health
            if (Time.time - lastReceivedTime > heartbeatInterval * 3)
            {
                Debug.LogWarning("⚠️ No response from watch - connection may be lost");
                OnConnectionLost();
            }
        }
    }

    float CalculateAverageLatency()
    {
        if (latencyMeasurements.Count == 0) return 0f;
        
        float sum = 0f;
        foreach (float latency in latencyMeasurements)
            sum += latency;
        
        return sum / latencyMeasurements.Count;
    }
    #endregion

    #region Utility Methods
    string GenerateSessionId()
    {
        return $"{SystemInfo.deviceUniqueIdentifier}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }

    public bool IsConnected() => isConnected;
    public float GetAverageLatency() => averageLatency;
    public float GetLastReceivedTime() => lastReceivedTime;
    #endregion

    void OnDestroy()
    {
        DisconnectFromWatch();
        
        if (heartbeatCoroutine != null)
            StopCoroutine(heartbeatCoroutine);
        
        if (reconnectCoroutine != null)
            StopCoroutine(reconnectCoroutine);

#if UNITY_ANDROID && !UNITY_EDITOR
        if (bluetoothPlugin != null)
        {
            bluetoothPlugin.Call("cleanup");
            bluetoothPlugin.Dispose();
        }
#endif
    }

    #region Service & Characteristic Discovery
    public event Action<string> OnServiceDiscovered;
    public event Action<string, string> OnCharacteristicDiscovered;

    private readonly List<string> discoveredServices = new List<string>();
    private readonly Dictionary<string, List<string>> serviceCharacteristics = new Dictionary<string, List<string>>();

    public void DiscoverServices()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (bluetoothPlugin != null && isConnected)
        {
            bluetoothPlugin.Call("discoverServices");
            Debug.Log("🔍 Requested service discovery");
        }
#else
        Debug.LogWarning("⚠️ Service discovery only works on Android device");
#endif
    }

    public void OnServiceFound(string serviceUuid)
    {
        if (!discoveredServices.Contains(serviceUuid))
        {
            discoveredServices.Add(serviceUuid);
            Debug.Log($"📡 Service discovered: {serviceUuid}");
            OnServiceDiscovered?.Invoke(serviceUuid);
        }
    }

    public void OnCharacteristicFound(string data)
    {
        string[] parts = data.Split('|');
        if (parts.Length == 2)
        {
            string serviceUuid = parts[0];
            string characteristicUuid = parts[1];

            if (!serviceCharacteristics.ContainsKey(serviceUuid))
                serviceCharacteristics[serviceUuid] = new List<string>();

            if (!serviceCharacteristics[serviceUuid].Contains(characteristicUuid))
                serviceCharacteristics[serviceUuid].Add(characteristicUuid);

            Debug.Log($"🔑 Characteristic discovered: {characteristicUuid} in {serviceUuid}");
            OnCharacteristicDiscovered?.Invoke(serviceUuid, characteristicUuid);
        }
    }
    public void SetWatchMacAddress(string mac)
    {
        watchMacAddress = mac;
        Debug.Log($"🔗 Watch MAC address updated to: {watchMacAddress}");
    }


    public void SubscribeToCharacteristic(string serviceUuid, string characteristicUuid)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (bluetoothPlugin != null && isConnected)
        {
            bluetoothPlugin.Call("subscribeToCharacteristic", serviceUuid, characteristicUuid);
            Debug.Log($"📡 Subscribed to {characteristicUuid} in {serviceUuid}");
        }
#else
        Debug.LogWarning("⚠️ Subscriptions only work on Android device");
#endif
    }
    #endregion

    #region Public API for Testing
    public void SimulateIncomingMessage(string message)
    {
#if UNITY_EDITOR
        incomingMessageQueue.Enqueue(message);
#endif
    }

    public void SimulateConnection()
    {
#if UNITY_EDITOR
        OnConnectionEstablished();
#endif
    }

    public void SimulateDisconnection()
    {
#if UNITY_EDITOR
        OnConnectionLost();
#endif
    }
    #endregion
}

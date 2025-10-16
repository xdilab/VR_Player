using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bluetooth Manager for Galaxy Watch 7 Custom Service
/// </summary>
public class BluetoothManager : MonoBehaviour
{
    private static BluetoothManager _instance;
    public static BluetoothManager Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("BluetoothManager");
                _instance = go.AddComponent<BluetoothManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    [Header("Custom Service UUIDs")]
    [SerializeField] private string customServiceUUID = "0000FFF0-0000-1000-8000-00805F9B34FB";
    [SerializeField] private string csvCharacteristicUUID = "0000FFF1-0000-1000-8000-00805F9B34FB";

    [Header("Device Settings")]
    [SerializeField] private string targetDeviceName = "Galaxy Watch7";

    [Header("Status")]
    [SerializeField] private bool isScanning = false;
    [SerializeField] private bool isConnected = false;
    [SerializeField] private string connectedDeviceAddress = "";

    [Header("Latest Sensor Data")]
    [SerializeField] private int currentHeartRate = 0;
    [SerializeField] private float rmssd = 0f;
    [SerializeField] private float sdnn = 0f;
    [SerializeField] private string stressLevel = "null";
    [SerializeField] private Vector3 acceleration = Vector3.zero;

    // Events
    public event Action<bool> OnConnectionChanged;
    public event Action<SensorData> OnSensorDataReceived;
    public event Action<string, string> OnDeviceFound;

    // State management
    private enum State
    {
        None,
        Initializing,
        Scanning,
        Connecting,
        Connected,
        Subscribing
    }
    private State currentState = State.None;

    // Discovered devices
    private Dictionary<string, string> discoveredDevices = new Dictionary<string, string>();
    private string lastFoundAddress = ""; // ✅ keeps track of last discovered device

    // CSV parsing
    private string csvBuffer = "";
    private bool headerReceived = false;

    [Serializable]
    public class SensorData
    {
        public string timestamp;
        public int heartRate;
        public float rmssd;
        public float sdnn;
        public string stressLevel;
        public Vector3 acceleration;

        public override string ToString()
        {
            return $"[{timestamp}] HR:{heartRate} RMSSD:{rmssd:F2} SDNN:{sdnn:F2} Stress:{stressLevel} Accel:{acceleration}";
        }
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        InitializeBluetooth();
    }

    #region Initialization

    void InitializeBluetooth()
    {
        currentState = State.Initializing;
        Debug.Log("Initializing Bluetooth...");

        BluetoothLEHardwareInterface.Initialize(true, false, () =>
        {
            Debug.Log("Bluetooth initialized successfully");
            currentState = State.None;
        }, (error) =>
        {
            Debug.LogError($"Bluetooth initialization failed: {error}");
            currentState = State.None;
        });
    }

    void OnApplicationQuit()
    {
        if (isConnected)
        {
            DisconnectDevice();
        }

        BluetoothLEHardwareInterface.DeInitialize(() =>
        {
            Debug.Log("Bluetooth deinitialized");
        });
    }

    #endregion

    #region Scanning

    public void StartScanning()
    {
        if (currentState != State.None)
        {
            Debug.LogWarning("Cannot scan - operation in progress");
            return;
        }

        currentState = State.Scanning;
        isScanning = true;
        discoveredDevices.Clear();
        lastFoundAddress = "";

        Debug.Log("Starting BLE scan for custom service...");

        // Scan specifically for your custom service UUID
        string[] serviceUUIDs = new string[] { customServiceUUID };

        BluetoothLEHardwareInterface.ScanForPeripheralsWithServices(
            serviceUUIDs,
            (address, name) =>
            {
                if (!string.IsNullOrEmpty(name) && !discoveredDevices.ContainsKey(address))
                {
                    discoveredDevices[address] = name;
                    lastFoundAddress = address; // ✅ store last found device
                    Debug.Log($"Found watch: {name} ({address})");
                    OnDeviceFound?.Invoke(address, name);
                }
            },
            null,
            false,
            false
        );

        Invoke(nameof(StopScanning), 10f);
    }

    public void StopScanning()
    {
        if (!isScanning) return;

        BluetoothLEHardwareInterface.StopScan();
        isScanning = false;
        currentState = State.None;
        Debug.Log("Scan stopped");
    }

    #endregion

    #region Connection

    public void ConnectToDevice(string deviceAddress)
    {
        if (currentState != State.None)
        {
            Debug.LogWarning("Cannot connect - operation in progress");
            return;
        }

        currentState = State.Connecting;
        Debug.Log($"Connecting to watch: {deviceAddress}");

        BluetoothLEHardwareInterface.ConnectToPeripheral(
            deviceAddress,
            (address) =>
            {
                Debug.Log($"Connected to: {address}");
                connectedDeviceAddress = address;
                isConnected = true;
                currentState = State.Connected;
                OnConnectionChanged?.Invoke(true);
            },
            (address, serviceUUID) =>
            {
                Debug.Log($"Service discovered: {serviceUUID}");
                if (IsEqual(serviceUUID, customServiceUUID))
                {
                    Debug.Log("Custom sensor service found!");
                }
            },
            (address, serviceUUID, characteristicUUID) =>
            {
                Debug.Log($"Characteristic: {characteristicUUID} in service {serviceUUID}");
                if (IsEqual(serviceUUID, customServiceUUID) &&
                    IsEqual(characteristicUUID, csvCharacteristicUUID))
                {
                    Debug.Log("CSV data characteristic found! Subscribing...");
                    SubscribeToCSVData(address);
                }
            },
            (address) =>
            {
                Debug.LogWarning($"Disconnected from: {address}");
                isConnected = false;
                currentState = State.None;
                connectedDeviceAddress = "";
                headerReceived = false;
                csvBuffer = "";
                OnConnectionChanged?.Invoke(false);
            }
        );
    }

    // ✅ helper for buttons (connect to most recently found watch)
    public void ConnectToLastDevice()
    {
        if (string.IsNullOrEmpty(lastFoundAddress))
        {
            Debug.LogWarning("No previously found device address to connect to.");
            return;
        }

        Debug.Log($"Connecting to last found device: {lastFoundAddress}");
        ConnectToDevice(lastFoundAddress);
    }

    void SubscribeToCSVData(string address)
    {
        currentState = State.Subscribing;

        string fullServiceUUID = customServiceUUID.ToLower();
        string fullCharUUID = csvCharacteristicUUID.ToLower();

        Debug.Log("Subscribing to CSV data notifications...");

        BluetoothLEHardwareInterface.SubscribeCharacteristicWithDeviceAddress(
            address,
            fullServiceUUID,
            fullCharUUID,
            (notifyAddress, notifyChar) =>
            {
                Debug.Log($"Subscribed to CSV notifications on {notifyChar}");
                currentState = State.Connected;
            },
            (dataAddress, dataChar, data) =>
            {
                ProcessCSVChunk(data);
            }
        );
    }

    public void DisconnectDevice()
    {
        if (!isConnected) return;

        Debug.Log($"Disconnecting from {connectedDeviceAddress}");

        BluetoothLEHardwareInterface.DisconnectPeripheral(connectedDeviceAddress, (address) =>
        {
            Debug.Log($"Disconnected from {address}");
            isConnected = false;
            connectedDeviceAddress = "";
            currentState = State.None;
            headerReceived = false;
            csvBuffer = "";
            OnConnectionChanged?.Invoke(false);
        });
    }

    #endregion

    #region CSV Data Processing

    void ProcessCSVChunk(byte[] data)
    {
        if (data == null || data.Length == 0) return;

        string chunk = System.Text.Encoding.UTF8.GetString(data);
        csvBuffer += chunk;

        // Process complete lines
        while (csvBuffer.Contains("\n"))
        {
            int newlineIndex = csvBuffer.IndexOf("\n");
            string line = csvBuffer.Substring(0, newlineIndex).Trim();
            csvBuffer = csvBuffer.Substring(newlineIndex + 1);

            if (!string.IsNullOrEmpty(line))
            {
                ProcessCSVLine(line);
            }
        }
    }

    void ProcessCSVLine(string line)
    {
        // First line is header
        if (!headerReceived)
        {
            if (line.Contains("Timestamp"))
            {
                Debug.Log($"CSV Header: {line}");
                headerReceived = true;
            }
            return;
        }

        // Parse data line: Timestamp,HeartRate,RMSSD,SDNN,StressLevel,AccelX,AccelY,AccelZ
        string[] values = line.Split(',');
        if (values.Length < 8) return;

        try
        {
            var sensorData = new SensorData
            {
                timestamp = values[0],
                heartRate = int.Parse(values[1]),
                rmssd = float.Parse(values[2]),
                sdnn = float.Parse(values[3]),
                stressLevel = values[4],
                acceleration = new Vector3(
                    float.Parse(values[5]),
                    float.Parse(values[6]),
                    float.Parse(values[7])
                )
            };

            // Update local cache
            currentHeartRate = sensorData.heartRate;
            rmssd = sensorData.rmssd;
            sdnn = sensorData.sdnn;
            stressLevel = sensorData.stressLevel;
            acceleration = sensorData.acceleration;

            Debug.Log($"Sensor Data: {sensorData}");
            OnSensorDataReceived?.Invoke(sensorData);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to parse CSV line: {line}\nError: {e.Message}");
        }
    }

    #endregion

    #region UUID Helpers

    bool IsEqual(string uuid1, string uuid2)
    {
        uuid1 = uuid1.Replace("-", "").ToLower();
        uuid2 = uuid2.Replace("-", "").ToLower();
        return uuid1 == uuid2;
    }

    #endregion

    #region Public API

    public bool IsConnected() => isConnected;
    public int GetCurrentHeartRate() => currentHeartRate;
    public float GetRMSSD() => rmssd;
    public float GetSDNN() => sdnn;
    public string GetStressLevel() => stressLevel;
    public Vector3 GetAcceleration() => acceleration;
    public string GetConnectedDeviceAddress() => connectedDeviceAddress;
    public Dictionary<string, string> GetDiscoveredDevices() => new Dictionary<string, string>(discoveredDevices);

    public void SetWatchMacAddress(string mac)
    {
        // Not needed for this implementation since we scan by service UUID
        Debug.Log($"Watch MAC address noted: {mac}");
    }

    #endregion
}

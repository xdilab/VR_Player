//Last updated 2 months ago



using UnityEngine;
using UnityEngine.Networking;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using VIVE.OpenXR.EyeTracker;

namespace VIVE.OpenXR.Samples.EyeTracker
{
    public class EyeTrackingLogger : MonoBehaviour
    {
        [Header("Logging Settings")]
        [SerializeField] private float sampleRate = 1.0f; // 1 sample per second
        [SerializeField] private string headsetIdentifier = ""; // Will be auto-detected
        
        [Header("Network Settings")]
        [SerializeField] private string serverUrl = "http://10.0.0.54:5000";
        [SerializeField] private float liveDataInterval = 1f;
        
        // File management
        private string currentFilePath;
        private StreamWriter csvWriter;
        private int currentSessionNumber = 0;
        private string currentDate;
        
        // Video tracking
        private string currentVideoName = "";
        private bool isSessionActive = false;
        
        // Sampling control
        private float lastSampleTime = 0f;
        private float timeSinceLastLiveData = 0f;
        
        // Data cache for reliable writing
        private Queue<string> dataQueue = new Queue<string>();
        private Coroutine writeCoroutine;

        void Start()
        {
            // Auto-detect hardware name if not set
            if (string.IsNullOrEmpty(headsetIdentifier))
            {
                headsetIdentifier = GetHardwareName();
                Debug.Log($"🎯 Auto-detected hardware: {headsetIdentifier}");
            }
            
            InitializeLogging();
            writeCoroutine = StartCoroutine(ProcessDataQueue());
        }

        /// <summary>
        /// Gets the hardware name with multiple fallback options
        /// </summary>
        string GetHardwareName()
        {
            // Try SystemInfo.deviceName first (best for VR headsets)
            try 
            {
                string deviceName = SystemInfo.deviceName;
                if (!string.IsNullOrEmpty(deviceName) && deviceName != "Unknown")
                {
                    return deviceName;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not get device name: {e.Message}");
            }

            // Try XR device name (for VR headsets)
            try
            {
                string xrDevice = UnityEngine.XR.XRSettings.loadedDeviceName;
                if (!string.IsNullOrEmpty(xrDevice) && xrDevice != "None")
                {
                    return $"{SystemInfo.deviceModel}_{xrDevice}";
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not get XR device name: {e.Message}");
            }

            // Try SystemInfo.deviceModel
            try
            {
                string deviceModel = SystemInfo.deviceModel;
                if (!string.IsNullOrEmpty(deviceModel) && deviceModel != "Unknown")
                {
                    return deviceModel;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not get device model: {e.Message}");
            }
            
            // Try System.Environment.MachineName (your original approach)
            try 
            {
                string machineName = System.Environment.MachineName;
                if (!string.IsNullOrEmpty(machineName))
                {
                    return machineName;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not get machine name: {e.Message}");
            }
            
            // Final fallback with timestamp
            return $"VR_HEADSET_{System.DateTime.Now:yyyyMMdd_HHmmss}";
        }

        void InitializeLogging()
        {
            currentDate = DateTime.Now.ToString("MM-dd-yyyy");
            
            // Create logs directory
            string logFolder = Path.Combine(Application.persistentDataPath, "Logs");
            Directory.CreateDirectory(logFolder);
            
            // Get the current session number BEFORE creating file
            currentSessionNumber = GetNextSessionNumber();
            
            // Create or append to today's file
            string fileName = $"Session_{GetDaySessionNumber()}_{currentDate}.csv";
            currentFilePath = Path.Combine(logFolder, fileName);
            
            bool fileExists = File.Exists(currentFilePath);
            csvWriter = new StreamWriter(currentFilePath, true); // Append mode
            
            if (!fileExists)
            {
                // Write file header for new file
                WriteFileHeader();
            }
            
            Debug.Log($"📁 Logging to: {currentFilePath}");
            Debug.Log($"📊 Current Session Number: {currentSessionNumber}");
            Debug.Log($"📊 Today's session count in PlayerPrefs: {PlayerPrefs.GetInt($"SessionCount_{DateTime.Now:yyyyMMdd}", 0)}");
        }

        void WriteFileHeader()
        {
            string header = $"# HEADSET_ID: {headsetIdentifier} | DATE: {DateTime.Now:yyyy-MM-dd}";
            csvWriter.WriteLine(header);
            csvWriter.WriteLine("Session_Num,Time,Video_Name,Eye_Left_X,Eye_Left_Y,Eye_Left_Z,Eye_Right_X,Eye_Right_Y,Eye_Right_Z,Head_X,Head_Y,Head_Z,Head_Rot_X,Head_Rot_Y,Head_Rot_Z,Head_Rot_W");
            csvWriter.Flush();
        }

        int GetNextSessionNumber()
        {
            // Check PlayerPrefs for today's session count
            string dateKey = $"SessionDate_{DateTime.Now:yyyyMMdd}";
            string countKey = $"SessionCount_{DateTime.Now:yyyyMMdd}";
            
            string savedDate = PlayerPrefs.GetString(dateKey, "");
            
            if (savedDate != DateTime.Now.ToString("yyyyMMdd"))
            {
                // New day, reset counter
                PlayerPrefs.SetString(dateKey, DateTime.Now.ToString("yyyyMMdd"));
                PlayerPrefs.SetInt(countKey, 0);
                PlayerPrefs.Save();
                return 0;
            }
            else
            {
                // Same day, get current count (this is the NEXT session number to use)
                return PlayerPrefs.GetInt(countKey, 0);
            }
        }

        int GetDaySessionNumber()
        {
            // Get the overall session number for the day (for filename)
            string dayCountKey = $"DaySessionNumber_{DateTime.Now:yyyyMMdd}";
            int dayNumber = PlayerPrefs.GetInt(dayCountKey, 1);
            
            if (currentSessionNumber == 0)
            {
                // First session of the day, might need to increment day number
                PlayerPrefs.SetInt(dayCountKey, dayNumber);
                PlayerPrefs.Save();
            }
            
            return dayNumber;
        }

        void IncrementSessionNumber()
        {
            string countKey = $"SessionCount_{DateTime.Now:yyyyMMdd}";
            int nextSession = PlayerPrefs.GetInt(countKey, 0) + 1;
            PlayerPrefs.SetInt(countKey, nextSession);
            PlayerPrefs.Save();
            Debug.Log($"📈 Session counter incremented. Next session will be: {nextSession}");
        }

        public void StartSession(string videoCategory = "")
        {
            if (isSessionActive)
            {
                Debug.LogWarning("Session already active! Ending previous session first.");
                EndSession();
            }
            
            // Always get the latest session number when starting
            currentSessionNumber = GetNextSessionNumber();
            
            isSessionActive = true;
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string sessionStart = $"# START SESSION {currentSessionNumber}: {timestamp}";
            
            dataQueue.Enqueue(sessionStart);
            
            Debug.Log($"📌 Session {currentSessionNumber} started at {timestamp}");
        }

        public void EndSession()
        {
            if (!isSessionActive)
            {
                Debug.LogWarning("No active session to end!");
                return;
            }
            
            isSessionActive = false;
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string sessionEnd = $"# END SESSION {currentSessionNumber}: {timestamp}";
            
            // Write END SESSION immediately to file instead of queue for force quit scenarios
            try
            {
                csvWriter?.WriteLine(sessionEnd);
                csvWriter?.Flush();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to write session end: {e.Message}");
            }
            
            // Increment for next session
            IncrementSessionNumber();
            
            Debug.Log($"📌 Session {currentSessionNumber - 1} ended at {timestamp}");
        }

        public void SetCurrentVideo(string videoName)
        {
            currentVideoName = videoName ?? "";
            Debug.Log($"🎥 Current video: {currentVideoName}");
        }

        public void ClearCurrentVideo()
        {
            currentVideoName = "";
        }

        void Update()
        {
            if (!isSessionActive) return;
            
            // Sample at specified rate (1 Hz by default)
            if (Time.time - lastSampleTime >= sampleRate)
            {
                CollectAndLogData();
                lastSampleTime = Time.time;
            }
            
            // Send live data at interval
            timeSinceLastLiveData += Time.deltaTime;
            if (timeSinceLastLiveData >= liveDataInterval)
            {
                StartCoroutine(SendLiveData());
                timeSinceLastLiveData = 0f;
            }
        }

        void CollectAndLogData()
        {
            // Get timestamp
            string timeString = DateTime.Now.ToString("HH:mm:ss");
            
            // Get head tracking data
            var headDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
            Vector3 headPos = Vector3.zero;
            Quaternion headRot = Quaternion.identity;
            
            bool headValid = headDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out headPos) &&
                            headDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out headRot);
            
            // Get eye tracking data
            XR_HTC_eye_tracker.Interop.GetEyeGazeData(out XrSingleEyeGazeDataHTC[] gazes);
            var left = gazes[(int)XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC];
            var right = gazes[(int)XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC];
            
            Vector3 leftDir = Vector3.zero;
            Vector3 rightDir = Vector3.zero;
            
            if (left.isValid && right.isValid)
            {
                Quaternion leftRot = new Quaternion(
                    left.gazePose.orientation.x,
                    left.gazePose.orientation.y,
                    left.gazePose.orientation.z,
                    left.gazePose.orientation.w
                );
                Quaternion rightRot = new Quaternion(
                    right.gazePose.orientation.x,
                    right.gazePose.orientation.y,
                    right.gazePose.orientation.z,
                    right.gazePose.orientation.w
                );
                
                leftDir = leftRot * Vector3.forward;
                rightDir = rightRot * Vector3.forward;
            }
            
            // Format CSV row
            string csvRow = string.Format("{0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F4},{13:F4},{14:F4},{15:F4}",
                currentSessionNumber,
                timeString,
                currentVideoName,
                left.isValid ? leftDir.x : float.NaN,
                left.isValid ? leftDir.y : float.NaN,
                left.isValid ? leftDir.z : float.NaN,
                right.isValid ? rightDir.x : float.NaN,
                right.isValid ? rightDir.y : float.NaN,
                right.isValid ? rightDir.z : float.NaN,
                headValid ? headPos.x : float.NaN,
                headValid ? headPos.y : float.NaN,
                headValid ? headPos.z : float.NaN,
                headValid ? headRot.x : float.NaN,
                headValid ? headRot.y : float.NaN,
                headValid ? headRot.z : float.NaN,
                headValid ? headRot.w : float.NaN
            );
            
            dataQueue.Enqueue(csvRow);
        }

        IEnumerator ProcessDataQueue()
        {
            while (true)
            {
                if (dataQueue.Count > 0 && csvWriter != null)
                {
                    while (dataQueue.Count > 0)
                    {
                        string data = dataQueue.Dequeue();
                        csvWriter.WriteLine(data);
                    }
                    csvWriter.Flush();
                }
                yield return new WaitForSeconds(0.5f); // Write to disk every 0.5 seconds
            }
        }

        IEnumerator SendLiveData()
        {
            if (string.IsNullOrEmpty(serverUrl)) yield break;
            
            var data = new
            {
                session = currentSessionNumber,
                video = currentVideoName,
                timestamp = DateTime.UtcNow.ToString("o"),
                headsetId = headsetIdentifier
            };
            
            string json = JsonUtility.ToJson(data);
            
            using (UnityWebRequest www = new UnityWebRequest($"{serverUrl}/update", "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                
                yield return www.SendWebRequest();
                
                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"Live data upload failed: {www.error}");
                }
            }
        }

        public void UploadCurrentFile()
        {
            if (string.IsNullOrEmpty(currentFilePath) || !File.Exists(currentFilePath))
            {
                Debug.LogError("No file to upload!");
                return;
            }
            
            // Flush any pending data
            if (csvWriter != null)
            {
                csvWriter.Flush();
            }
            
            StartCoroutine(UploadFile(currentFilePath));
        }

        IEnumerator UploadFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"File not found: {filePath}");
                yield break;
            }
            
            byte[] fileData = File.ReadAllBytes(filePath);
            string fileName = Path.GetFileName(filePath);
            
            WWWForm form = new WWWForm();
            form.AddBinaryData("file", fileData, fileName, "text/csv");
            
            using (UnityWebRequest request = UnityWebRequest.Post($"{serverUrl}/upload", form))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"✅ Upload successful: {fileName}");
                }
                else
                {
                    Debug.LogError($"❌ Upload failed: {request.error}");
                }
            }
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && isSessionActive)
            {
                Debug.Log("🚨 App paused - ending session immediately");
                EndSession();
                csvWriter?.Flush();
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && isSessionActive)
            {
                Debug.Log("🚨 App lost focus - ending session immediately");
                EndSession();
                csvWriter?.Flush();
            }
        }

        void OnDestroy()
        {
            if (isSessionActive)
            {
                Debug.Log("🚨 Component destroyed - ending session");
                EndSession();
            }
            
            if (writeCoroutine != null)
            {
                StopCoroutine(writeCoroutine);
            }
            
            // Flush remaining data
            while (dataQueue.Count > 0)
            {
                string data = dataQueue.Dequeue();
                csvWriter?.WriteLine(data);
            }
            
            csvWriter?.Flush();
            csvWriter?.Close();
            
            // Upload file on destroy
            if (!string.IsNullOrEmpty(currentFilePath))
            {
                UploadCurrentFile();
            }
        }

        void OnApplicationQuit()
        {
            Debug.Log("🚨 Application quitting");
            
            // Force write END SESSION before Unity shuts down
            if (isSessionActive)
            {
                isSessionActive = false;
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string sessionEnd = $"# END SESSION {currentSessionNumber}: {timestamp} (APP_QUIT)";
                
                // Write directly to file instead of queue
                csvWriter?.WriteLine(sessionEnd);
                csvWriter?.Flush();
                
                // Increment for next session
                IncrementSessionNumber();
                
                Debug.Log($"📌 Session {currentSessionNumber - 1} ended at {timestamp} (App Quit)");
            }
            
            // Ensure everything is written
            while (dataQueue.Count > 0)
            {
                string data = dataQueue.Dequeue();
                csvWriter?.WriteLine(data);
            }
            
            csvWriter?.Flush();
            csvWriter?.Close();
        }
    }

}

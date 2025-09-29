using UnityEngine;
using VIVE.OpenXR.Samples.EyeTracker;

/// <summary>
/// Bridge component to connect VideoPlaylistController with EyeTrackingLogger
/// Add this to the same GameObject as VideoPlaylistController
/// </summary>
public class VideoLoggerIntegration : MonoBehaviour
{
    [Header("References")]
    public VideoPlaylistController playlistController;
    public EyeTrackingLogger eyeLogger;
    public UIScreenManager uiManager;

    void Start()
    {
        // Auto-find components if not assigned
        if (playlistController == null)
            playlistController = GetComponent<VideoPlaylistController>();
        
        if (eyeLogger == null)
            eyeLogger = FindFirstObjectByType<EyeTrackingLogger>();
        
        if (uiManager == null)
            uiManager = FindFirstObjectByType<UIScreenManager>();

        // Subscribe to video events
        if (playlistController != null)
        {
            playlistController.OnVideoChanged.AddListener(OnVideoChanged);
            playlistController.OnPlaybackStarted.AddListener(OnPlaybackStarted);
            playlistController.OnPlaybackStopped.AddListener(OnPlaybackStopped);
            playlistController.OnCategoryChanged.AddListener(OnCategoryChanged);
        }
        else
        {
            Debug.LogError("VideoPlaylistController not found!");
        }
    }

    void OnVideoChanged(int index, string videoName)
    {
        Debug.Log($"🎬 Video changed to: {videoName} (index {index})");
        
        if (eyeLogger != null)
        {
            // Set the current video name in the logger
            eyeLogger.SetCurrentVideo(videoName);
        }
        
        // Also notify UI manager if needed
        if (uiManager != null)
        {
            uiManager.OnVideoSelected(videoName);
        }
    }

    void OnPlaybackStarted()
    {
        Debug.Log("▶️ Playback started");
        // Video name should already be set from OnVideoChanged
    }

    void OnPlaybackStopped()
    {
        Debug.Log("⏸️ Playback stopped");
        
        if (eyeLogger != null)
        {
            // Clear video name when playback stops
            eyeLogger.ClearCurrentVideo();
        }
    }

    void OnCategoryChanged(string category, int videoCount)
    {
        Debug.Log($"📁 Category changed to: {category} ({videoCount} videos)");
        
        // Category changes might mean we're browsing, not playing yet
        if (eyeLogger != null)
        {
            eyeLogger.ClearCurrentVideo();
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (playlistController != null)
        {
            playlistController.OnVideoChanged.RemoveListener(OnVideoChanged);
            playlistController.OnPlaybackStarted.RemoveListener(OnPlaybackStarted);
            playlistController.OnPlaybackStopped.RemoveListener(OnPlaybackStopped);
            playlistController.OnCategoryChanged.RemoveListener(OnCategoryChanged);
        }
    }
}
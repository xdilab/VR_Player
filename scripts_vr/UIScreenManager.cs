/* 
Sundown project
Kirsten Hefney

Description : Updated UIScreenManager to work with the unified EyeTrackingLogger.
Manages UI objects and coordinates with the new single-file CSV logging system.

Last Update: 8/15/2025
*/

using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using VIVE.OpenXR.Samples.EyeTracker;
using System;

public class UIScreenManager : MonoBehaviour
{
    [Header("UI References")]
    public Transform homePosition;
    public Transform player;
    public GameObject homeMenu;
    public GameObject mainVideo;
    public GameObject favoritesMenu;
    public GameObject dale;
    public GameObject dale_nature;
    public GameObject dale_relaxation;
    public GameObject dale_travel;
    public GameObject gallery_n;
    public GameObject controls;

    [Header("Logging")]
    public EyeTrackingLogger eyeLogger;

    private string selectedCategory = "";
    private string currentVideoName = "";
    
    private string[] playlistCategories = new string[] {
        "Relaxation", "Focus", "Travel", "Nature", "Meditation"
    };

    private Dictionary<string, GameObject> daleCategories;
    private Dictionary<string, GameObject> galleryCategories;

    void Start()
    {
        if (eyeLogger == null)
        {
            eyeLogger = FindFirstObjectByType<EyeTrackingLogger>();
            if (eyeLogger == null)
            {
                Debug.LogError("❌ EyeTrackingLogger not found! Please assign it in the inspector.");
            }
        }

        daleCategories = new Dictionary<string, GameObject>
        {
            { "Relaxation", dale_relaxation },
            { "Nature", dale_nature },
            { "Travel", dale_travel }
        };

        galleryCategories = new Dictionary<string, GameObject>
        {
            { "Relaxation", dale_relaxation },
            { "Nature", dale_nature },
            { "Travel", dale_travel }
        };

        // Start a new session when the app starts
        StartNewSession();
    }

    void StartNewSession()
    {
        if (eyeLogger != null)
        {
            eyeLogger.StartSession();
            Debug.Log("🚀 New tracking session started");
        }
    }

    public void ToggleHomeView()
    {
        homeMenu.SetActive(!homeMenu.activeSelf);
    }

    // Called by category selection buttons
    public void SelectCategory(string categoryName)
    {
        selectedCategory = categoryName;
        Debug.Log("Category Selected: " + selectedCategory);
    }

    public void ShowSelectedGallery()
    {
        homeMenu.SetActive(false);
        if (galleryCategories.TryGetValue(selectedCategory, out GameObject gallery))
        {
            gallery.SetActive(true);
            Debug.Log($"Opened {selectedCategory} Gallery");
        }
        else
        {
            Debug.LogWarning("No gallery UI mapped for: " + selectedCategory);
        }
    }

    // Called when user clicks "Start Watching" on the home menu
    public void StartWatching()
    {
        if (string.IsNullOrEmpty(selectedCategory))
        {
            int randomIndex = UnityEngine.Random.Range(0, playlistCategories.Length);
            selectedCategory = playlistCategories[randomIndex];
            Debug.LogWarning("No category selected — randomizing...");
        }

        homeMenu.SetActive(false);
        mainVideo.SetActive(true);

        Debug.Log("Switched to video view for: " + selectedCategory);
        Debug.Log("🎥 Waiting for user to hit PLAY...");
    }

    // Called when user hits PLAY on the main video screen
    public void OnPlayButtonPressed()
    {
        // Generate video name based on category (you can customize this)
        currentVideoName = $"{selectedCategory}_Video_{UnityEngine.Random.Range(1, 100)}";
        
        // Tell the logger what video is playing
        if (eyeLogger != null)
        {
            eyeLogger.SetCurrentVideo(currentVideoName);
        }
        
        toDale(selectedCategory);
        
        Debug.Log($"▶️ Playing video: {currentVideoName}");
    }

    // Called by VideoPlaylistController when a specific video is selected
    public void OnVideoSelected(string videoName)
    {
        currentVideoName = videoName;
        
        if (eyeLogger != null)
        {
            eyeLogger.SetCurrentVideo(videoName);
        }
        
        Debug.Log($"🎬 Video selected: {videoName}");
    }

    // Shows the correct Dale video panel
    public void toDale(string selectedCategory)
    {
        mainVideo.SetActive(false);

        if (daleCategories.TryGetValue(selectedCategory, out GameObject target))
        {
            target.SetActive(true);
        }
        else
        {
            Debug.LogWarning("No UI mapped for category: " + selectedCategory);
            dale.SetActive(true);
        }

        Debug.Log("Switched to Dale View");
    }

    // Return from dale to Video screen
    public void ReturnFromDale()
    {     
        if (player != null && homePosition != null)
        {
            player.position = homePosition.position;
            player.rotation = homePosition.rotation;
            Debug.Log("🧍 Player teleported in front of Home UI.");
        }

    }

    // NOT ADDED - Potential favorites video
    public void toSelectedGallery()
    {
        homeMenu.SetActive(false);
        favoritesMenu.SetActive(true);
        Debug.Log("Favorites Menu");
    }

    // NOT ADDED 
    public void ReturnToHome_Gallery()
    {
        gallery_n.SetActive(false);
        homeMenu.SetActive(true);
        Debug.Log("Returned to Home View");
    }

    // Return to home from Main video
    public void ReturnToHome()
    {
        // Clear current video
        if (eyeLogger != null)
        {
            eyeLogger.ClearCurrentVideo();
        }

        mainVideo.SetActive(false);
        homeMenu.SetActive(true);

        Debug.Log("Returned to Home View");
    }

    public void Panorama()
    {
        homeMenu.SetActive(false);
        controls.SetActive(true);
    }

    public void return_Panorama()
    {
        controls.SetActive(false);
        if (player != null && homePosition != null)
        {
            player.position = homePosition.position;
            player.rotation = homePosition.rotation;
            Debug.Log("🧍 Player teleported in front of Home UI.");
        }
        homeMenu.SetActive(true);
        Debug.Log("Returned to Video View from Panorama");
    }

    // Upload current log file manually
    public void SyncLogs()
    {
        if (eyeLogger != null)
        {
            eyeLogger.UploadCurrentFile();
            Debug.Log("📤 Manual log sync initiated");
        }
        else
        {
            Debug.LogError("❌ Cannot sync logs - EyeTrackingLogger not found!");
        }
    }

    // Manual upload button for testing
    public void ManualUpload()
    {
        Debug.Log("🔄 Manual upload triggered");
        SyncLogs();
    }

    // End session and upload when app is closing
    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            if (eyeLogger != null)
            {
                eyeLogger.EndSession();
                eyeLogger.UploadCurrentFile();
            }
        }
        else
        {
            // Resume - start new session
            if (eyeLogger != null)
            {
                eyeLogger.StartSession();
            }
        }
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            // Lost focus - end session
            if (eyeLogger != null)
            {
                eyeLogger.EndSession();
            }
        }
        else
        {
            // Regained focus - start new session
            if (eyeLogger != null)
            {
                eyeLogger.StartSession();
            }
        }
    }

    void OnDestroy()
    {
        // Make sure session is ended and uploaded
        if (eyeLogger != null)
        {
            eyeLogger.EndSession();
            eyeLogger.UploadCurrentFile();
        }
    }

    void OnApplicationQuit()
    {
        OnDestroy();
    }
}
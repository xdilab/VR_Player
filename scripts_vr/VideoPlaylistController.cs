using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;

public class VideoPlaylistController : MonoBehaviour
{
    [Header("Video Player")]
    public VideoPlayer playlistVideoPlayer;

    [Header("Gallery UI")]
    public GameObject galleryRoot;      // parent of side+main panels
    public TMP_Text galleryTitleText;   // TMP title

    [Header("Gallery Settings")]
    public Transform galleryGridParent;
    public GameObject galleryItemButtonPrefab;     // MUST be a Prefab asset (not a scene object)
    public Sprite defaultThumbnail;
    public bool useThumbnailsFromStreamingAssets = true;

    [Header("Events")]
    public UnityEvent<string, int> OnCategoryChanged;
    public UnityEvent<int, string> OnVideoChanged;
    public UnityEvent<int> OnGalleryBuilt;
    public UnityEvent OnPlaybackStarted;
    public UnityEvent OnPlaybackStopped;

    // Private fields
    private Dictionary<string, string[]> categoryPlaylists; // DEST paths (persistentDataPath)
    private Dictionary<string, string> destToStreamingUri;  // DEST -> StreamingAssets URI
    private string[] currentPlaylist;                       // array of DEST paths
    private string currentCategory;
    private int playlistCurrentIndex = 0;

    private bool playlistsReady = false;
    private string pendingCategory = null;

    private readonly string[] supportedCategories = { "Relaxation", "Travel", "Nature", "Focus", "Medititation" };

    void Awake()
    {
        if (galleryItemButtonPrefab && galleryItemButtonPrefab.scene.IsValid())
            Debug.LogError("galleryItemButtonPrefab must be a Prefab asset (from Project), not a scene object.");

        if (galleryRoot) galleryRoot.SetActive(false); // ensure hidden before Start
    }

    void Start()
    {
        playlistVideoPlayer.loopPointReached += OnVideoEnd;
        if (galleryRoot) galleryRoot.SetActive(false); // start hidden (double safety)
        StartCoroutine(InitAndroidPlaylists());        // Android-friendly loader using _list.txt
    }

    // -------------------- Android playlist loader (reads _list.txt) --------------------
    private IEnumerator InitAndroidPlaylists()
    {
        categoryPlaylists = new Dictionary<string, string[]>();
        destToStreamingUri = new Dictionary<string, string>();

        foreach (string rawCat in supportedCategories)
        {
            string category = rawCat.Trim();
            List<string> destVideoList = new List<string>();

            string listUri = Application.streamingAssetsPath + "/Videos/" + category + "/_list.txt";
            using (var req = UnityWebRequest.Get(listUri))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"No _list.txt for {category} at {listUri} → {req.error}");
                    categoryPlaylists[category] = destVideoList.ToArray();
                    continue;
                }

                var names = req.downloadHandler.text
                    .Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(n => n.EndsWith(".mp4", System.StringComparison.OrdinalIgnoreCase)
                             || n.EndsWith(".mov", System.StringComparison.OrdinalIgnoreCase)
                             || n.EndsWith(".m4v", System.StringComparison.OrdinalIgnoreCase)
                             || n.EndsWith(".webm", System.StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                foreach (var fileName in names)
                {
                    string streamingUri = Application.streamingAssetsPath + "/Videos/" + category + "/" + fileName;
                    string destPath = Path.Combine(Application.persistentDataPath, "Videos", category, fileName);

                    destVideoList.Add(destPath);
                    if (!destToStreamingUri.ContainsKey(destPath))
                        destToStreamingUri.Add(destPath, streamingUri);
                }
            }

            categoryPlaylists[category] = destVideoList.ToArray();
            Debug.Log($"📁(Android) {category}: {destVideoList.Count} videos from _list.txt");
        }

        playlistsReady = true;
        Debug.Log("[VP] Playlists READY.");

        if (!string.IsNullOrEmpty(pendingCategory))
        {
            var queued = pendingCategory; pendingCategory = null;
            SetCategory(queued); // user clicked early; now safe to open
        }
        // else: do nothing. Stay hidden until the user clicks a category.
    }

    // -------------------- Category / Gallery --------------------
    public void SetCategory(string category)
    {
        if (!playlistsReady)
        {
            pendingCategory = category; // queue user click until lists load
            Debug.Log($"[VP] Queued SetCategory('{category}') until playlists load.");
            return;
        }

        if (categoryPlaylists == null || !categoryPlaylists.ContainsKey(category))
        {
            Debug.LogWarning($"⚠️ Category not recognized: {category}. Creating empty playlist.");
            if (categoryPlaylists == null) categoryPlaylists = new Dictionary<string, string[]>();
            categoryPlaylists[category] = new string[0];
        }

        currentCategory = category;
        currentPlaylist = categoryPlaylists[category];
        playlistCurrentIndex = 0;

        if (galleryRoot) galleryRoot.SetActive(true);
        if (galleryTitleText) galleryTitleText.text = $"{category} ({currentPlaylist.Length} Videos)";

        if (currentPlaylist.Length == 0)
            Debug.LogWarning($"⚠️ No videos found for category: {category}");

        PlayerPrefs.SetString("vp_last_category", category);
        PlayerPrefs.SetInt("vp_last_index", 0);
        PlayerPrefs.Save();

        RebuildGallery();
        OnCategoryChanged?.Invoke(category, currentPlaylist.Length);

        Debug.Log($"🎥 Set category to {category} with {currentPlaylist.Length} videos");
    }

    public void RebuildGallery()
    {
        if (galleryGridParent == null || galleryItemButtonPrefab == null)
        {
            Debug.LogWarning("⚠️ Gallery components not set up");
            return;
        }

        for (int i = galleryGridParent.childCount - 1; i >= 0; i--)
            Destroy(galleryGridParent.GetChild(i).gameObject);

        if (currentPlaylist == null)
        {
            OnGalleryBuilt?.Invoke(0);
            return;
        }

        for (int i = 0; i < currentPlaylist.Length; i++)
        {
            GameObject buttonObj = Instantiate(galleryItemButtonPrefab, galleryGridParent);

            // Button click
            Button button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                int index = i;
                button.onClick.AddListener(() => PlayVideoAtIndex(index));
            }

            string destPath = currentPlaylist[i];
            string fileName = Path.GetFileNameWithoutExtension(destPath);

            // Handle Title TMP component
            var titleTMP = buttonObj.transform.Find("Title")?.GetComponent<TMP_Text>();
            if (titleTMP != null)
            {
                titleTMP.text = fileName;
            }
            else
            {
                // Fallback: look for any TMP_Text component (backward compatibility)
                var tmp = buttonObj.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null) tmp.text = fileName;
                else
                {
                    var textComponent = buttonObj.GetComponentInChildren<Text>(true);
                    if (textComponent != null) textComponent.text = fileName;
                }
            }

            // Handle Duration TMP component
            var durationTMP = buttonObj.transform.Find("Duration")?.GetComponent<TMP_Text>();
            if (durationTMP != null)
            {
                durationTMP.text = "Loading...";
                StartCoroutine(GetVideoDuration(destPath, durationTMP));
            }

            // Thumbnail: prefer child "Thumbnail", fallback to any Image
            Image imageComponent = null;
            var tf = buttonObj.transform.Find("Thumbnail");
            if (tf) imageComponent = tf.GetComponent<Image>();
            if (imageComponent == null) imageComponent = buttonObj.GetComponentInChildren<Image>(true);

            if (imageComponent != null)
            {
                imageComponent.preserveAspect = true;
                StartCoroutine(AssignThumbnailFromStreaming(imageComponent, destPath)); // Android-safe
            }
        }

        if (galleryTitleText && !string.IsNullOrEmpty(currentCategory))
            galleryTitleText.text = $"{currentCategory} ({currentPlaylist.Length} Videos)";

        // Force layout to update positions/sizes this frame
        var rt = galleryGridParent as RectTransform;
        if (rt) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        OnGalleryBuilt?.Invoke(currentPlaylist.Length);
        Debug.Log($"🖼️ Gallery rebuilt with {currentPlaylist.Length} items");
    }

    private IEnumerator GetVideoDuration(string destPath, TMP_Text durationTMP)
    {
        // Create a temporary VideoPlayer to get duration
        GameObject tempGO = new GameObject("TempVideoPlayer");
        VideoPlayer tempPlayer = tempGO.AddComponent<VideoPlayer>();
        tempPlayer.renderMode = VideoRenderMode.RenderTexture;
        tempPlayer.playOnAwake = false;
        
        // Check if video exists locally first
        if (File.Exists(destPath))
        {
            tempPlayer.url = destPath;
        }
        else if (destToStreamingUri.TryGetValue(destPath, out var streamingUri))
        {
            tempPlayer.url = streamingUri;
        }
        else
        {
            durationTMP.text = "Unknown";
            Destroy(tempGO);
            yield break;
        }

        tempPlayer.Prepare();
        
        // Wait for preparation to complete
        while (!tempPlayer.isPrepared)
        {
            yield return null;
        }

        // Get duration and format it
        double durationSeconds = tempPlayer.length;
        string formattedDuration = FormatDuration(durationSeconds);
        
        if (durationTMP != null) // Check if component still exists
        {
            durationTMP.text = formattedDuration;
        }

        Destroy(tempGO);
    }

    private string FormatDuration(double totalSeconds)
    {
        if (totalSeconds <= 0)
            return "Unknown";

        int hours = (int)(totalSeconds / 3600);
        int minutes = (int)((totalSeconds % 3600) / 60);
        int seconds = (int)(totalSeconds % 60);

        if (hours > 0)
            return $"{hours}:{minutes:D2}:{seconds:D2}";
        else
            return $"{minutes}:{seconds:D2}";
    }

    private IEnumerator AssignThumbnailFromStreaming(Image img, string destPath)
    {
        if (!useThumbnailsFromStreamingAssets || string.IsNullOrEmpty(currentCategory))
        {
            img.sprite = defaultThumbnail;
            yield break;
        }

        // Get original StreamingAssets URI, then swap extension to .png
        if (!destToStreamingUri.TryGetValue(destPath, out var streamingUri))
        {
            img.sprite = defaultThumbnail;
            yield break;
        }

        int dot = streamingUri.LastIndexOf('.');
        string pngUri = (dot >= 0 ? streamingUri.Substring(0, dot) : streamingUri) + ".png";

        using (var req = UnityWebRequestTexture.GetTexture(pngUri))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                var tex = DownloadHandlerTexture.GetContent(req);
                img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            else
            {
                img.sprite = defaultThumbnail;
            }
        }
    }

    // -------------------- Playback --------------------
    public void PlayVideoAtIndex(int index)
    {
        if (currentPlaylist == null || currentPlaylist.Length == 0)
        {
            Debug.LogWarning("⚠️ No playlist available");
            return;
        }

        if (index < 0 || index >= currentPlaylist.Length)
        {
            Debug.LogWarning($"⚠️ Video index {index} out of bounds (0-{currentPlaylist.Length - 1})");
            return;
        }

        playlistCurrentIndex = index;

        string destPath = currentPlaylist[playlistCurrentIndex];
        PlayerPrefs.SetInt("vp_last_index", playlistCurrentIndex);
        PlayerPrefs.Save();

        StartCoroutine(EnsureLocalThenPlay(destPath, index));
    }

    private IEnumerator EnsureLocalThenPlay(string destPath, int indexForEvent)
    {
        // Copy once from StreamingAssets to persistentDataPath (VideoPlayer can't read from APK)
        if (!File.Exists(destPath))
        {
            if (!destToStreamingUri.TryGetValue(destPath, out var streamingUri))
            {
                Debug.LogError($"No streaming URI mapped for: {destPath}");
                yield break;
            }

            string dir = Path.GetDirectoryName(destPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            using (var req = new UnityWebRequest(streamingUri, UnityWebRequest.kHttpVerbGET))
            {
                req.downloadHandler = new DownloadHandlerFile(destPath);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Failed to copy video to {destPath}: {req.error}");
                    yield break;
                }
            }
        }

        // Play from local file
        playlistVideoPlayer.url = destPath;
        playlistVideoPlayer.Play();
        OnPlaybackStarted?.Invoke();

        string fileName = Path.GetFileNameWithoutExtension(destPath);
        OnVideoChanged?.Invoke(indexForEvent, fileName);

        Debug.Log($"🎬 Playing video {indexForEvent + 1}/{currentPlaylist.Length}: {fileName}");
    }

    public void TogglePlayPause()
    {
        if (playlistVideoPlayer.isPlaying)
        {
            playlistVideoPlayer.Pause();
            OnPlaybackStopped?.Invoke();
            Debug.Log("⏸️ Video paused");
        }
        else
        {
            playlistVideoPlayer.Play();
            OnPlaybackStarted?.Invoke();
            Debug.Log("▶️ Video resumed");
        }
    }

    public void PlayNextVideo()
    {
        if (currentPlaylist == null || currentPlaylist.Length == 0) return;
        int nextIndex = (playlistCurrentIndex + 1) % currentPlaylist.Length;
        PlayVideoAtIndex(nextIndex);
    }

    public void PlayPreviousVideo()
    {
        if (currentPlaylist == null || currentPlaylist.Length == 0) return;
        int prevIndex = (playlistCurrentIndex - 1 + currentPlaylist.Length) % currentPlaylist.Length;
        PlayVideoAtIndex(prevIndex);
    }

    public void StopPlayback()
    {
        playlistVideoPlayer.Pause();
        OnPlaybackStopped?.Invoke();
        Debug.Log("⏹️ Video stopped");
    }

    public void HideGallery()
    {
        StopPlayback();
        if (galleryRoot) galleryRoot.SetActive(false);
    }

    // -------------------- Session restore --------------------
    // Intentionally NOT auto-restoring on boot to keep gallery hidden until user clicks.
    // Call RestoreLastSession() manually from a "Continue" button if you want that UX.
    private void RestoreLastSession()
    {
        if (!PlayerPrefs.HasKey("vp_last_category")) return;

        string lastCategory = PlayerPrefs.GetString("vp_last_category");
        int lastIndex = PlayerPrefs.GetInt("vp_last_index", 0);

        if (categoryPlaylists.ContainsKey(lastCategory) && categoryPlaylists[lastCategory].Length > 0)
        {
            SetCategory(lastCategory);

            if (lastIndex < currentPlaylist.Length)
            {
                playlistCurrentIndex = lastIndex;
                // URL assigned on play; index is enough
            }

            Debug.Log($"🔄 Restored session: {lastCategory}, video {lastIndex}");
        }
    }

    void OnVideoEnd(VideoPlayer vp)
    {
        OnPlaybackStopped?.Invoke();
        Debug.Log("🔚 Video ended, playing next");
        PlayNextVideo();
    }
}
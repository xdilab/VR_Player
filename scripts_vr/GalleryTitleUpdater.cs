using UnityEngine;
using UnityEngine.UI; // Use TMPro instead if TMP
using TMPro;

public class GalleryTitleUpdater : MonoBehaviour
{
    public TMP_Text galleryTitleText; // change to Text if not using TMP

    public void UpdateGalleryTitle(string category, int count)
    {
        if (galleryTitleText != null)
        {
            galleryTitleText.text = $"{category} ({count} Videos)";
        }
    }
}

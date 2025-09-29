// Assets/Editor/GenerateVideoLists.cs
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class GenerateVideoLists : IPreprocessBuildWithReport
{
    // Add/trim extensions as needed
    static readonly string[] k_Exts = { ".mp4", ".mov", ".m4v", ".webm" };

    public int callbackOrder => 0;

    [MenuItem("Tools/VR/Regenerate _list.txt for StreamingAssets Videos")]
    public static void RegenerateNow()
    {
        string videosRoot = Path.Combine(Application.streamingAssetsPath, "Videos");
        if (!Directory.Exists(videosRoot))
        {
            Debug.LogWarning($"StreamingAssets Videos folder not found: {videosRoot}");
            return;
        }

        int totalFiles = 0;
        foreach (var categoryDir in Directory.GetDirectories(videosRoot))
        {
            string categoryName = Path.GetFileName(categoryDir);

            // Gather files in this category (top-level only)
            var files = Directory.GetFiles(categoryDir, "*", SearchOption.TopDirectoryOnly)
                .Where(p =>
                {
                    var ext = Path.GetExtension(p).ToLowerInvariant();
                    if (!k_Exts.Contains(ext)) return false;
                    // ignore hidden/temp/meta
                    string name = Path.GetFileName(p);
                    if (name.StartsWith("._") || name.EndsWith(".meta")) return false;
                    return true;
                })
                .Select(Path.GetFileName) // only filename, no path
                .OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();

            string listPath = Path.Combine(categoryDir, "_list.txt");
            File.WriteAllLines(listPath, files);
            totalFiles += files.Length;

            Debug.Log($"[_list.txt] {categoryName}: wrote {files.Length} entries → {listPath}");
        }

        AssetDatabase.Refresh();
        Debug.Log($"Done. Total video entries written across categories: {totalFiles}");
    }

    // Auto-generate before build so device always has fresh lists
    public void OnPreprocessBuild(BuildReport report)
    {
        RegenerateNow();
    }
}
#endif

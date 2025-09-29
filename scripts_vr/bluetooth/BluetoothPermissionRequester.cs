using UnityEngine;

public class BluetoothPermissionRequester : MonoBehaviour
{
    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (AndroidJavaObject activity = GetUnityActivity())
        using (AndroidJavaClass activityCompat = new AndroidJavaClass("androidx.core.app.ActivityCompat"))
        using (AndroidJavaClass permissionClass = new AndroidJavaClass("android.Manifest$permission"))
        {
            string[] permissions = new string[]
            {
                permissionClass.GetStatic<string>("BLUETOOTH_CONNECT"),
                permissionClass.GetStatic<string>("BLUETOOTH_SCAN")
            };

            activityCompat.CallStatic(
                "requestPermissions",
                activity,
                permissions,
                1 // requestCode
            );
        }
#endif
    }

    AndroidJavaObject GetUnityActivity()
    {
        using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }
    }
}
using System;
using UnityEngine;

namespace ReelsMods
{
    /// <summary>
    /// Starts Reel from inside this app.
    ///
    /// Reel's package name is not published anywhere, and Android 11+ hides
    /// other apps' packages unless you ask for them - so we ask
    /// (QUERY_ALL_PACKAGES + a &lt;queries&gt; intent in the manifest), then
    /// match the launcher entry whose label reads "Reel".
    /// </summary>
    public static class GameLauncher
    {
        private const string WantedLabel = "Reel";

        public static bool IsAndroid
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        private static AndroidJavaObject Activity()
        {
            var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            player.Dispose();
            return activity;
        }

        /// <summary>Find the launcher component for Reel.</summary>
        public static bool FindReel(out string pkg, out string cls)
        {
            pkg = null;
            cls = null;

#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject activity = null;
            try
            {
                activity = Activity();
                using (var pm = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.MAIN"))
                {
                    intent.Call<AndroidJavaObject>("addCategory", "android.intent.category.LAUNCHER");
                    using (var list = pm.Call<AndroidJavaObject>("queryIntentActivities", intent, 0))
                    {
                        int n = list.Call<int>("size");
                        for (int i = 0; i < n; i++)
                        {
                            AndroidJavaObject info = null;
                            try
                            {
                                using (var ri = list.Call<AndroidJavaObject>("get", i))
                                {
                                    if (ri == null) continue;
                                    info = ri.Get<AndroidJavaObject>("activityInfo");
                                    if (info == null) continue;

                                    string p = info.Get<string>("packageName");
                                    string c = info.Get<string>("name");

                                    string label = "";
                                    try { label = pm.Call<string>("getApplicationLabel", info); }
                                    catch (Exception) { }

                                    if (Matches(p, label))
                                    {
                                        pkg = p;
                                        cls = c;
                                        return true;
                                    }
                                }
                            }
                            finally
                            {
                                if (info != null) info.Dispose();
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ReelMods] package scan failed: " + e.Message);
            }
            finally
            {
                if (activity != null) activity.Dispose();
            }
#endif
            return false;
        }

        private static bool Matches(string pkg, string label)
        {
            if (!string.IsNullOrEmpty(label) &&
                string.Equals(label.Trim(), WantedLabel, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(pkg) &&
                pkg.IndexOf("reel", StringComparison.OrdinalIgnoreCase) >= 0 &&
                pkg.IndexOf("reelsmods") < 0)
                return true;

            return false;
        }

        public static bool Launch(string pkg, string cls)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject activity = null;
            try
            {
                activity = Activity();
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.MAIN"))
                {
                    intent.Call<AndroidJavaObject>("addCategory", "android.intent.category.LAUNCHER");
                    using (var comp = new AndroidJavaObject("android.content.ComponentName", pkg, cls))
                    {
                        intent.Call<AndroidJavaObject>("setComponent", comp);
                    }
                    // FLAG_ACTIVITY_NEW_TASK - we are not the game's task
                    intent.Call<AndroidJavaObject>("addFlags", 0x10000000);
                    activity.Call("startActivity", intent);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ReelMods] launch failed: " + e.Message);
                return false;
            }
            finally
            {
                if (activity != null) activity.Dispose();
            }
#else
            return false;
#endif
        }
    }
}

using System;
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Editor.Build
{
    /// <summary>
    /// Stamps each build with when it was made (UTC seconds) in StreamingAssets/build_stamp.txt. The auto-updater
    /// only replaces a build with a newer one (see BuildUpdate.IsNewer), and tools/make_update.py puts the same stamp
    /// in the CDN's file list.
    /// </summary>
    public class BuildStamp : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            Directory.CreateDirectory("Assets/StreamingAssets");
            File.WriteAllText("Assets/StreamingAssets/build_stamp.txt", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        }
    }
}

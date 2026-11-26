using System.IO;
using UnityEditor;
using UnityEngine;
using YARG.Song;

namespace Editor
{
    /// <summary>Turns Resources/OnlineApiKey.txt into its scrambled form (see OnlineKey), in place. Safe to run twice.</summary>
    public static class ScrambleApiKey
    {
        private const string PATH = "Assets/Resources/OnlineApiKey.txt";

        [MenuItem("LunarRhythm/Scramble the API key")]
        public static void Scramble()
        {
            if (!File.Exists(PATH))
            {
                EditorUtility.DisplayDialog("Scramble the API key", "There's no " + PATH + ".", "OK");
                return;
            }
            string stored = File.ReadAllText(PATH).Trim();
            string key = OnlineKey.Read(stored);
            if (string.IsNullOrEmpty(key))
            {
                EditorUtility.DisplayDialog("Scramble the API key", "The file couldn't be read as a key.", "OK");
                return;
            }
            string scrambled = OnlineKey.Scramble(key);
            if (scrambled == stored)
            {
                EditorUtility.DisplayDialog("Scramble the API key", "It's already scrambled.", "OK");
                return;
            }
            File.WriteAllText(PATH, scrambled);
            AssetDatabase.ImportAsset(PATH);
            Debug.Log("OnlineApiKey.txt is scrambled now.");
        }
    }
}

using UnityEditor;
using YARG.Song;

namespace YARG.Editor
{
    public static class LocalSongsToggle
    {
        private const string MENU = "LunarRhythm/Load Local Songs (debug)";

        [MenuItem(MENU)]
        private static void Toggle()
        {
            bool enabled = !EditorPrefs.GetBool(SongContainer.LOAD_LOCAL_SONGS_PREF, false);
            EditorPrefs.SetBool(SongContainer.LOAD_LOCAL_SONGS_PREF, enabled);
            UnityEditor.Menu.SetChecked(MENU, enabled);
        }

        [MenuItem(MENU, true)]
        private static bool ToggleValidate()
        {
            UnityEditor.Menu.SetChecked(MENU, EditorPrefs.GetBool(SongContainer.LOAD_LOCAL_SONGS_PREF, false));
            return true;
        }
    }
}

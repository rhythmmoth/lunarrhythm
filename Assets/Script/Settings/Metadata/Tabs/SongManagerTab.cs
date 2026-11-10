using UnityEngine;
using UnityEngine.AddressableAssets;
using YARG.Menu.Navigation;
using YARG.Menu.Settings;

namespace YARG.Settings.Metadata
{
    public class SongManagerTab : MetadataTab
    {
        // Prefabs needed for this tab type
        private static GameObject _songManagerHeader;

        public SongManagerTab(string name, string icon = "Generic") : base(name, icon)
        {
        }

        public override void BuildSettingTab(Transform settingContainer, NavigationGroup navGroup)
        {
            if (_songManagerHeader == null)
            {
                 _songManagerHeader = Addressables
                    .LoadAssetAsync<GameObject>("SettingTab/SongManagerHeader")
                    .WaitForCompletion();
            }

            // Spawn in the special header
            Object.Instantiate(_songManagerHeader, settingContainer);

            // Build the rest of the metadata
            base.BuildSettingTab(settingContainer, navGroup);
        }
    }
}
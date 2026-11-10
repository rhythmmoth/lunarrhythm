using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Menu.MusicLibrary;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Menu.Settings;
using YARG.Settings;

namespace YARG.Menu.Main
{
    public class MainMenu : MonoBehaviour
    {
        private static bool _antiPiracyDialogShown;

        [SerializeField]
        private TextMeshProUGUI _versionText;
        [SerializeField]
        private Sprite _panelSprite;
        [SerializeField]
        private RectTransform _menuOptions;

        private void Start()
        {
            _versionText.text = GlobalVariables.Instance.CurrentVersion;
            NewsTicker.Create(transform, _versionText.font, _panelSprite);
            NowPlayingCard.Create(transform, _versionText.font, _panelSprite);
            MirrorSelectionBars();

            // Show the anti-piracy dialog if it hasn't been shown already
            // Also only show it once per game launch
            if (!_antiPiracyDialogShown && SettingsManager.Settings.ShowAntiPiracyDialog)
            {
                DialogManager.Instance.ShowOneTimeMessage(
                    "Menu.Dialog.AntiPiracy",
                    () =>
                    {
                        SettingsManager.Settings.ShowAntiPiracyDialog = false;
                        SettingsManager.SaveSettings();
                    });

                _antiPiracyDialogShown = true;
            }

            if (SettingsMenu.ConsumeOpenOnNextMenuLoad())
            {
                SettingsMenu.Instance.gameObject.SetActive(true);
            }
        }

        private void OnEnable()
        {
            // Set navigation scheme
            Navigator.Instance.PushScheme(new NavigationScheme(new()
            {
                NavigationScheme.Entry.NavigateSelect,
                NavigationScheme.Entry.NavigateUp,
                NavigationScheme.Entry.NavigateDown,
                new NavigationScheme.Entry(MenuAction.Select, "Menu.Main.GoToCurrentlyPlaying", CurrentlyPlaying)
            }, true));

            // The now playing card replaces the help bar's player here; keep that one running (disabling it
            // stops the music), just invisible
            SetHelpBarPlayerVisible(false);
        }

        /// <summary>
        /// The menu is centered, so give each entry's selection bar a mirrored twin on the right. The twin is a
        /// child of the bar, so it shows and hides with it.
        /// </summary>
        private void MirrorSelectionBars()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_menuOptions);
            foreach (RectTransform entry in _menuOptions)
            {
                if (entry.Find("Selection") is not RectTransform bar)
                {
                    continue;
                }

                var twin = (RectTransform) Instantiate(bar.gameObject, bar).transform;
                twin.name = "Selection (mirror)";
                twin.anchorMin = twin.anchorMax = new Vector2(1f, 0.5f);
                twin.pivot = new Vector2(1f, 0.5f);
                twin.localScale = new Vector3(-1f, 1f, 1f);
                // The bar ends 25 left of the text, so the twin starts 25 right of it
                twin.anchoredPosition = new Vector2(entry.rect.width - bar.anchoredPosition.x * 2f, 0f);
                twin.sizeDelta = bar.sizeDelta;
                // Unselected bars are inactive and the copy keeps that; it should follow its parent instead
                twin.gameObject.SetActive(true);
                bar.GetComponent<Image>().color = twin.GetComponent<Image>().color = MainMenuUI.Accent;
            }
        }

        private void OnDisable()
        {
            Navigator.Instance?.PopScheme();
            SetHelpBarPlayerVisible(true);
        }

        private static void SetHelpBarPlayerVisible(bool visible)
        {
            if (HelpBar.Instance == null || HelpBar.Instance.MusicPlayer == null)
            {
                return;
            }

            var player = HelpBar.Instance.MusicPlayer.gameObject;
            if (!player.TryGetComponent(out CanvasGroup group))
            {
                group = player.AddComponent<CanvasGroup>();
            }
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = group.interactable = visible;
        }

        public void CurrentlyPlaying()
        {
            MusicLibraryMenu.RequestGoToCurrentlyPlaying(MusicPlayer.NowPlaying);
            QuickPlay();
        }

        public void QuickPlay()
        {
            var menu = MenuManager.Instance.PushMenu(MenuManager.Menu.MusicLibrary, false);

            MusicLibraryMenu.LibraryMode = MusicLibraryMode.QuickPlay;

            menu.gameObject.SetActive(true);
        }

        public void Practice()
        {
            var menu = MenuManager.Instance.PushMenu(MenuManager.Menu.MusicLibrary, false);

            MusicLibraryMenu.LibraryMode = MusicLibraryMode.Practice;

            menu.gameObject.SetActive(true);
        }

        public void Profiles()
        {
            MenuManager.Instance.PushMenu(MenuManager.Menu.ProfileList);
        }

        public void Replays()
        {
            MenuManager.Instance.PushMenu(MenuManager.Menu.History);
        }

        public void Credits()
        {
            MenuManager.Instance.PushMenu(MenuManager.Menu.Credits);
        }

        public void Settings()
        {
            SettingsMenu.Instance.gameObject.SetActive(true);
        }

        public void Exit()
        {
#if UNITY_EDITOR

            UnityEditor.EditorApplication.isPlaying = false;

#else
			Application.Quit();

#endif
        }

        public void OpenDiscord()
        {
            Application.OpenURL("https://discord.gg/example");
        }

        public void OpenTwitter()
        {
            Application.OpenURL("https://twitter.com/YARGGame");
        }

        public void OpenGithub()
        {
            Application.OpenURL("https://github.com/YARC-Official/YARG");
        }
    }
}

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
        [SerializeField]
        private Sprite _starSprite;
        [SerializeField]
        private TMP_FontAsset _headingFont;

        private AccountGate _gate;

        private void Awake()
        {
            // The main menu is the first menu to load, so it hands the shared fonts to the rest (Awake: before
            // OnEnable sets up the help bar)
            Moonlight.Body = _versionText.font;
            Moonlight.Heading = _headingFont ? _headingFont : _versionText.font;
        }

        private void Start()
        {
            _versionText.text = GlobalVariables.Instance.CurrentVersion;
            Persistent.AutoUpdater.CheckOnce(); // the first title screen of a run checks for a new build
            _versionText.color = Moonlight.Faint;
            if (transform.Find("Logo") is { } logo)
            {
                Moonlight.ShowWordmark(logo.GetComponent<Image>(), 760f);
                // The version sits centred just under the wordmark (it used to hang off the old logo's right side)
                var version = _versionText.rectTransform;
                if (version.parent == logo)
                {
                    version.anchorMin = new Vector2(0f, 0f);
                    version.anchorMax = new Vector2(1f, 0f);
                    version.pivot = new Vector2(0.5f, 1f);
                    version.sizeDelta = new Vector2(0f, version.sizeDelta.y);
                    version.anchoredPosition = new Vector2(0f, -8f);
                    _versionText.alignment = TMPro.TextAlignmentOptions.Top;
                }
            }
            NewsTicker.Create(transform, _versionText.font, _panelSprite);
            NowPlayingCard.Create(transform, _versionText.font, CurrentlyPlaying);
            _profile = ProfileCard.Create(transform, _versionText.font);
            // Last, so it covers everything: nobody plays until the account is signed in and allowed
            AccountGate.CreateUpdatePill(transform, _versionText.font, () => _gate?.ShowUpdate());
            _gate = AccountGate.Create(transform, _versionText.font, Exit);
            _gate.Check();
            // Someone's first time, once they're signed in and in: the tour (board NextTutorial)
            FirstRunTour.StartIfNew(this, () => _gate != null && !_gate.gameObject.activeSelf && YARG.Core.Song.Leaderboards.SignedIn);
            AddEntry("Leaderboard", "Menu.Main.Options.Leaderboard", "Leaderboard", "Quickplay", Leaderboard);
            AddEntry("Online", "Menu.Main.Options.Online", "Play online", "Quickplay", PlayOnline);
            AddEntry("Challenges", "Menu.Main.Options.Challenges", "Challenges", "Online", Challenges);
            StyleMenuEntries();

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

            // Back from a lobby song (or its score screen): straight back to the lobby, or to the open lobbies when
            // it was left mid-song
            if (YARG.Core.Song.Lobby.State != null || Online.LobbyPlay.BackToList)
            {
                PlayOnline();
            }

            if (SettingsMenu.ConsumeOpenOnNextMenuLoad())
            {
                SettingsMenu.Instance.gameObject.SetActive(true);
                Moonrise.Rise(SettingsMenu.Instance.gameObject);
            }
        }

        private void OnEnable()
        {
            // Back on the title screen: look at the account again (a ban takes effect here)
            if (_gate != null)
            {
                _gate.Check();
            }

            // Set navigation scheme
            Navigator.Instance.PushScheme(new NavigationScheme(new()
            {
                NavigationScheme.Entry.NavigateSelect,
                NavigationScheme.Entry.NavigateUp,
                NavigationScheme.Entry.NavigateDown,
                new NavigationScheme.Entry(MenuAction.Select, "Menu.Main.GoToCurrentlyPlaying", CurrentlyPlaying),
                // The board's Y: the profile card's popup (Challenges live there)
                new NavigationScheme.Entry(MenuAction.Yellow, "Menu.Main.Profile", () => _profile?.OpenPopup()),
            }, true));

            // The now playing card replaces the help bar's player here; keep that one running (disabling it
            // stops the music), just invisible
            SetHelpBarPlayerVisible(false);
        }

        /// <summary>
        /// Moonlight menu entries: Unbounded in sentence case, muted until selected, then moon cream with a crescent
        /// to the left. The crescent replaces the entry's selection bar, so it shows and hides with the selection.
        /// </summary>
        private void StyleMenuEntries()
        {
            foreach (RectTransform entry in _menuOptions)
            {
                var text = entry.GetComponentInChildren<Localization.LocalizeText>(true)?.GetComponent<TextMeshProUGUI>();
                if (text == null || !entry.TryGetComponent(out NavigatableButton button))
                {
                    continue;
                }

                text.font = Moonlight.Heading;
                text.fontStyle = FontStyles.Normal; // the old style forced capitals
                text.fontSize = 40f; // seven entries fit above the news bar (FixTitle board)
                text.characterSpacing = -1f;
                void Paint(bool selected) => text.color = selected ? Moonlight.Moon : Moonlight.MenuIdle;
                Paint(button.Selected);
                button.SelectionStateChanged += (_, selected, _) => Paint(selected);

                if (entry.Find("Selection") is RectTransform bar && bar.TryGetComponent(out Image marker))
                {
                    marker.sprite = Moonlight.Crescent;
                    marker.type = Image.Type.Simple;
                    marker.preserveAspect = true;
                    marker.color = Moonlight.Moon;
                    bar.anchorMin = bar.anchorMax = new Vector2(0f, 0.5f);
                    bar.pivot = new Vector2(1f, 0.5f);
                    bar.anchoredPosition = new Vector2(-22f, 0f);
                    bar.sizeDelta = new Vector2(28f, 28f);
                }
            }
            if (_menuOptions.TryGetComponent(out VerticalLayoutGroup column))
            {
                column.spacing = 14f;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_menuOptions);
        }

        /// <summary>
        /// Adds an entry right after another (Leaderboard after Play, Challenges after Leaderboard), copied from the
        /// Profiles entry so it looks the same (Practice, Replays and Credits are hidden).
        /// </summary>
        private void AddEntry(string name, string key, string fallback, string after, UnityEngine.Events.UnityAction click)
        {
            var previous = _menuOptions.Find(after);
            var source = _menuOptions.Find("Profiles");
            if (previous == null || source == null || !source.TryGetComponent(out NavigatableButton sourceButton))
            {
                return;
            }

            var entry = Instantiate(source.gameObject, _menuOptions);
            entry.name = name;
            entry.transform.SetSiblingIndex(previous.GetSiblingIndex() + 1);
            entry.GetComponentInChildren<Localization.LocalizeText>(true).GetComponent<TextMeshProUGUI>().text =
                Localization.LocalizationManager.TryGetLocalizedKey(key, out var label) ? label : fallback;

            var button = entry.GetComponent<NavigatableButton>();
            button.ReplaceOnClickEvent(click);
            // Up and down follow the group's own order, so put the copy after its neighbour there too
            var group = sourceButton.NavigationGroup;
            if (group != null && previous.TryGetComponent(out NavigatableButton previousButton))
            {
                group.InsertNavigatable(group.IndexOf(previousButton) + 1, button);
            }
        }

        private ProfileCard _profile;

        public void PlayOnline()
        {
            if (YARG.Core.Song.Leaderboards.UpdateRequired)
            {
                _gate?.ShowUpdate(); // lobbies need everyone on one version
                return;
            }
            Online.LobbyMenu.Open(transform);
        }

        public void Challenges()
        {
            ChallengesMenu.Open(transform, _versionText.font);
        }

        public void Leaderboard()
        {
            GlobalLeaderboardMenu.Open(transform, _versionText.font, _starSprite);
        }



        private void OnDisable()
        {
            Navigator.Instance?.PopScheme();
            // The help bar's old music widget stays hidden everywhere: Moonlight footers have none, the jukebox card is it
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

        /// <summary>Opens the game song the jukebox track promotes ("Play this song"); nothing if it has none.</summary>
        public void CurrentlyPlaying()
        {
            if (MusicPlayer.NowPlaying == null)
            {
                return;
            }
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
            Moonrise.Rise(SettingsMenu.Instance.gameObject);
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

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Core.Song;
using YARG.Menu.Navigation;
using YARG.Settings;

namespace YARG.Menu.Main
{
    /// <summary>
    /// Covers the title screen until the account may play: sign in first, a banned account sees only the ban, and
    /// offline play stops 24 hours after the server last confirmed the account. The server enforces all of this for
    /// anything online; this is what the player sees.
    /// </summary>
    public class AccountGate : MonoBehaviour
    {
        private enum Screen { None, Checking, SignIn, Waiting, Banned, Offline, Update }

        /// <summary>"Not now" on the update screen: it isn't shown again this launch; the pill under the profile card
        /// brings it back (board FixUpdate).</summary>
        public static bool UpdateDismissed { get; private set; }

        private static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(1);
        private static DateTime _lastAsked = DateTime.MinValue;

        /// <summary>True while playing offline inside the 24 hour window (the profile card says so).</summary>
        public static bool PlayingOffline { get; private set; }

        private Action _quit;
        private TMP_FontAsset _font;
        private RectTransform _panel;
        private readonly List<(CanvasGroup Group, float Alpha, bool Blocks)> _hidden = new();
        private Screen _screen = Screen.None;
        private bool _checking;

        public static AccountGate Create(Transform parent, TMP_FontAsset font, Action quit)
        {
            var root = new GameObject("Account Gate", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            var dim = root.GetComponent<Image>();
            dim.color = new Color(0.008f, 0.02f, 0.063f, 0.6f); // a raycast target, so nothing behind can be clicked
            Stretch(dim.rectTransform);
            root.SetActive(false);

            var gate = root.AddComponent<AccountGate>();
            gate._quit = quit;
            gate._font = font;
            return gate;
        }

        /// <summary>Checks the account (at most once a minute unless forced) and shows whatever screen that calls for.</summary>
        public void Check(bool force = false)
        {
            // The title screen starts behind the loading screen, before the saved sign-in is read
            if (LoadingScreen.IsActive)
            {
                CheckAfterLoading(force).Forget();
                return;
            }
            if (!Leaderboards.SignedIn)
            {
                Show(Screen.SignIn);
                return;
            }
            if (_checking || (!force && DateTime.UtcNow - _lastAsked < RecheckEvery && _screen == Screen.None))
            {
                return;
            }
            // Signed in but never confirmed (or long ago): wait for the server before letting anyone in
            if (Leaderboards.Banned)
            {
                Show(Screen.Banned);
            }
            else if (Leaderboards.OfflineTimeLeft <= TimeSpan.Zero)
            {
                Show(Screen.Checking);
            }
            CheckAsync().Forget();
        }

        private async UniTaskVoid CheckAfterLoading(bool force)
        {
            await UniTask.WaitUntil(() => !LoadingScreen.IsActive);
            if (this != null)
            {
                Check(force);
            }
        }

        private async UniTaskVoid CheckAsync()
        {
            _checking = true;
            var state = await Leaderboards.CheckAccount();
            _checking = false;
            _lastAsked = DateTime.UtcNow;
            if (this == null)
            {
                return;
            }

            PlayingOffline = state == Leaderboards.AccountState.Offline;
            Show(state switch
            {
                Leaderboards.AccountState.SignedOut => Screen.SignIn,
                Leaderboards.AccountState.Banned => Screen.Banned,
                Leaderboards.AccountState.Offline when Leaderboards.OfflineTimeLeft <= TimeSpan.Zero => Screen.Offline,
                Leaderboards.AccountState.Ok when Leaderboards.UpdateRequired && !UpdateDismissed => Screen.Update,
                _ => Screen.None,
            });
        }

        /// <summary>The update screen again (the pill, Play online): this build is too old for scores and lobbies.</summary>
        public void ShowUpdate() => Show(Screen.Update);

        private void OpenReleases() => Application.OpenURL(Leaderboards.RELEASES_URL);

        private void NotNow()
        {
            UpdateDismissed = true;
            Show(Screen.None);
        }

        private void Update()
        {
            // Sign-in finishes in the browser (or from Settings); pick it up here
            if (_screen is Screen.SignIn or Screen.Waiting && Leaderboards.SignedIn)
            {
                Check(true);
            }
        }

        private void Show(Screen screen)
        {
            if (screen == _screen)
            {
                return;
            }
            bool wasOpen = _screen != Screen.None;
            _screen = screen;
            if (screen == Screen.None)
            {
                Close();
                return;
            }
            if (!wasOpen)
            {
                Open();
            }
            Build(screen);
        }

        private void Open()
        {
            // Hide the title screen behind: some of its text draws over anything, whatever the order
            foreach (Transform child in transform.parent)
            {
                if (child == transform)
                {
                    continue;
                }
                if (!child.TryGetComponent(out CanvasGroup group))
                {
                    group = child.gameObject.AddComponent<CanvasGroup>();
                }
                _hidden.Add((group, Moonrise.Settle(group), group.blocksRaycasts));
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            Moonrise.Rise(gameObject);
        }

        private void Close()
        {
            foreach (var (group, alpha, blocks) in _hidden)
            {
                if (group != null)
                {
                    Moonrise.Return(group, alpha);
                    group.blocksRaycasts = blocks;
                }
            }
            _hidden.Clear();
            PopScheme();
            gameObject.SetActive(false);
        }

        private bool _schemePushed;

        private void PushScheme(NavigationScheme scheme)
        {
            PopScheme();
            Navigator.Instance.PushScheme(scheme);
            _schemePushed = true;
        }

        private void PopScheme()
        {
            if (_schemePushed)
            {
                Navigator.Instance?.PopScheme();
                _schemePushed = false;
            }
        }

        private void OnDestroy() => PopScheme();

        private void Build(Screen screen)
        {
            if (_panel != null)
            {
                Destroy(_panel.gameObject);
            }
            _panel = Moonlight.Panel("Panel", transform, 33, new Color(0.024f, 0.04f, 0.11f, 0.94f)).rectTransform;
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(780f, 0f);
            var layout = _panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(54, 54, 48, 48);
            layout.spacing = 22f;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            _panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            switch (screen)
            {
                case Screen.Checking:
                    Title("Checking your account");
                    Body("One moment…");
                    PushScheme(new NavigationScheme(new(), false));
                    break;
                case Screen.SignIn:
                case Screen.Waiting:
                    Title("Sign in to play");
                    Body("LunarRhythm uses your Discord account for songs, scores and your profile. You'll finish signing in in your browser, then come back here.");
                    if (screen == Screen.Waiting)
                    {
                        Body("Waiting for your browser…", Moonlight.Moon);
                    }
                    Buttons(("Sign in with Discord", SignIn), ("Quit", _quit));
                    Body("Only your Discord name and picture are read.", Moonlight.Muted, 20f);
                    Scheme(SignIn);
                    break;
                case Screen.Banned:
                    Title("This account is banned");
                    var who = Body($"You can't play while signed in as {Leaderboards.PlayerName}.");
                    who.richText = false; // a Discord name is anyone's text
                    if (!string.IsNullOrWhiteSpace(Leaderboards.BanReason))
                    {
                        var reason = Body("Reason: " + Leaderboards.BanReason, Moonlight.Danger);
                        reason.richText = false;
                    }
                    Body("To appeal, message staff in the LunarRhythm Discord.", Moonlight.Muted, 20f);
                    Buttons(("Sign out", SignOut), ("Quit", _quit));
                    Scheme(SignOut);
                    break;
                case Screen.Update:
                    Title("A new version is out");
                    Body("This version can't send scores any more. Leaderboards, XP, challenges and online lobbies need the newest one.");
                    Body($"You have {GlobalVariables.Instance.CurrentVersion}", Moonlight.Muted, 21f);
                    Body("You can keep playing songs in the meantime. Your scores won't be sent.", Moonlight.Muted, 20f);
                    Buttons(("Get the update", OpenReleases), ("Not now", NotNow));
                    PushScheme(new NavigationScheme(new()
                    {
                        new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.Confirm", OpenReleases),
                        new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", NotNow),
                    }, false));
                    break;
                case Screen.Offline:
                    Title("Can't reach the server");
                    Body("Offline play needs a check with the server in the last 24 hours. Check your connection; your saved scores are kept and will send once you're back.");
                    Buttons(("Try again", () => Check(true)), ("Quit", _quit));
                    Scheme(() => Check(true));
                    break;
            }
        }

        private void Scheme(Action primary)
        {
            PushScheme(new NavigationScheme(new()
            {
                new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.Confirm", primary),
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Main.Options.Exit", _quit),
            }, false));
        }

        private void SignIn()
        {
            // The same flow as Settings, which also fetches the songs that were skipped while signed out
            SettingsManager.Settings.SignInWithDiscord();
            Show(Screen.Waiting);
        }

        private void SignOut()
        {
            SignOutAsync().Forget();
        }

        private async UniTaskVoid SignOutAsync()
        {
            await Leaderboards.SignOut();
            if (this != null)
            {
                Show(Screen.SignIn);
            }
        }

        private void Title(string text)
        {
            var label = Moonlight.Label("Title", _panel, Moonlight.Heading ? Moonlight.Heading : _font, 38f, Moonlight.Text);
            label.text = text;
        }

        private TextMeshProUGUI Body(string text, Color? color = null, float size = 24f)
        {
            var label = Moonlight.Label("Text", _panel, _font, size, color ?? Moonlight.Soft);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.text = text;
            return label;
        }

        /// <summary>A cream primary pill and an outlined second one, side by side.</summary>
        private void Buttons((string Label, Action Click) primary, (string Label, Action Click) secondary)
        {
            var row = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<HorizontalLayoutGroup>();
            row.transform.SetParent(_panel, false);
            row.spacing = 16f;
            row.childControlHeight = row.childControlWidth = true;
            row.childForceExpandWidth = true;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 68f;
            Pill(row.transform, primary.Label, primary.Click, Moonlight.Moon, Moonlight.Night);
            Pill(row.transform, secondary.Label, secondary.Click, Moonlight.Raised, Moonlight.Text);
        }

        private void Pill(Transform parent, string text, Action click, Color fill, Color ink)
        {
            var pill = Moonlight.Panel(text, parent, 34, fill);
            pill.raycastTarget = true;
            var button = pill.gameObject.AddComponent<Button>();
            button.targetGraphic = pill;
            button.onClick.AddListener(() => click?.Invoke());
            var label = Moonlight.Label("Text", pill.rectTransform, _font, 24f, ink);
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.text = text;
            Stretch(label.rectTransform);
        }

        /// <summary>The pill under the profile card while this build is too old: "Update for leaderboards", reopening the
        /// update screen. Hidden otherwise.</summary>
        public static void CreateUpdatePill(Transform parent, TMP_FontAsset font, Action open)
        {
            var pill = Moonlight.Panel("Update Pill", parent, 45, Moonlight.PanelFill);
            pill.raycastTarget = true;
            pill.gameObject.AddComponent<Button>().onClick.AddListener(() => open());
            Moonlight.HoverWash(pill, 45);
            var rect = pill.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(42f, -153f);
            rect.sizeDelta = new Vector2(360f, 90f);
            var title = Moonlight.Label("Title", rect, font, 22.5f, Moonlight.Text);
            title.text = "Update for leaderboards";
            title.fontWeight = FontWeight.Bold;
            title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.offsetMin = new Vector2(30f, 0f);
            title.rectTransform.offsetMax = new Vector2(-24f, -15f);
            title.alignment = TextAlignmentOptions.BottomLeft;
            var detail = Moonlight.Label("Detail", rect, font, 19.5f, Moonlight.Muted);
            detail.text = "Your scores aren't being sent";
            detail.rectTransform.anchorMin = Vector2.zero;
            detail.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            detail.rectTransform.offsetMin = new Vector2(30f, 15f);
            detail.rectTransform.offsetMax = new Vector2(-24f, 0f);
            detail.alignment = TextAlignmentOptions.TopLeft;
            pill.gameObject.AddComponent<UpdatePill>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }

    /// <summary>Shows its pill only while the build is too old and the update screen was put off.</summary>
    public class UpdatePill : MonoBehaviour
    {
        private CanvasGroup _group;

        private void Awake() => _group = gameObject.AddComponent<CanvasGroup>();

        private void Update()
        {
            bool show = Leaderboards.UpdateRequired && AccountGate.UpdateDismissed;
            _group.alpha = show ? 1f : 0f;
            _group.blocksRaycasts = show;
        }
    }
}

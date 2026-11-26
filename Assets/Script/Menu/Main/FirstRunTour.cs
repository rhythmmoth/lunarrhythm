using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Helpers;
using YARG.Menu.Navigation;
using YARG.Player;

namespace YARG.Menu.Main
{
    /// <summary>
    /// The first-time tour (board NextTutorial): welcome, make your player, bind your controls, how to play, first song.
    /// It shows once, on the first title screen after sign-in for someone with no players yet; Settings can replay it.
    /// The player and controls steps open the real Players screen with a small coach card beside it, so nothing here
    /// does what the game's own screens do; they move on by themselves when a player is connected / the screen is left.
    /// </summary>
    public class FirstRunTour : MonoBehaviour
    {
        private enum Step { Welcome, Player, Controls, HowToPlay, FirstSong }
        private const int STEPS = 5;

        private static string DoneFile => Path.Combine(PathHelper.PersistentDataPath, "tour_done.txt");
        private static FirstRunTour _running;

        private MainMenu _menu;
        private Func<bool> _gateClear;
        private Step _step;
        private bool _schemePushed;
        private float _shownAt;

        private RectTransform _root, _card;
        private Image _dim;
        private TextMeshProUGUI _tag, _title, _body, _primaryLabel, _secondaryLabel;
        private Image[] _dots;
        private GameObject _art;
        private Action _primary, _secondary;

        /// <summary>From the title screen: starts the tour if this is someone's first time.</summary>
        public static void StartIfNew(MainMenu menu, Func<bool> gateClear)
        {
            if (_running != null || File.Exists(DoneFile)) return;
            // Anyone who already made a player (everyone from before the tour) doesn't need it
            if (PlayerContainer.Profiles.Any(p => !p.IsBot))
            {
                MarkDone();
                return;
            }
            WaitThenStart(menu, gateClear).Forget();
        }

        /// <summary>Settings' "Replay the tour": closes Settings and starts from the welcome.</summary>
        public static bool Replay()
        {
            var menu = FindObjectOfType<MainMenu>(true);
            if (menu == null || _running != null) return false;
            Settings.SettingsMenu.Instance.gameObject.SetActive(false);
            Begin(menu, () => true);
            return true;
        }

        private static async UniTaskVoid WaitThenStart(MainMenu menu, Func<bool> gateClear)
        {
            // Behind the loading screen, the sign-in gate or the updater, nobody would see it
            await UniTask.WaitUntil(() => menu == null || (!LoadingScreen.IsActive && gateClear() && menu.isActiveAndEnabled));
            await UniTask.Delay(600);
            if (menu != null && _running == null && gateClear() && menu.isActiveAndEnabled) Begin(menu, gateClear);
        }

        private static void Begin(MainMenu menu, Func<bool> gateClear)
        {
            var root = new GameObject("First Run Tour", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(root);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000; // over the menus, under the updater
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var tour = root.AddComponent<FirstRunTour>();
            tour._menu = menu;
            tour._gateClear = gateClear;
            tour.Build((RectTransform) root.transform);
            _running = tour;
            tour.Show(Step.Welcome);
        }

        private static void MarkDone()
        {
            try
            {
                File.WriteAllText(DoneFile, DateTime.UtcNow.ToString("O"));
            }
            catch (IOException) { }
        }

        private void Finish()
        {
            HideTitle(false);
            MarkDone();
            PopScheme();
            _running = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            PopScheme();
            if (_running == this) _running = null;
        }

        // ---------- the steps ----------

        private void Show(Step step)
        {
            _step = step;
            _shownAt = Time.unscaledTime;
            for (int i = 0; i < _dots.Length; i++)
            {
                _dots[i].color = i <= (int) step ? Moonlight.Moon : Moonlight.Keycap;
                _dots[i].rectTransform.sizeDelta = new Vector2(i == (int) step ? 24f : 9f, 9f);
            }
            _tag.text = $"{(int) step + 1} of {STEPS}";
            _art.SetActive(step == Step.HowToPlay);
            switch (step)
            {
                case Step.Welcome:
                    Modal("Welcome to LunarRhythm",
                        "A minute to get you playing: make your player, check your controls, see how notes work, then pick a song. Every step can be skipped.",
                        "Let's go", () => Show(Step.Player), "Skip tour", Finish);
                    break;
                case Step.Player:
                    Coach("Make your player",
                        "Choose <b>Add a player</b>, pick the pad or keyboard you'll play on, and give it a name. This moves on once your player is connected.",
                        "Next", () => Show(Step.Controls), "Skip tour", Finish);
                    _menu.Profiles();
                    break;
                case Step.Controls:
                    Coach("Set your controls",
                        "Open your player with <b>Edit player</b>. On <b>Controls</b>, pick each lane left to right and press the pad or key for it, then Overdrive. Go back to the title screen when you're done.",
                        "Next", () => Show(Step.HowToPlay), "Skip tour", Finish);
                    break;
                case Step.HowToPlay:
                    Modal("Hit notes as they reach the line",
                        "Tap a note's lane when it meets the strike line. A lift: hold it, then let go at its end. A miss, or tapping an empty lane, breaks your combo. When the Overdrive bar is full, press Overdrive for double points. Solos score like any other notes.",
                        "Next", () => Show(Step.FirstSong), "Skip", () => Show(Step.FirstSong));
                    break;
                case Step.FirstSong:
                    Modal("Pick your first song",
                        "Start with an easy difficulty and a song you know. Every finished run earns XP toward your next level. That's the tour: Settings › General can show it again.",
                        "Choose a song", () =>
                        {
                            Finish();
                            _menu.QuickPlay();
                        }, "Later", Finish);
                    break;
            }
        }

        private void Update()
        {
            // The coach steps follow what's happening on the real screens
            if (_menu == null)
            {
                Finish(); // the menu scene went away (a song started)
                return;
            }
            bool coaching = _step is Step.Player or Step.Controls;
            if (coaching && Time.unscaledTime - _shownAt > 1f && _menu.isActiveAndEnabled)
            {
                Show(Step.HowToPlay); // left the Players screen: back on the title
            }
            else if (_step == Step.Player && PlayerContainer.Players.Count > 0)
            {
                Show(Step.Controls);
            }
        }

        // A centred card over a dimmed screen, answered with the menu buttons
        private void Modal(string title, string body, string primary, Action onPrimary, string secondary, Action onSecondary)
        {
            Fill(title, body, primary, onPrimary, secondary, onSecondary);
            _dim.gameObject.SetActive(true);
            HideTitle(true);
            Place(_card, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(760f, _step == Step.HowToPlay ? 560f : 330f));
            PopScheme();
            Navigator.Instance.PushScheme(new NavigationScheme(new()
            {
                new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.Continue", () => _primary?.Invoke()),
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", () => _secondary?.Invoke()),
            }, false));
            _schemePushed = true;
        }

        // A small card beside the real screen, which keeps its own controls
        private void Coach(string title, string body, string primary, Action onPrimary, string secondary, Action onSecondary)
        {
            PopScheme();
            Fill(title, body, primary, onPrimary, secondary, onSecondary);
            _dim.gameObject.SetActive(false);
            HideTitle(false);
            Place(_card, new Vector2(0f, 0f), new Vector2(70f, 130f), new Vector2(600f, 380f)); // over the list's empty end, clear of the player's buttons
        }

        private void Fill(string title, string body, string primary, Action onPrimary, string secondary, Action onSecondary)
        {
            _title.text = title;
            _body.text = body;
            _primaryLabel.text = primary;
            _secondaryLabel.text = secondary;
            _primary = onPrimary;
            _secondary = onSecondary;
        }

        // Some of the title screen's text draws over anything whatever the order (as the sign-in gate found): hide it
        private void HideTitle(bool hide)
        {
            if (_menu == null) return;
            if (!_menu.TryGetComponent(out CanvasGroup group)) group = _menu.gameObject.AddComponent<CanvasGroup>();
            group.alpha = hide ? 0f : 1f;
        }

        private void PopScheme()
        {
            if (!_schemePushed) return;
            _schemePushed = false;
            Navigator.Instance?.PopScheme();
        }

        // ---------- the card ----------

        private void Build(RectTransform root)
        {
            _root = root;
            _dim = new GameObject("Dim", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            _dim.transform.SetParent(root, false);
            _dim.color = new Color(0.008f, 0.02f, 0.063f, 0.72f); // a raycast target: the menu behind waits
            Stretch(_dim.rectTransform);

            var heading = Moonlight.Heading != null ? Moonlight.Heading : TMP_Settings.defaultFontAsset;
            var body = Moonlight.Body != null ? Moonlight.Body : TMP_Settings.defaultFontAsset;
            var card = Moonlight.Panel("Card", root, 21, new Color(0.024f, 0.04f, 0.125f, 0.96f));
            card.raycastTarget = true;
            _card = card.rectTransform;

            _tag = Moonlight.Label("Step", _card, body, 19f, Moonlight.Muted);
            _tag.fontWeight = FontWeight.Bold;
            Inset(_tag.rectTransform, 26f, 28f, 200f, 28f);
            var dots = new GameObject("Dots", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            dots.SetParent(_card, false);
            dots.anchorMin = dots.anchorMax = dots.pivot = new Vector2(1f, 1f);
            dots.anchoredPosition = new Vector2(-28f, -36f);
            dots.sizeDelta = new Vector2(120f, 9f);
            var layout = dots.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 7f;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            _dots = Enumerable.Range(0, STEPS).Select(i => Moonlight.Panel("Dot", dots, 4, Moonlight.Keycap)).ToArray();

            _title = Moonlight.Label("Title", _card, heading, 30f, Moonlight.Text);
            Inset(_title.rectTransform, 64f, 28f, 28f, 44f);
            _body = Moonlight.Label("Body", _card, body, 22f, Moonlight.Soft);
            _body.textWrappingMode = TextWrappingModes.Normal;
            _body.overflowMode = TextOverflowModes.Overflow;
            _body.alignment = TextAlignmentOptions.TopLeft;
            _body.lineSpacing = 8f;
            Inset(_body.rectTransform, 120f, 28f, 28f, 160f);

            _art = BuildArt(_card);

            var primary = Moonlight.Panel("Primary", _card, 26, Moonlight.Moon);
            primary.raycastTarget = true;
            primary.rectTransform.anchorMin = primary.rectTransform.anchorMax = primary.rectTransform.pivot = new Vector2(1f, 0f);
            primary.rectTransform.anchoredPosition = new Vector2(-28f, 26f);
            primary.rectTransform.sizeDelta = new Vector2(230f, 54f);
            primary.gameObject.AddComponent<Button>().onClick.AddListener(() => _primary?.Invoke());
            _primaryLabel = Moonlight.Label("Label", primary.rectTransform, body, 22f, new Color(0.043f, 0.063f, 0.149f));
            _primaryLabel.fontWeight = FontWeight.Bold;
            _primaryLabel.alignment = TextAlignmentOptions.Center;
            Stretch(_primaryLabel.rectTransform);

            var secondary = new GameObject("Secondary", typeof(RectTransform), typeof(Image), typeof(Button));
            secondary.transform.SetParent(_card, false);
            secondary.GetComponent<Image>().color = Color.clear;
            var srect = (RectTransform) secondary.transform;
            srect.anchorMin = srect.anchorMax = srect.pivot = new Vector2(0f, 0f);
            srect.anchoredPosition = new Vector2(20f, 26f);
            srect.sizeDelta = new Vector2(200f, 54f);
            secondary.GetComponent<Button>().onClick.AddListener(() => _secondary?.Invoke());
            _secondaryLabel = Moonlight.Label("Label", srect, body, 21f, Moonlight.Muted);
            Stretch(_secondaryLabel.rectTransform);
            _secondaryLabel.rectTransform.offsetMin = new Vector2(8f, 0f);
        }

        // How to play: a strip of highway with a tap, a lift and the strike line
        private static GameObject BuildArt(RectTransform card)
        {
            var art = Moonlight.Panel("Art", card, 10, new Color(0.008f, 0.024f, 0.08f, 0.9f));
            var rect = art.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 100f);
            rect.sizeDelta = new Vector2(704f, 170f);
            Color[] lanes = { new(0.23f, 0.83f, 0.55f), new(0.93f, 0.23f, 0.27f), new(0.96f, 0.81f, 0.24f), new(0.18f, 0.45f, 0.94f), new(0.96f, 0.54f, 0.12f) };
            const float left = 222f, lane = 52f, strike = 32f;
            for (int i = 0; i < 5; i++)
            {
                var mark = Moonlight.Panel("Mark", rect, 6, lanes[i] * new Color(1f, 1f, 1f, 0.45f));
                Box(mark.rectTransform, left + i * lane + 6f, strike - 6f, lane - 12f, 12f);
            }
            var line = Moonlight.Panel("Strike line", rect, 1, Moonlight.Moon);
            Box(line.rectTransform, left - 8f, strike - 1f, lane * 5f + 16f, 2f);
            Box(Moonlight.Panel("Tap", rect, 6, lanes[0]).rectTransform, left + 6f, 112f, lane - 12f, 12f);
            Box(Moonlight.Panel("Note", rect, 6, lanes[2]).rectTransform, left + 2f * lane + 6f, 70f, lane - 12f, 12f);
            Box(Moonlight.Panel("Lift", rect, 3, lanes[4] * new Color(1f, 1f, 1f, 0.6f)).rectTransform, left + 4f * lane + lane / 2f - 4f, 64f, 8f, 80f);
            Box(Moonlight.Panel("Lift head", rect, 6, lanes[4]).rectTransform, left + 4f * lane + 6f, 58f, lane - 12f, 12f);
            var body = Moonlight.Body != null ? Moonlight.Body : TMP_Settings.defaultFontAsset;
            Caption(rect, body, "tap", left - 70f, 112f, Moonlight.Muted);
            Caption(rect, body, "strike line", left - 120f, strike - 1f, Moonlight.Moon);
            Caption(rect, body, "lift: hold, then let go", left + 5f * lane + 18f, 100f, lanes[4]);
            return art.gameObject;
        }

        private static void Caption(RectTransform parent, TMP_FontAsset font, string text, float x, float y, Color color)
        {
            var label = Moonlight.Label("Caption", parent, font, 17f, color);
            label.text = text;
            Box(label.rectTransform, x, y - 12f, 200f, 24f);
        }

        private static void Box(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // top: distance from the card's top; left/right: from its sides
        private static void Inset(RectTransform rect, float top, float left, float right, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}

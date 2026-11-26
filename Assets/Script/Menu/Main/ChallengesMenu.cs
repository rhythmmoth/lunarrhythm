using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Core.Song;
using YARG.Menu.Navigation;
using YARG.Song;

namespace YARG.Menu.Main
{
    /// <summary>
    /// The Challenges screen (MChallenges): daily goals, the weekly setlist and the season's challenges, side by side.
    /// Everything comes from the server's /lb/progress; nothing is worked out here. Built in code over the main menu,
    /// like the global leaderboard. Sizes are the board's (1280×720) × 1.5.
    /// </summary>
    public class ChallengesMenu : MonoBehaviour
    {
        private static readonly Color Done = Moonlight.Good;

        private TMP_FontAsset _font;
        private RectTransform _columns;
        private TextMeshProUGUI _message;
        private CancellationTokenSource _cancel;
        private List<(CanvasGroup Group, float Alpha, bool Blocks)> _hidden;

        public static void Open(Transform parent, TMP_FontAsset font)
        {
            // Hide the main menu while this is open, as the leaderboard does
            var hidden = new List<(CanvasGroup Group, float Alpha, bool Blocks)>();
            foreach (Transform child in parent)
            {
                if (!child.TryGetComponent(out CanvasGroup group)) group = child.gameObject.AddComponent<CanvasGroup>();
                hidden.Add((group, Moonrise.Settle(group), group.blocksRaycasts));
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
            var root = new GameObject("Challenges", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            var background = root.GetComponent<Image>();
            background.color = Color.clear;
            Stretch(background.rectTransform);
            Moonrise.Rise(root);

            var menu = root.AddComponent<ChallengesMenu>();
            menu._font = font;
            menu._hidden = hidden;
            menu.Build(background.rectTransform);
        }

        private void Build(RectTransform root)
        {
            var back = Moonlight.Panel("Back", root, 33, Moonlight.Raised);
            back.raycastTarget = true;
            Pin(back.rectTransform, new Vector2(42f, -33f), new Vector2(66f, 66f));
            back.gameObject.AddComponent<Button>().onClick.AddListener(Close);
            var chevron = new GameObject("Chevron", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            chevron.transform.SetParent(back.rectTransform, false);
            chevron.sprite = Moonlight.Chevron;
            chevron.color = Moonlight.Text;
            chevron.raycastTarget = false;
            chevron.rectTransform.anchorMin = chevron.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            chevron.rectTransform.sizeDelta = new Vector2(27f, 27f);

            var title = Moonlight.Label("Title", root, Moonlight.Heading ? Moonlight.Heading : _font, 36f, Moonlight.Text);
            title.text = "Challenges";
            Pin(title.rectTransform, new Vector2(132f, -33f), new Vector2(320f, 66f));
            var subtitle = Moonlight.Label("Subtitle", root, _font, 21f, Moonlight.Muted);
            subtitle.text = "Only accepted runs on online songs count";
            Pin(subtitle.rectTransform, new Vector2(420f, -33f), new Vector2(1000f, 66f));

            _columns = new GameObject("Columns", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            _columns.SetParent(root, false);
            Stretch(_columns);
            _columns.offsetMin = new Vector2(42f, 102f);
            _columns.offsetMax = new Vector2(-42f, -135f);
            var row = _columns.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 24f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;

            _message = Moonlight.Label("Message", root, _font, 24f, Moonlight.Muted);
            _message.alignment = TextAlignmentOptions.Center;
            Stretch(_message.rectTransform);
            _message.text = "Loading…";

            Navigator.Instance.PushScheme(new NavigationScheme(new()
            {
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", Close),
            }, true));

            _cancel = new CancellationTokenSource();
            Load(_cancel.Token).Forget();
        }

        private void Close() => Destroy(gameObject);

        private void OnDestroy()
        {
            _cancel?.Cancel();
            _cancel?.Dispose();
            Navigator.Instance?.PopScheme();
            foreach (var (group, alpha, blocks) in _hidden)
            {
                if (group != null)
                {
                    Moonrise.Return(group, alpha);
                    group.blocksRaycasts = blocks;
                }
            }
        }

        private async UniTaskVoid Load(CancellationToken cancel)
        {
            var progress = await Leaderboards.GetProgress();
            if (cancel.IsCancellationRequested || this == null)
            {
                return;
            }
            if (progress == null)
            {
                _message.text = "Couldn't load your challenges. Check your connection and try again.";
                return;
            }
            _message.gameObject.SetActive(false);

            var now = OnlineSongs.ServerNow;
            var daily = Column("DAILY", $"New in {Until(progress.Daily.Ends, now)}", Moonlight.Moon,
                "Three small goals, new every day.");
            foreach (var c in progress.Daily.Items)
            {
                Card(daily, c.Label, c.Done ? "Done" : $"+{c.Xp:N0} XP", Moonlight.Moon,
                    c.Count > 0 ? c.Progress / (float) c.Count : 0f, c.Done ? "Done" : $"{c.Progress} of {c.Count}", c.Done);
            }

            var weekly = progress.Weekly;
            int count = weekly.Songs.Count;
            var setlist = Column("WEEKLY SETLIST", $"Ends {Until(weekly.Ends, now)} from now", Moonlight.Featured,
                count == 0 ? "No setlist this week."
                    : $"Finish all {count} for +{weekly.Bonus:N0} XP. Each gives {weekly.Multiplier:0.#}× XP this week.");
            foreach (var s in weekly.Songs)
            {
                var (name, artist) = Names(s.Shortname);
                Card(setlist, name, s.Done ? "Done" : $"{weekly.Multiplier:0.#}× XP", Moonlight.Featured, s.Done ? 1f : 0f, artist, s.Done);
            }

            if (progress.Season is { } season)
            {
                string when = season.Running && DateTime.TryParse(season.End, out var end)
                    ? $"{Math.Max(0, (int) Math.Ceiling((end - now).TotalDays))} days left · level {season.Level}"
                    : $"Ended · level {season.Level}";
                var column = Column($"SEASON {season.Number}", when, Moonlight.Season,
                    "Rewards are looks only; nothing in the game is locked.");
                foreach (var c in season.Challenges)
                {
                    Card(column, c.Label, c.Reward ?? (c.Xp > 0 ? $"+{c.Xp:N0} XP" : ""), Moonlight.Season,
                        c.Count > 0 ? c.Progress / (float) c.Count : 0f,
                        c.Note ?? (c.Done ? "Done" : $"{c.Progress} of {c.Count}"), c.Done);
                }
            }
            else
            {
                Column("SEASON", "", Moonlight.Season, "No season is running.");
            }
        }

        /// <summary>"6 h 12 m" or "3 days" until a time.</summary>
        private static string Until(DateTime when, DateTime now)
        {
            var left = when.ToUniversalTime() - now;
            if (left.TotalDays >= 2) return $"{(int) left.TotalDays} days";
            return left.TotalMinutes <= 0 ? "a moment" : $"{(int) left.TotalHours} h {left.Minutes} m";
        }

        /// <summary>An online song's title and artist from the library, or its shortname if it isn't there.</summary>
        private static (string Name, string Artist) Names(string shortname)
        {
            var song = SongContainer.Songs.FirstOrDefault(s => OnlineSongs.ShortnameOf(s.ActualLocation) == shortname);
            return song != null ? (song.Name, song.Artist) : (shortname, "");
        }

        private RectTransform Column(string title, string when, Color color, string intro)
        {
            var panel = Moonlight.Panel(title, _columns, 27, Moonlight.PanelFill).rectTransform;
            var stack = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(30, 30, 30, 30);
            stack.spacing = 15f;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            var head = Line(panel, 27f);
            var label = Label(head, title, 18f, color, Moonlight.Heading ? Moonlight.Heading : _font);
            label.characterSpacing = 12f;
            label.fontWeight = FontWeight.Medium;
            var right = Label(head, when, 19.5f, Moonlight.Muted);
            right.alignment = TextAlignmentOptions.MidlineRight;

            var text = Label(Line(panel, 0f), intro, 21f, Moonlight.Soft);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.GetComponentInParent<LayoutElement>().preferredHeight = 60f;
            return panel;
        }

        private void Card(RectTransform column, string label, string reward, Color color, float progress, string note, bool done)
        {
            var card = Moonlight.Panel("Card", column, 21, Moonlight.Raised).rectTransform;
            card.gameObject.AddComponent<LayoutElement>().preferredHeight = 108f;

            var title = Label(card, label, 22.5f, Moonlight.Text);
            title.fontWeight = FontWeight.Bold;
            title.richText = false;
            Inset(title.rectTransform, 18f, 21f, 120f, 30f);
            var right = Label(card, reward, 19.5f, color);
            right.fontWeight = FontWeight.SemiBold;
            right.alignment = TextAlignmentOptions.MidlineRight;
            right.richText = false;
            Inset(right.rectTransform, 18f, 21f, 0f, 30f);

            var track = Moonlight.Panel("Track", card, 3, Moonlight.Keycap).rectTransform;
            Inset(track, 57f, 21f, 0f, 6f);
            var fill = Moonlight.Panel("Fill", track, 3, done ? Done : color).rectTransform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fill.gameObject.SetActive(progress > 0f);

            var text = Label(card, note, 19.5f, Moonlight.Muted);
            text.richText = false;
            Inset(text.rectTransform, 72f, 21f, 0f, 27f);
        }

        private RectTransform Line(Transform parent, float height)
        {
            var line = new GameObject("Line", typeof(RectTransform), typeof(LayoutElement)).GetComponent<RectTransform>();
            line.SetParent(parent, false);
            var size = line.GetComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = height;
            size.flexibleHeight = 0f;
            return line;
        }

        private TextMeshProUGUI Label(Transform parent, string text, float size, Color color, TMP_FontAsset font = null)
        {
            var label = Moonlight.Label("Text", parent, font ? font : _font, size, color);
            label.text = text;
            Stretch(label.rectTransform);
            return label;
        }

        // From the top-left of the parent, inset left and right
        private static void Inset(RectTransform rect, float top, float left, float right, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-left - right, -top);
        }

        private static void Pin(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}

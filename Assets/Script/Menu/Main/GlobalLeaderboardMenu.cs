using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Core.Song;
using YARG.Menu.MusicLibrary;
using YARG.Menu.Navigation;

namespace YARG.Menu.Main
{
    /// <summary>
    /// The global leaderboard, opened from the main menu: every player's total score, stars per instrument and full
    /// combos across all ranked songs. Built in code over the main menu, in the Moonlight look: one flat panel over
    /// the moving background, the signed-in player's row in moon cream.
    /// </summary>
    public class GlobalLeaderboardMenu : MonoBehaviour
    {
        private const int LIMIT = 100; // the server's maximum
        private const float ROW = 81f;
        private const float GAP = 3f;
        private const float PAD = 21f;
        // Column widths, right to left: FCs, four instruments, score
        private const float FC_WIDTH = 90f;
        private const float STAR_WIDTH = 111f;
        private const float SCORE_WIDTH = 225f;
        private const float NAME_LEFT = 150f;

        // Lead, bass, drums, vocals: the API's names, which are also the icons' sprite names
        private static readonly string[] Instruments = { "guitar", "bass", "drums", "vocals" };

        private TMP_FontAsset _font;
        private TextMeshProUGUI _subtitle;
        private RectTransform _viewport;
        private RectTransform _content;
        private RectTransform _footer;
        private CancellationTokenSource _cancel;
        // All time, or the season board once the server says there is one
        private bool _showSeason;
        private Leaderboards.GlobalBoard _seasonBoard;
        private RectTransform _tabs;
        private TextMeshProUGUI _scoreHeading;
        private float _meTop = -1f;
        private List<(CanvasGroup Group, float Alpha, bool Blocks)> _hidden;

        public static void Open(Transform parent, TMP_FontAsset font, Sprite star)
        {
            // Hide the main menu while this is open: some of its text draws over anything, whatever the order
            var hidden = new List<(CanvasGroup Group, float Alpha, bool Blocks)>();
            foreach (Transform child in parent)
            {
                if (!child.TryGetComponent(out CanvasGroup group))
                {
                    group = child.gameObject.AddComponent<CanvasGroup>();
                }
                hidden.Add((group, Moonrise.Settle(group), group.blocksRaycasts));
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }

            // Clear but still a raycast target, so the moving background shows and nothing behind can be clicked
            var root = new GameObject("Global Leaderboard", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            var background = root.GetComponent<Image>();
            background.color = Color.clear;
            Stretch(background.rectTransform);
            Moonrise.Rise(root);

            var menu = root.AddComponent<GlobalLeaderboardMenu>();
            menu._font = font;
            menu._hidden = hidden;
            menu.Build(background.rectTransform);
        }

        private void Build(RectTransform root)
        {
            // Header: round back button, title, a quiet line of context
            var back = Moonlight.Panel("Back", root, 33, Moonlight.Raised);
            back.raycastTarget = true;
            back.rectTransform.anchorMin = back.rectTransform.anchorMax = back.rectTransform.pivot = new Vector2(0f, 1f);
            back.rectTransform.anchoredPosition = new Vector2(42f, -33f);
            back.rectTransform.sizeDelta = new Vector2(66f, 66f);
            back.gameObject.AddComponent<Button>().onClick.AddListener(Close);
            var chevron = Text("Chevron", back.rectTransform, "‹", 44f, Moonlight.Text, FontStyles.Bold);
            chevron.alignment = TextAlignmentOptions.Center;
            chevron.margin = new Vector4(0f, 0f, 3f, 6f);
            Stretch(chevron.rectTransform);

            var title = Moonlight.Label("Title", root, Moonlight.Heading ? Moonlight.Heading : _font, 36f, Moonlight.Text);
            title.text = "Leaderboard";
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(132f, -33f);
            title.rectTransform.sizeDelta = new Vector2(320f, 66f);
            _subtitle = Text("Subtitle", root, "Totals across every ranked song", 21f, Moonlight.Muted, FontStyles.Normal);
            _subtitle.rectTransform.anchorMin = _subtitle.rectTransform.anchorMax = _subtitle.rectTransform.pivot = new Vector2(0f, 1f);
            _subtitle.rectTransform.anchoredPosition = new Vector2(460f, -33f);
            _subtitle.rectTransform.sizeDelta = new Vector2(900f, 66f);

            // One flat panel holds the column headings and the list
            var panel = Moonlight.Panel("Panel", root, 27, Moonlight.PanelFill).rectTransform;
            Stretch(panel);
            panel.offsetMin = new Vector2(42f, 102f); // clear of the help bar
            panel.offsetMax = new Vector2(-42f, -135f);

            var heading = new GameObject("Heading", typeof(RectTransform)).GetComponent<RectTransform>();
            heading.SetParent(panel, false);
            Place(heading, PAD, -9f, -PAD, 66f);
            HeadingLeft(heading, "#", 21f, 60f);
            HeadingLeft(heading, "Player", NAME_LEFT, 300f);
            _scoreHeading = HeadingRight(heading, "Score", ScoreInset(), SCORE_WIDTH);
            HeadingRight(heading, "FCs", 0f, FC_WIDTH);
            for (int i = 0; i < Instruments.Length; i++)
            {
                // The game's instrument icons (line style), dimmed like the other headings
                var icon = new GameObject(Instruments[i], typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                icon.transform.SetParent(heading, false);
                Addressables.LoadAssetAsync<Sprite>($"InstrumentIcons[{Instruments[i]}]").Completed += handle =>
                {
                    if (icon != null) icon.sprite = handle.Result;
                };
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.color = Moonlight.Muted;
                var rect = icon.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
                rect.anchoredPosition = new Vector2(-StarInset(i), 0f);
                rect.sizeDelta = new Vector2(42f, 42f);
            }

            // The list scrolls with the mouse wheel or up and down; the player's own row stays below it
            _footer = new GameObject("You", typeof(RectTransform)).GetComponent<RectTransform>();
            _footer.SetParent(panel, false);
            _footer.anchorMin = Vector2.zero;
            _footer.anchorMax = new Vector2(1f, 0f);
            _footer.pivot = new Vector2(0.5f, 0f);
            _footer.offsetMin = new Vector2(PAD, PAD);
            _footer.offsetMax = new Vector2(-PAD, PAD + ROW);

            var viewport = new GameObject("List", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(panel, false);
            viewport.GetComponent<Image>().color = Color.clear; // catches the mouse wheel
            _viewport = viewport.GetComponent<RectTransform>();
            Stretch(_viewport);
            _viewport.offsetMin = new Vector2(PAD, PAD + ROW + 12f);
            _viewport.offsetMax = new Vector2(-PAD, -78f);

            _content = new GameObject("Rows", typeof(RectTransform)).GetComponent<RectTransform>();
            _content.SetParent(_viewport, false);
            Place(_content, 0f, 0f, 0f, 0f);

            var scroll = viewport.AddComponent<ScrollRect>();
            scroll.viewport = _viewport;
            scroll.content = _content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = ROW;

            // All time / Season N, top right; the season tab only appears once the server has a season
            _tabs = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter))
                .GetComponent<RectTransform>();
            _tabs.SetParent(root, false);
            _tabs.anchorMin = _tabs.anchorMax = _tabs.pivot = Vector2.one;
            _tabs.anchoredPosition = new Vector2(-42f, -33f);
            var pill = _tabs.gameObject.AddComponent<Image>();
            pill.sprite = Moonlight.RoundedSprite(36);
            pill.type = Image.Type.Sliced;
            pill.color = Moonlight.PanelFill;
            var row = _tabs.GetComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(6, 6, 6, 6);
            row.spacing = 6f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var fit = _tabs.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _tabs.gameObject.SetActive(false);

            Navigator.Instance.PushScheme(new NavigationScheme(new()
            {
                new NavigationScheme.Entry(MenuAction.Green, "Menu.GlobalLeaderboard.ViewProfile", () => ViewProfile(_selected)),
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", Close),
                new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Up", () => Select(_selected - 1, true)),
                new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Down", () => Select(_selected + 1, true)),
                new NavigationScheme.Entry(MenuAction.Orange, "Menu.GlobalLeaderboard.Switch", SwitchBoard),
                new NavigationScheme.Entry(MenuAction.Yellow, "Menu.GlobalLeaderboard.JumpToYou", JumpToMe),
            }, true));

            _cancel = new CancellationTokenSource();
            Load(_cancel.Token).Forget();
            FindSeason(_cancel.Token).Forget();
        }

        private async UniTaskVoid FindSeason(CancellationToken cancel)
        {
            var season = await Leaderboards.GetSeason(LIMIT);
            if (cancel.IsCancellationRequested || this == null || season?.Season == null)
            {
                return;
            }
            _seasonBoard = season;
            DrawTabs();
        }

        private void DrawTabs()
        {
            Clear(_tabs);
            Tab("All time", !_showSeason);
            Tab($"Season {_seasonBoard.Season.Number}", _showSeason);
            _tabs.gameObject.SetActive(true);
        }

        private void Tab(string text, bool on)
        {
            var tab = Moonlight.Panel(text, _tabs, 27, on ? Moonlight.Moon : Color.clear);
            tab.raycastTarget = true;
            tab.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                if (!on) SwitchBoard();
            });
            var size = tab.gameObject.AddComponent<LayoutElement>();
            var label = Text("Label", tab.rectTransform, text, 21f, on ? new Color(0.043f, 0.063f, 0.149f) : Moonlight.Muted,
                on ? FontStyles.Bold : FontStyles.Normal);
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
            size.preferredWidth = label.GetPreferredValues(text).x + 48f;
            size.preferredHeight = 54f;
        }

        private void SwitchBoard()
        {
            if (_seasonBoard == null)
            {
                return; // no season yet: All time is the only board
            }
            _showSeason = !_showSeason;
            DrawTabs();
            _cancel?.Cancel();
            _cancel?.Dispose();
            _cancel = new CancellationTokenSource();
            Load(_cancel.Token).Forget();
        }

        private void JumpToMe()
        {
            if (_meTop < 0f)
            {
                return; // not in the list (or pinned underneath it already)
            }
            float max = Mathf.Max(0f, _content.rect.height - _viewport.rect.height);
            _content.anchoredPosition = new Vector2(0f, Mathf.Clamp(_meTop - _viewport.rect.height / 2f + ROW / 2f, 0f, max));
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

        // The highlighted row (board FixOtherProfile): Up/Down move it and the list follows, A opens that player's profile
        private readonly List<(Image Row, Leaderboards.GlobalEntry Entry, bool IsMe)> _rows = new();
        private int _selected = -1;

        private void Select(int index, bool follow)
        {
            if (_rows.Count == 0) return;
            index = Mathf.Clamp(index, 0, _rows.Count - 1);
            for (int i = 0; i < _rows.Count; i++)
            {
                var (row, _, isMe) = _rows[i];
                if (row != null) row.color = isMe ? Moonlight.MoonWash : i == index ? Moonlight.Raised : Color.clear;
            }
            _selected = index;
            if (!follow || _rows[index].Row.transform.parent != _content) return;
            float top = -((RectTransform) _rows[index].Row.transform).anchoredPosition.y;
            float max = Mathf.Max(0f, _content.rect.height - _viewport.rect.height);
            float y = _content.anchoredPosition.y;
            if (top < y) y = top;
            else if (top + ROW > y + _viewport.rect.height) y = top + ROW - _viewport.rect.height;
            _content.anchoredPosition = new Vector2(0f, Mathf.Clamp(y, 0f, max));
        }

        private void ViewProfile(int index)
        {
            if (index < 0 || index >= _rows.Count) return;
            var (_, entry, isMe) = _rows[index];
            if (isMe)
            {
                ProfilePopup.Open(transform, _font);
            }
            else if (entry.Id > 0) // older servers send no id
            {
                ProfilePopup.OpenPlayer(transform, _font, entry.Id, entry.Name, entry.Avatar);
            }
        }

        private async UniTaskVoid Load(CancellationToken cancel)
        {
            Clear(_content);
            Clear(_footer);
            _rows.Clear();
            _selected = -1;
            _content.anchoredPosition = Vector2.zero;
            _viewport.offsetMin = new Vector2(PAD, PAD + ROW + 12f);
            _meTop = -1f;
            Message("Loading…");
            bool season = _showSeason;
            var board = season ? await Leaderboards.GetSeason(LIMIT) : await Leaderboards.GetGlobal(LIMIT);
            if (cancel.IsCancellationRequested || this == null)
            {
                return;
            }
            Clear(_content);
            _scoreHeading.text = season ? "Season score" : "Score";
            if (board == null)
            {
                Message("Couldn't load the leaderboard.");
                return;
            }
            string players = board.Players == 1 ? "1 player" : $"{board.Players:N0} players";
            _subtitle.text = season && board.Season is { } info
                ? info.Running
                    ? $"{players} · season songs since {Day(info.Start)}"
                    : $"{players} · final standings, {Day(info.Start)} – {Day(info.End, -1)}"
                : $"{players} · totals across every ranked song";
            if (board.Entries.Count == 0)
            {
                Message(season ? "No season scores yet. Play a season song to set the first one."
                    : "No scores yet. Play a ranked song to set the first one.");
                return;
            }

            bool meListed = false;
            for (int i = 0; i < board.Entries.Count; i++)
            {
                var entry = board.Entries[i];
                bool isMe = board.Me != null && entry.Rank == board.Me.Rank && entry.Name == board.Me.Name;
                meListed |= isMe;
                if (isMe) _meTop = i * (ROW + GAP);
                Row(_content, entry, -i * (ROW + GAP), isMe);
            }
            _content.sizeDelta = new Vector2(0f, board.Entries.Count * (ROW + GAP));

            if (meListed)
            {
                _viewport.offsetMin = new Vector2(PAD, PAD); // already highlighted in the list: no pinned copy
            }
            else if (board.Me != null)
            {
                Row(_footer, board.Me, ROW, true);
            }
            else
            {
                var hint = Text("Hint", _footer, "Set a score on a ranked song to appear here.", 21f, Moonlight.Muted,
                    FontStyles.Normal);
                hint.alignment = TextAlignmentOptions.Center;
                Stretch(hint.rectTransform);
            }
            Select(0, false);
        }

        // "2026-12-01" as "Dec 1"; offset for the day before an exclusive end date
        private static string Day(string date, int offset = 0) =>
            System.DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day)
                ? day.AddDays(offset).ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture)
                : date;

        private void Row(RectTransform parent, Leaderboards.GlobalEntry entry, float top, bool isMe)
        {
            int medal = entry.Rank - 1;
            bool medalled = medal is >= 0 and < 3;
            string shown = LeaderboardCard.Readable(_font, entry.Name);
            var fill = Moonlight.Panel("Row", parent, 15, isMe ? Moonlight.MoonWash : Color.clear);
            var row = fill.rectTransform;
            Place(row, 0f, top, 0f, ROW);
            // Pointing at a row highlights it, clicking opens that player's profile
            int index = _rows.Count;
            _rows.Add((fill, entry, isMe));
            fill.raycastTarget = true;
            fill.gameObject.AddComponent<Button>().onClick.AddListener(() => ViewProfile(index));
            Moonlight.OnHover(fill.gameObject, () => Select(index, false));

            var rank = Moonlight.Label("Rank", row, Moonlight.Heading ? Moonlight.Heading : _font, 21f,
                medalled ? LeaderboardCard.Medals[medal].Fill : Moonlight.Faint);
            rank.text = entry.Rank.ToString();
            PlaceLeft(rank.rectTransform, 21f, 60f);

            // Avatar, with the initial behind it until (or unless) the picture loads
            var avatar = Moonlight.Panel("Avatar", row, 27, Moonlight.Raised);
            avatar.rectTransform.anchorMin = avatar.rectTransform.anchorMax = avatar.rectTransform.pivot = new Vector2(0f, 0.5f);
            avatar.rectTransform.anchoredPosition = new Vector2(84f, 0f);
            avatar.rectTransform.sizeDelta = new Vector2(54f, 54f);
            var initial = Moonlight.Label("Initial", avatar.rectTransform, Moonlight.Heading ? Moonlight.Heading : _font, 20f,
                Moonlight.Text);
            initial.text = shown[..1].ToUpperInvariant();
            initial.alignment = TextAlignmentOptions.Center;
            Stretch(initial.rectTransform);
            if (entry.Avatar != null)
            {
                avatar.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var picture = new GameObject("Picture", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                picture.transform.SetParent(avatar.rectTransform, false);
                picture.raycastTarget = false;
                Stretch(picture.rectTransform);
                picture.enabled = false;
                LeaderboardCard.LoadAvatar(entry.Avatar, picture).Forget();
            }
            // The player's worn looks: a frame ring, a name colour, an alias under the name
            var look = entry.Look;
            if (look?.Frame != null && ColorUtility.TryParseHtmlString(look.Frame, out var frameColor))
            {
                var frame = new GameObject("Frame", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                frame.transform.SetParent(row, false);
                frame.sprite = Moonlight.ThinRing;
                frame.color = frameColor;
                frame.raycastTarget = false;
                frame.rectTransform.anchorMin = frame.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                frame.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                frame.rectTransform.anchoredPosition = new Vector2(84f + 27f, 0f);
                frame.rectTransform.sizeDelta = new Vector2(72f, 72f);
            }
            var nameColor = look?.Color != null && ColorUtility.TryParseHtmlString(look.Color, out var c) ? c
                : isMe ? Moonlight.Moon : Moonlight.Text;
            bool alias = !string.IsNullOrEmpty(look?.Alias);

            var name = Text("Name", row, shown, 25f, nameColor, FontStyles.Bold);
            name.richText = false; // Discord names are anyone's text: no <size> or <sprite> tags
            Place(name.rectTransform, NAME_LEFT, alias ? -9f : 0f, -(ScoreInset() + SCORE_WIDTH + 20f), alias ? 39f : ROW);
            if (entry.Level > 0)
            {
                // "LV 24" just after the name, as on the board
                var level = Text("Level", row, $"LV {entry.Level}", 18f, Moonlight.Faint, FontStyles.Bold);
                float nameWidth = Mathf.Min(name.GetPreferredValues(shown).x, 520f);
                Place(level.rectTransform, NAME_LEFT + nameWidth + 15f, alias ? -9f : 0f, -(ScoreInset() + SCORE_WIDTH + 20f), alias ? 39f : ROW);
            }
            if (alias)
            {
                var aliasText = Text("Alias", row, look.Alias, 19.5f, Moonlight.Muted, FontStyles.Normal);
                aliasText.richText = false;
                Place(aliasText.rectTransform, NAME_LEFT, -45f, -(ScoreInset() + SCORE_WIDTH + 20f), 27f);
            }

            var score = Text("Score", row, entry.Total.ToString("N0"), 27f, isMe ? Moonlight.Moon : Moonlight.Text, FontStyles.Bold);
            score.alignment = TextAlignmentOptions.MidlineRight;
            PlaceRight(score.rectTransform, ScoreInset(), SCORE_WIDTH);

            int[] stars = { entry.Guitar, entry.Bass, entry.Drums, entry.Vocals };
            for (int i = 0; i < stars.Length; i++)
            {
                var count = Text(Instruments[i], row, stars[i].ToString("N0"), 23f,
                    stars[i] > 0 ? Moonlight.Soft : Moonlight.Faint, FontStyles.Normal);
                count.alignment = TextAlignmentOptions.MidlineRight;
                PlaceRight(count.rectTransform, StarInset(i) - 21f, STAR_WIDTH);
            }

            var fcs = Text("FCs", row, entry.Fcs.ToString("N0"), 25f, Moonlight.Moon, FontStyles.Bold);
            fcs.alignment = TextAlignmentOptions.MidlineRight;
            PlaceRight(fcs.rectTransform, 21f, FC_WIDTH);
        }

        /// <summary>Right inset of instrument <paramref name="i"/>'s column centre (0 = lead, the leftmost).</summary>
        private static float StarInset(int i) => 21f + FC_WIDTH + (Instruments.Length - 1 - i) * STAR_WIDTH;

        private static float ScoreInset() => 21f + FC_WIDTH + Instruments.Length * STAR_WIDTH + 12f;

        private void HeadingLeft(RectTransform parent, string text, float left, float width)
        {
            var label = Text(text, parent, text, 20f, Moonlight.Muted, FontStyles.Normal);
            PlaceLeft(label.rectTransform, left, width);
        }

        private TextMeshProUGUI HeadingRight(RectTransform parent, string text, float inset, float width)
        {
            var label = Text(text, parent, text, 20f, Moonlight.Muted, FontStyles.Normal);
            label.alignment = TextAlignmentOptions.MidlineRight;
            PlaceRight(label.rectTransform, inset + 21f, width);
            return label;
        }

        private void Message(string text)
        {
            var message = Text("Message", _content, text, 25f, Moonlight.Muted, FontStyles.Normal);
            message.alignment = TextAlignmentOptions.Center;
            Place(message.rectTransform, 0f, -60f, 0f, 60f);
        }

        private static void Clear(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        private TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color, FontStyles style)
        {
            var label = Moonlight.Label(name, parent, _font, size, color);
            label.fontStyle = style;
            label.text = text;
            return label;
        }

        /// <summary>Pins a rect to the top of its parent, inset from the left and right edges.</summary>
        private static void Place(RectTransform rect, float left, float top, float right, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, top - height);
            rect.offsetMax = new Vector2(right, top);
        }

        /// <summary>A full-height column <paramref name="left"/> from its parent's left edge.</summary>
        private static void PlaceLeft(RectTransform rect, float left, float width)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(left, 0f);
            rect.sizeDelta = new Vector2(width, 0f);
        }

        /// <summary>A full-height column, <paramref name="inset"/> from its parent's right edge.</summary>
        private static void PlaceRight(RectTransform rect, float inset, float width)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-inset, 0f);
            rect.sizeDelta = new Vector2(width, 0f);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}

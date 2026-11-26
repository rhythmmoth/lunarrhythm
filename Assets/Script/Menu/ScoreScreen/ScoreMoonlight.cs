using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Song;
using YARG.Helpers.Extensions;
using YARG.Localization;
using YARG.Menu.MusicLibrary;
using YARG.Song;

namespace YARG.Menu.ScoreScreen
{
    /// <summary>
    /// The score screen as the Moonlight board draws it (MScore): the song in the header, one card per player
    /// (score, stars, a full combo / new best / misses tag, four stats) and a side panel. Until progression exists
    /// the side panel shows the song's top scores; Y switches the instrument. Drawn over YARG's score screen, which
    /// still saves scores and moves on. Sizes are the board's (1280×720) × 1.5.
    /// </summary>
    public class ScoreMoonlight : MonoBehaviour
    {
        private const float EDGE = 42f, TOP = 156f, BOTTOM = 102f, LIST_RIGHT = 1122f, GAP = 24f;
        private static readonly Color Gold = new(0.949f, 0.788f, 0.298f); // #f2c94c
        private static readonly Color OffStar = new(0.227f, 0.267f, 0.459f);
        private static readonly (string Api, string Label)[] Boards =
            { ("guitar", "LEAD"), ("bass", "BASS"), ("drums", "DRUMS"), ("vocals", "VOCALS") };

        private RectTransform _root, _cards, _aside;
        private RawImage _cover;
        private TextMeshProUGUI _kicker;
        private TextMeshProUGUI _title, _artist;
        private LeaderboardCard _board;
        private int _boardIndex;
        private string _shortname;
        // The XP panel (MScore): shown once the server answers this run; Y goes back to the song's top scores
        private RectTransform _xp;
        private string _xpName;
        private float _xpWaitUntil;

        public static ScoreMoonlight Create(ScoreScreenMenu menu)
        {
            foreach (Transform child in menu.transform)
            {
                if (!child.TryGetComponent(out CanvasGroup group)) group = child.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
            var canvas = (RectTransform) menu.GetComponentInParent<Canvas>(true).rootCanvas.transform;
            var root = new GameObject("Moonlight Score", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(canvas, false);
            Stretch(root);
            root.SetParent(menu.transform, true);
            var view = root.gameObject.AddComponent<ScoreMoonlight>();
            view._root = root;
            view.Build();
            return view;
        }

        private void Build()
        {
            var cover = Moonlight.Panel("Cover", _root, 18, Moonlight.Raised);
            cover.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            Pin(cover.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(EDGE, -81f), new Vector2(90f, 90f));
            _cover = Graphic<RawImage>("Art", cover.transform);
            Stretch(_cover.rectTransform);
            _title = Text(_root, Moonlight.Heading, 36f, Moonlight.Text, FontWeight.Medium);
            _title.richText = false;
            Pin(_title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(EDGE + 90f + 24f, -81f + 15f), new Vector2(1200f, 45f));
            _kicker = Text(_root, Moonlight.Body, 18f, Moonlight.Season, FontWeight.SemiBold);
            _kicker.characterSpacing = 14f;
            Pin(_kicker.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(EDGE + 90f + 24f, -81f + 47f), new Vector2(1200f, 24f));
            _artist = Text(_root, Moonlight.Body, 22.5f, Moonlight.Muted, FontWeight.Regular);
            _artist.richText = false;
            Pin(_artist.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(EDGE + 90f + 24f, -81f - 22f), new Vector2(1200f, 30f));

            _cards = new GameObject("Cards", typeof(RectTransform), typeof(GridLayoutGroup)).GetComponent<RectTransform>();
            _cards.SetParent(_root, false);
            _cards.anchorMin = Vector2.zero;
            _cards.anchorMax = new Vector2(0f, 1f);
            _cards.pivot = new Vector2(0f, 1f);
            _cards.offsetMin = new Vector2(EDGE, BOTTOM);
            _cards.offsetMax = new Vector2(EDGE + LIST_RIGHT - EDGE, -TOP);

            _aside = Moonlight.Panel("Aside", _root, 27, Moonlight.PanelFill).rectTransform;
            _aside.anchorMin = Vector2.zero;
            _aside.anchorMax = Vector2.one;
            _aside.offsetMin = new Vector2(LIST_RIGHT + 30f, BOTTOM);
            _aside.offsetMax = new Vector2(-EDGE, -TOP);
            var stack = _aside.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(33, 33, 30, 30);
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            _board = LeaderboardCard.Create(_aside, Moonlight.Body);
        }

        public void Show(ScoreScreenStats stats)
        {
            var song = GlobalVariables.State.CurrentSong;
            _title.text = song != null ? (string) song.Name : "";
            if (song != null && !Mathf.Approximately(GlobalVariables.State.SongSpeed, 1f))
            {
                _title.text += $" ({Localize.Percent(GlobalVariables.State.SongSpeed)})";
            }
            _artist.text = song != null ? (string) song.Artist : "";
            string kicker = MusicLibrary.LibrarySidebar.Kicker(song, out var kickerColor);
            _kicker.text = kicker ?? string.Empty;
            _kicker.color = kickerColor;
            _cover.enabled = false;
            LoadCover(song, _cover);

            string folder = song?.ActualLocation;
            _shortname = folder != null && OnlineSongs.IsOnlineFolder(folder)
                ? System.IO.Path.GetFileName(folder.TrimEnd('/', '\\'))
                : null;

            foreach (Transform child in _cards) Destroy(child.gameObject);
            var players = stats.PlayerScores;
            int columns = 2;
            int rows = Mathf.Max(1, Mathf.CeilToInt(players.Length / 2f));
            var grid = _cards.GetComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(GAP, GAP);
            float width = (LIST_RIGHT - EDGE - GAP * (columns - 1)) / columns;
            float height = (1080f - TOP - BOTTOM - GAP * (rows - 1)) / rows;
            grid.cellSize = new Vector2(width, height);
            bool compact = rows > 1; // four players: no room for the stats
            for (int i = 0; i < players.Length; i++)
            {
                Card(players[i], i == 0, compact);
            }

            // The side panel: the song's top scores for player 1's part (the board's XP panel comes with progression)
            var first = players.FirstOrDefault(p => !p.Player.Profile.IsBot);
            _boardIndex = first.Player == null ? 0 : first.Player.Profile.CurrentInstrument switch
            {
                Instrument.FiveFretBass => 1, Instrument.FiveFretRhythm => 2, Instrument.Keys => 3, _ => 0,
            };
            LeaderboardCard.Invalidate(); // this run may be on it now
            ShowBoard();

            // A solo online run was sent at the end of the song: wait a little for its XP
            _xpName = first.Player?.Profile.Name ?? "";
            _xpWaitUntil = _shortname != null && players.Count(p => !p.Player.Profile.IsBot) == 1 && Leaderboards.SignedIn
                ? Time.unscaledTime + 15f
                : 0f;
        }

        private void Update()
        {
            if (_xpWaitUntil <= 0f || _xp != null)
            {
                return;
            }
            if (Leaderboards.LastResult is { Accepted: true, Xp: { } xp, Progress: { } progress })
            {
                ShowXp(xp, progress);
            }
            else if (Time.unscaledTime > _xpWaitUntil)
            {
                _xpWaitUntil = 0f; // no answer (offline, rejected, an older server): the top scores stay
            }
        }

        /// <summary>Y: from the XP panel to the top scores, then the next instrument's.</summary>
        public void NextBoard()
        {
            if (_xp != null && _xp.gameObject.activeSelf)
            {
                _xp.gameObject.SetActive(false);
                _board.gameObject.SetActive(true);
                return;
            }
            _boardIndex = (_boardIndex + 1) % Boards.Length;
            ShowBoard();
        }

        private void ShowXp(Leaderboards.XpInfo xp, Leaderboards.Progress progress)
        {
            _board.gameObject.SetActive(false);
            _xp = new GameObject("XP", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
            _xp.SetParent(_aside, false);
            _xp.GetComponent<LayoutElement>().flexibleHeight = 1f;
            var stack = _xp.GetComponent<VerticalLayoutGroup>();
            stack.spacing = 6f;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            var kicker = Text(Box("Kicker", _xp, 30f), Moonlight.Body, 18f, Moonlight.Muted, FontWeight.SemiBold);
            kicker.characterSpacing = 14f;
            kicker.richText = false;
            kicker.text = (_xpName.Length > 0 ? _xpName.ToUpperInvariant() + " · " : "") + "XP";
            Stretch(kicker.rectTransform);

            // The level ring (filled as far as this level goes), the run's XP and how far to the next level
            var head = Box("Level", _xp, 96f);
            var track = Graphic<Image>("Track", head);
            track.sprite = Moonlight.ThinRing;
            track.color = Moonlight.Keycap;
            Pin(track.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(84f, 84f));
            var ring = Graphic<Image>("Ring", head);
            ring.sprite = Moonlight.ThinRing;
            ring.color = Moonlight.Moon;
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int) Image.Origin360.Top;
            ring.fillAmount = progress.Need > 0 ? Mathf.Clamp01(progress.Into / (float) progress.Need) : 0f;
            Pin(ring.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(84f, 84f));
            var level = Text(head, Moonlight.Heading, 30f, Moonlight.Text, FontWeight.Medium);
            level.text = progress.Level.ToString();
            level.alignment = TextAlignmentOptions.Center;
            Pin(level.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(84f, 84f));
            var gained = Text(head, Moonlight.Heading, 42f, Moonlight.Text, FontWeight.Medium);
            gained.text = $"+{xp.Total:N0} XP";
            Pin(gained.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(108f, -4f), new Vector2(600f, 54f));
            var next = Text(head, Moonlight.Body, 19.5f, Moonlight.Muted, FontWeight.Regular);
            next.text = progress.Level > xp.LevelBefore
                ? $"Level {progress.Level}! {progress.Into:N0} / {progress.Need:N0} to level {progress.Level + 1}"
                : $"{progress.Into:N0} / {progress.Need:N0} to level {progress.Level + 1}";
            Pin(next.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(108f, -6f), new Vector2(600f, 27f));

            foreach (var line in xp.Lines)
            {
                Row(_xp, line.Label, line.Xp >= 0 ? $"+{line.Xp:N0}" : $"−{-line.Xp:N0}", Moonlight.Soft, Moonlight.Text);
            }

            var rule = Graphic<Image>("Rule", Box("Rule", _xp, 19f));
            rule.color = new Color(Moonlight.Muted.r, Moonlight.Muted.g, Moonlight.Muted.b, 0.2f);
            Pin(rule.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(2000f, 1.5f));

            var daily = Text(Box("Daily", _xp, 30f), Moonlight.Body, 18f, Moonlight.Muted, FontWeight.SemiBold);
            daily.characterSpacing = 14f;
            daily.text = "DAILY CHALLENGES";
            Stretch(daily.rectTransform);
            foreach (var c in progress.Daily.Items)
            {
                var row = Row(_xp, c.Label, c.Done ? "Done" : $"{c.Progress} / {c.Count}", Moonlight.Text, Moonlight.Muted, 36f);
                var dot = Graphic<Image>("Dot", row);
                dot.sprite = c.Done ? Moonlight.RoundedSprite(8) : Moonlight.Ring;
                dot.type = c.Done ? Image.Type.Sliced : Image.Type.Simple;
                dot.color = c.Done ? Moonlight.Good : new Color(0.227f, 0.267f, 0.459f);
                Pin(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(16f, 16f));
            }
        }

        /// <summary>A label on the left, a value on the right; indent leaves room for a dot.</summary>
        private static RectTransform Row(Transform parent, string label, string value, Color labelColor, Color valueColor, float indent = 0f)
        {
            var row = Box("Row", parent, 30f);
            var left = Text(row, Moonlight.Body, 21f, labelColor, FontWeight.Regular);
            left.richText = false;
            left.text = label;
            Stretch(left.rectTransform);
            left.rectTransform.offsetMin = new Vector2(indent, 0f);
            left.rectTransform.offsetMax = new Vector2(-120f, 0f);
            var right = Text(row, Moonlight.Body, 21f, valueColor, FontWeight.SemiBold);
            right.text = value;
            right.alignment = TextAlignmentOptions.MidlineRight;
            Stretch(right.rectTransform);
            return row;
        }

        private void ShowBoard() => _board.Show(_shortname, Boards[_boardIndex].Api, Boards[_boardIndex].Label);

        private void Card(PlayerScoreCard score, bool you, bool compact)
        {
            var player = score.Player;
            var stats = score.Stats;
            var card = Moonlight.Panel(player.Profile.Name, _cards, 27, Moonlight.PanelFill);
            var stack = card.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(33, 33, 30, 30);
            stack.spacing = 18f;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            if (you && !player.Profile.IsBot)
            {
                var edge = Graphic<Image>("Edge", card.transform);
                edge.sprite = Moonlight.OutlineSprite(27);
                edge.type = Image.Type.Sliced;
                edge.color = new Color(0.953f, 0.91f, 0.8f, 0.45f);
                Stretch(edge.rectTransform);
            }

            var head = Box("Player", card.transform, 51f);
            var icon = Graphic<Image>("Icon", head);
            icon.preserveAspect = true;
            icon.color = Moonlight.Text;
            Pin(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(42f, 42f));
            SetIcon(icon, IconName(player.Profile.CurrentInstrument));
            var name = Text(head, Moonlight.Body, 25.5f, Moonlight.Text, FontWeight.Bold);
            name.text = player.Profile.Name;
            name.richText = false;
            Pin(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, 4f), new Vector2(500f, 36f)); // under the line height, Ellipsis drops the whole line
            var part = Text(head, Moonlight.Body, 19.5f, Moonlight.Muted, FontWeight.Regular);
            part.text = $"{player.Profile.CurrentInstrument.ToLocalizedName()} · {player.Profile.CurrentDifficulty.ToLocalizedName()}";
            Pin(part.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(60f, 0f), new Vector2(500f, 24f));

            var total = Text(card.transform, Moonlight.Heading, compact ? 42f : 60f, Moonlight.Text, FontWeight.Medium);
            total.text = stats.TotalScore.ToString("N0");
            total.characterSpacing = -2f;

            // Five stars, as in game (board FixScore): all gold at six, otherwise the earned ones in moon cream
            var starRow = new GameObject("Stars", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            starRow.transform.SetParent(card.transform, false);
            var starSize = starRow.GetComponent<LayoutElement>();
            starSize.minHeight = starSize.preferredHeight = 36f;
            starSize.flexibleHeight = 0f;
            var starLayout = starRow.GetComponent<HorizontalLayoutGroup>();
            starLayout.spacing = 10f;
            starLayout.childControlWidth = starLayout.childControlHeight = false;
            starLayout.childForceExpandWidth = starLayout.childForceExpandHeight = false;
            int earned = Mathf.FloorToInt(stats.Stars);
            for (int s = 0; s < 5; s++)
            {
                var star = new GameObject("Star", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                star.transform.SetParent(starRow.transform, false);
                star.rectTransform.sizeDelta = new Vector2(36f, 36f);
                star.sprite = Moonlight.Star;
                star.color = earned >= 6 ? Gold : s < earned ? Moonlight.Moon : OffStar;
                star.raycastTarget = false;
                Moonrise.Land(star.transform, s);
            }

            bool fc = stats.IsFullCombo;
            string tagText = fc ? (score.IsHighScore ? "Full combo · new best" : "Full combo")
                : score.IsHighScore ? "New best"
                : stats.Misses == 1 ? "1 miss" : $"{stats.Misses} misses";
            bool golden = fc || score.IsHighScore;
            var tagRow = Box("Tag", card.transform, 42f);
            var tag = Moonlight.Panel("Tag", tagRow, 21, golden ? new Color(Gold.r, Gold.g, Gold.b, 0.14f) : Moonlight.Raised);
            var tagLabel = Text(tag.transform, Moonlight.Body, 21f, golden ? Gold : Moonlight.Muted, FontWeight.Bold);
            tagLabel.text = tagText;
            tagLabel.alignment = TextAlignmentOptions.Center;
            Stretch(tagLabel.rectTransform);
            Pin(tag.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                new Vector2(tagLabel.GetPreferredValues(tagText).x + 36f, 42f));

            if (!compact)
            {
                var grid = new GameObject("Stats", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
                grid.transform.SetParent(card.transform, false);
                // Perfect and Good only where they're counted: the 5-lane keys engine (Festival lead, bass, keys and
                // vocals pads). Pro keys shares its stats but doesn't count them
                var keys = player.Profile.CurrentInstrument != YARG.Core.Instrument.ProKeys
                    ? stats as YARG.Core.Engine.Keys.KeysStats : null;
                int rows = keys != null ? 3 : 2;
                grid.GetComponent<LayoutElement>().preferredHeight = 75f * rows + 12f * (rows - 1);
                var cells = grid.GetComponent<GridLayoutGroup>();
                cells.spacing = new Vector2(12f, 12f);
                cells.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                cells.constraintCount = 2;
                float cardWidth = _cards.GetComponent<GridLayoutGroup>().cellSize.x - 66f;
                cells.cellSize = new Vector2((cardWidth - 12f) / 2f, 75f);
                Stat(grid.transform, "Accuracy", Mathf.FloorToInt(stats.Percent * 100f) + "%");
                Stat(grid.transform, "Notes", $"{stats.NotesHit:N0} / {stats.TotalNotes:N0}");
                if (keys != null)
                {
                    Stat(grid.transform, "Perfect", keys.PerfectHits.ToString("N0"), Moonlight.Moon);
                    Stat(grid.transform, "Good", Mathf.Max(0, keys.NotesHit - keys.PerfectHits).ToString("N0"));
                }
                Stat(grid.transform, "Best streak", stats.MaxCombo.ToString("N0"));
                Stat(grid.transform, "Overdrive", stats.StarPowerActivationCount == 1 ? "1 used" : $"{stats.StarPowerActivationCount} used");
            }

            var note = Text(card.transform, Moonlight.Body, 21f, you && !player.Profile.IsBot ? Moonlight.Moon : Moonlight.Muted, FontWeight.Regular);
            note.text = player.Profile.IsBot ? "Bot · not on the leaderboards"
                : _shortname == null ? "Not a ranked song"
                : you && YARG.Core.Song.Leaderboards.UpdateRequired ? "Not sent: this version is too old for leaderboards. Update to send your next scores."
                : you && YARG.Core.Song.Leaderboards.SignedIn ? "Sent to the song leaderboard"
                : "Local profile · not on the leaderboards";
        }

        private static void Stat(Transform parent, string label, string value, Color? color = null)
        {
            var cell = Moonlight.Panel(label, parent, 18, Moonlight.Raised);
            var key = Text(cell.transform, Moonlight.Body, 18f, Moonlight.Muted, FontWeight.Regular);
            key.text = label;
            Pin(key.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -12f), new Vector2(300f, 24f));
            var v = Text(cell.transform, Moonlight.Body, 25.5f, color ?? Moonlight.Text, FontWeight.Bold);
            v.text = value;
            Pin(v.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 12f), new Vector2(300f, 33f));
        }

        internal static async void LoadCover(SongEntry song, RawImage art)
        {
            if (song == null) return;
            using var image = await Cysharp.Threading.Tasks.UniTask.RunOnThreadPool(song.LoadAlbumData);
            if (image == null || art == null) return;
            art.texture = image.LoadTexture(false);
            art.uvRect = new Rect(0f, 0f, 1f, -1f); // covers load upside down, as in the library
            art.enabled = true;
        }

        private static string IconName(Instrument instrument) => instrument switch
        {
            Instrument.FiveFretGuitar => "guitar",
            Instrument.FiveFretBass => "bass",
            Instrument.FiveFretRhythm => "drums",
            Instrument.Keys => "vocals",
            _ => instrument.ToResourceName(),
        };

        private static void SetIcon(Image icon, string name)
        {
            icon.enabled = false;
            // Asynchronously: a blocking load can run inside the player loop and recurse it
            Addressables.LoadAssetAsync<Sprite>($"InstrumentIcons[{name}]").Completed += handle =>
            {
                if (icon == null) return;
                icon.sprite = handle.Result;
                icon.enabled = handle.Result != null;
            };
        }

        private static RectTransform Box(string name, Transform parent, float height)
        {
            var box = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            box.transform.SetParent(parent, false);
            var size = box.GetComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = height;
            size.flexibleHeight = 0f;
            return (RectTransform) box.transform;
        }

        private static T Graphic<T>(string name, Transform parent) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(T), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            go.transform.SetParent(parent, false);
            var graphic = go.GetComponent<T>();
            graphic.raycastTarget = false;
            return graphic;
        }

        private static TextMeshProUGUI Text(Transform parent, TMP_FontAsset font, float size, Color color, FontWeight weight)
        {
            var label = Moonlight.Label("Text", parent, font != null ? font : Moonlight.Body, size, color);
            label.fontWeight = weight;
            return label;
        }

        private static void Pin(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
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

using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Song;

namespace YARG.Menu.MusicLibrary
{
    /// <summary>The music library sidebar's leaderboard: the top runs of the selected song for one instrument.</summary>
    public class LeaderboardCard : MonoBehaviour
    {
        private const float PAD = 10f;
        private const float HEADER = 34f;
        private const float ROW = 36f;
        private const float GAP = 4f;
        private const float DIVIDER = 18f;
        private const float BOARD_CACHE_SECONDS = 60f;

        private static readonly Color CardColor = new(0.063f, 0.055f, 0.106f);
        private static readonly Color Accent = new(0.8f, 0.69f, 1f);
        private static readonly Color Muted = new(0.56f, 0.53f, 0.69f);
        private static readonly Color Dim = new(0.36f, 0.34f, 0.47f);
        private static readonly Color AvatarColor = new(0.13f, 0.11f, 0.23f);
        private static readonly Color TagColor = new(0.12f, 0.1f, 0.2f);
        private static readonly Color MeColor = new(0.23f, 0.1f, 0.53f);
        private static readonly Color MeOutline = new(0.48f, 0.31f, 0.94f);
        private static readonly (Color Fill, Color Text, Color Row)[] Medals =
        {
            (new(0.95f, 0.79f, 0.3f), new(0.29f, 0.23f, 0.02f), new(0.11f, 0.094f, 0.19f)),
            (new(0.79f, 0.81f, 0.85f), new(0.17f, 0.19f, 0.22f), new(0.09f, 0.078f, 0.165f)),
            (new(0.82f, 0.55f, 0.36f), new(0.29f, 0.15f, 0.06f), new(0.09f, 0.078f, 0.165f)),
        };

        private static Sprite _rounded;
        private static Sprite _circle;
        // Kept for the whole session: a few dozen 64px pictures at most
        private static readonly Dictionary<string, Texture2D> Avatars = new();
        private static readonly Dictionary<string, (float Time, Leaderboards.Board Board)> Boards = new();

        private TMP_FontAsset _font;
        private TextMeshProUGUI _tag;
        private RectTransform _body;
        private CancellationTokenSource _cancel;

        public static LeaderboardCard Create(Transform parent, TMP_FontAsset font)
        {
            // Unity-null checks, not ??=: runtime assets can be unloaded between scenes
            if (_rounded == null) _rounded = MakeRounded(32, 12);
            if (_circle == null) _circle = MakeRounded(64, 32);

            // The sidebar's column stretches its children edge to edge; the holder takes that, the card sits inset
            var holder = new GameObject("Leaderboard", typeof(RectTransform), typeof(LayoutElement));
            holder.transform.SetParent(parent, false);
            var layout = holder.GetComponent<LayoutElement>();
            layout.flexibleHeight = 1f;
            layout.minHeight = 160f;

            var rect = Box("Card", holder.transform, CardColor);
            Stretch(rect);
            rect.offsetMin = new Vector2(15f, 14f);
            rect.offsetMax = new Vector2(-15f, -6f);

            var card = holder.AddComponent<LeaderboardCard>();
            card._font = font;

            var title = card.Text("Title", rect, "LEADERBOARD", 17f, Accent, FontStyles.Bold);
            title.characterSpacing = 6f;
            Place(title.rectTransform, PAD + 6f, -PAD, -120f, HEADER);

            var tagBox = Box("Instrument", rect, TagColor);
            tagBox.anchorMin = tagBox.anchorMax = tagBox.pivot = Vector2.one;
            tagBox.anchoredPosition = new Vector2(-PAD - 4f, -PAD - 5f);
            tagBox.sizeDelta = new Vector2(84f, 24f);
            card._tag = card.Text("Text", tagBox, "", 13f, Muted, FontStyles.Bold);
            card._tag.alignment = TextAlignmentOptions.Center;
            card._tag.characterSpacing = 3f;
            Stretch(card._tag.rectTransform);

            card._body = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
            card._body.SetParent(rect, false);
            Stretch(card._body);
            card._body.offsetMax = new Vector2(-PAD, -PAD - HEADER - GAP);
            card._body.offsetMin = new Vector2(PAD, PAD);
            return card;
        }

        /// <summary>Shows a song's board. Pass a null shortname for songs that can't have one.</summary>
        public void Show(string shortname, string instrument, string label)
        {
            _tag.text = label;
            Cancel();
            Clear();
            if (shortname == null)
            {
                Message("Leaderboards are only for online songs.");
                return;
            }
            _cancel = new CancellationTokenSource();
            Load(shortname, instrument, _cancel.Token).Forget();
        }

        public void Hide()
        {
            Cancel();
            Clear();
            _tag.text = "";
        }

        private void OnDestroy() => Cancel();

        private void Cancel()
        {
            _cancel?.Cancel();
            _cancel?.Dispose();
            _cancel = null;
        }

        private async UniTaskVoid Load(string shortname, string instrument, CancellationToken cancel)
        {
            // Wait out fast scrolling through the list before asking the server
            if (await UniTask.Delay(250, cancellationToken: cancel).SuppressCancellationThrow())
            {
                return;
            }

            int rows = Mathf.Clamp(Mathf.FloorToInt((_body.rect.height - DIVIDER - ROW) / (ROW + GAP)), 1, 10);
            string key = $"{shortname}/{instrument}/{rows}";
            if (!Boards.TryGetValue(key, out var cached) || Time.unscaledTime - cached.Time > BOARD_CACHE_SECONDS)
            {
                Message("Loading…");
                var board = await Leaderboards.GetBoard(shortname, instrument, rows);
                if (cancel.IsCancellationRequested || this == null)
                {
                    return;
                }
                if (board == null)
                {
                    Clear();
                    Message("Couldn't load the leaderboard.");
                    return;
                }
                cached = (Time.unscaledTime, board);
                Boards[key] = cached;
            }

            Clear();
            Build(cached.Board, rows);
        }

        /// <summary>Forgets cached boards, so a new score shows the next time a song is selected.</summary>
        public static void Invalidate() => Boards.Clear();

        private void Build(Leaderboards.Board board, int rows)
        {
            if (!board.Ranked)
            {
                Message("This song doesn't have a leaderboard yet.");
                return;
            }
            if (board.Entries.Count == 0)
            {
                Message("No scores yet. Play it to set the first one.");
                return;
            }

            var me = board.Me;
            bool meShown = false;
            float y = 0f;
            for (int i = 0; i < board.Entries.Count && i < rows; i++)
            {
                var entry = board.Entries[i];
                bool isMe = me != null && entry.Rank == me.Rank && entry.Name == me.Name && entry.Score == me.Score;
                meShown |= isMe;
                Row(entry, y, isMe);
                y -= ROW + GAP;
            }

            if (me != null && !meShown)
            {
                Divider(y - 2f);
                Row(me, y - DIVIDER, true);
            }
            else if (me == null && !Leaderboards.SignedIn)
            {
                var hint = Text("Hint", _body, "Sign in with Discord in Settings to appear here.", 14f, Dim, FontStyles.Normal);
                hint.alignment = TextAlignmentOptions.Center;
                Place(hint.rectTransform, 0f, y - 6f, 0f, ROW);
            }
        }

        private void Row(Leaderboards.Entry entry, float top, bool isMe)
        {
            int medal = entry.Rank - 1;
            RectTransform row;
            if (isMe)
            {
                var outline = Box("Row", _body, MeOutline);
                Place(outline, 0f, top, 0f, ROW);
                row = Box("Fill", outline, MeColor);
                Stretch(row);
                row.offsetMin = new Vector2(1.5f, 1.5f);
                row.offsetMax = new Vector2(-1.5f, -1.5f);
            }
            else
            {
                row = Box("Row", _body, medal is >= 0 and < 3 ? Medals[medal].Row : Color.clear);
                Place(row, 0f, top, 0f, ROW);
            }

            // Rank: a medal for the top three
            var rank = Circle("Rank", row, medal is >= 0 and < 3 ? Medals[medal].Fill : Color.clear, 8f, 24f);
            var rankText = Text("Text", rank, entry.Rank.ToString(), 13f,
                medal is >= 0 and < 3 ? Medals[medal].Text : isMe ? Color.white : Muted, FontStyles.Bold);
            rankText.alignment = TextAlignmentOptions.Center;
            Stretch(rankText.rectTransform);

            // Avatar, with the initial behind it until (or unless) the picture loads
            var avatar = Circle("Avatar", row, isMe ? Accent : AvatarColor, 40f, 28f);
            var initial = Text("Initial", avatar, entry.Name.Length > 0 ? entry.Name[..1].ToUpperInvariant() : "?", 13f,
                isMe ? MeColor : Accent, FontStyles.Bold);
            initial.alignment = TextAlignmentOptions.Center;
            Stretch(initial.rectTransform);
            if (entry.Avatar != null)
            {
                avatar.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var picture = new GameObject("Picture", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                picture.transform.SetParent(avatar, false);
                picture.raycastTarget = false;
                Stretch(picture.rectTransform);
                picture.enabled = false;
                LoadAvatar(entry.Avatar, picture).Forget();
            }

            var name = Text("Name", row, entry.Name, 15f, Color.white, isMe ? FontStyles.Bold : FontStyles.Normal);
            Place(name.rectTransform, 78f, 0f, -166f, ROW);

            var tag = Box("Difficulty", row, isMe ? MeOutline : TagColor);
            tag.anchorMin = tag.anchorMax = tag.pivot = new Vector2(1f, 0.5f);
            tag.anchoredPosition = new Vector2(-128f, 0f);
            tag.sizeDelta = new Vector2(28f, 20f);
            var tagText = Text("Text", tag, DifficultyLetter(entry.Difficulty), 11f, isMe ? Color.white : Accent,
                FontStyles.Bold);
            tagText.alignment = TextAlignmentOptions.Center;
            Stretch(tagText.rectTransform);

            var score = Text("Score", row, entry.Score.ToString("N0"), 15f,
                isMe || medal is >= 0 and < 3 ? Color.white : new Color(0.81f, 0.79f, 0.89f), FontStyles.Bold);
            score.alignment = TextAlignmentOptions.Right;
            PlaceRight(score.rectTransform, 40f, 84f);

            if (entry.Fc)
            {
                var fc = Text("FC", row, "FC", 11f, Medals[0].Fill, FontStyles.Bold);
                fc.alignment = TextAlignmentOptions.Center;
                PlaceRight(fc.rectTransform, 8f, 28f);
            }
        }

        private void Divider(float top)
        {
            var label = Text("You", _body, "YOU", 11f, Dim, FontStyles.Bold);
            label.alignment = TextAlignmentOptions.Center;
            label.characterSpacing = 4f;
            Place(label.rectTransform, 0f, top, 0f, DIVIDER - 4f);
            foreach (float side in new[] { 0f, 1f })
            {
                var line = Box("Line", _body, new Color(0.18f, 0.16f, 0.28f));
                line.GetComponent<Image>().sprite = null;
                line.anchorMin = line.anchorMax = new Vector2(side, 1f);
                line.pivot = new Vector2(side, 0.5f);
                line.anchoredPosition = new Vector2(0f, top - (DIVIDER - 4f) / 2f);
                line.sizeDelta = new Vector2(_body.rect.width / 2f - 30f, 1f);
            }
        }

        private void Message(string text)
        {
            var message = Text("Message", _body, text, 15f, Muted, FontStyles.Normal);
            message.alignment = TextAlignmentOptions.Center;
            message.textWrappingMode = TextWrappingModes.Normal;
            Stretch(message.rectTransform);
        }

        private void Clear()
        {
            for (int i = _body.childCount - 1; i >= 0; i--)
            {
                Destroy(_body.GetChild(i).gameObject);
            }
        }

        private static async UniTaskVoid LoadAvatar(string path, RawImage picture)
        {
            if (!Avatars.TryGetValue(path, out var texture) || texture == null)
            {
                var png = await Leaderboards.GetAvatar(path);
                if (png == null || (Avatars.TryGetValue(path, out texture) && texture != null))
                {
                    // No picture, or another row loaded the same one meanwhile
                }
                else
                {
                    texture = new Texture2D(2, 2) { hideFlags = HideFlags.DontUnloadUnusedAsset };
                    if (!texture.LoadImage(png))
                    {
                        Destroy(texture);
                        texture = null;
                    }
                    else
                    {
                        Avatars[path] = texture;
                    }
                }
            }
            if (picture != null && texture != null)
            {
                picture.texture = texture;
                picture.enabled = true;
            }
        }

        private static string DifficultyLetter(string difficulty) => difficulty switch
        {
            "easy"   => "E",
            "medium" => "M",
            "hard"   => "H",
            _        => "X",
        };

        private TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color, FontStyles style)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            label.font = _font;
            label.fontSize = size;
            label.color = color;
            label.fontStyle = style;
            label.text = text;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform Box(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.sprite = _rounded;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image.rectTransform;
        }

        private static RectTransform Circle(string name, Transform parent, Color color, float left, float size)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.sprite = _circle;
            image.color = color;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(left, 0f);
            rect.sizeDelta = new Vector2(size, size);
            return rect;
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

        /// <summary>A full-height column of a row, <paramref name="inset"/> from its right edge.</summary>
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

        /// <summary>A white rounded square, 9-sliced so it scales to any size; radius = size / 2 is a circle.</summary>
        private static Sprite MakeRounded(int size, int radius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontUnloadUnusedAsset,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance past the nearest corner's circle, antialiased over a pixel
                    float dx = Mathf.Max(radius - (x + 0.5f), x + 0.5f - (size - radius), 0f);
                    float dy = Mathf.Max(radius - (y + 0.5f), y + 0.5f - (size - radius), 0f);
                    float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte) (alpha * 255));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }
    }
}

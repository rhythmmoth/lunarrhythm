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
        // The Moonlight board's "TOP SCORES · LEAD" list, ×1.5: no card, small caps label, plain rows
        private const float PAD = 0f;
        private const float HEADER = 27f;
        private const float ROW = 27f;
        private const float GAP = 10.5f;
        private const float DIVIDER = 10.5f;
        private const float BOARD_CACHE_SECONDS = 60f;

        private static readonly Color Muted = Moonlight.Muted;
        private static readonly Color Dim = Moonlight.Faint;
        internal static readonly (Color Fill, Color Text, Color Row)[] Medals =
        {
            (new(0.95f, 0.79f, 0.3f), new(0.29f, 0.23f, 0.02f), Color.clear),
            (new(0.79f, 0.81f, 0.85f), new(0.17f, 0.19f, 0.22f), Color.clear),
            (new(0.82f, 0.55f, 0.36f), new(0.29f, 0.15f, 0.06f), Color.clear),
        };

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

            // The sidebar's column stretches its children edge to edge; the holder takes that, the card sits inset
            var holder = new GameObject("Leaderboard", typeof(RectTransform), typeof(LayoutElement));
            holder.transform.SetParent(parent, false);
            var layout = holder.GetComponent<LayoutElement>();
            layout.flexibleHeight = 1f;
            layout.minHeight = 160f;

            var rect = (RectTransform) holder.transform;

            var card = holder.AddComponent<LeaderboardCard>();
            card._font = font;

            // "TOP SCORES · LEAD": the instrument is part of the label
            card._tag = card.Text("Title", rect, "", 18f, Muted, FontStyles.Normal);
            card._tag.fontWeight = FontWeight.SemiBold;
            card._tag.characterSpacing = 14f;
            Place(card._tag.rectTransform, 0f, 0f, 0f, HEADER);

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
            _tag.text = "TOP SCORES · " + label;
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
                var hint = Text("Hint", _body, "Sign in with Discord in Settings to appear here.", 19.5f, Dim, FontStyles.Normal);
                hint.alignment = TextAlignmentOptions.MidlineLeft;
                Place(hint.rectTransform, 0f, y - 6f, 0f, ROW);
            }
        }

        private void Row(Leaderboards.Entry entry, float top, bool isMe)
        {
            // Rank and name on the left, the score on the right; your own row in moon cream
            var row = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(_body, false);
            Place(row, 0f, top, 0f, ROW);
            var color = isMe ? Moonlight.Moon : Moonlight.Text;

            var rank = Text("Rank", row, entry.Rank.ToString(), 22.5f, isMe ? Moonlight.Moon : Moonlight.Faint,
                FontStyles.Normal);
            rank.rectTransform.anchorMin = Vector2.zero;
            rank.rectTransform.anchorMax = new Vector2(0f, 1f);
            rank.rectTransform.pivot = new Vector2(0f, 0.5f);
            rank.rectTransform.sizeDelta = new Vector2(rank.preferredWidth + 2f, 0f);

            var name = Text("Name", row, Readable(_font, entry.Name), 22.5f, color, FontStyles.Normal);
            name.richText = false; // Discord names are anyone's text: no <size> or <sprite> tags
            Place(name.rectTransform, rank.preferredWidth + 15f, 0f, -150f, ROW);

            var score = Text("Score", row, entry.Score.ToString("N0"), 22.5f, color, FontStyles.Normal);
            score.fontWeight = FontWeight.SemiBold;
            score.alignment = TextAlignmentOptions.Right;
            PlaceRight(score.rectTransform, 0f, 150f);
        }

        private void Divider(float top)
        {
            // Just a gap before your own row when you're below the shown ones
        }

        private void Message(string text)
        {
            var message = Text("Message", _body, text, 21f, Muted, FontStyles.Normal);
            message.alignment = TextAlignmentOptions.TopLeft;
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

        internal static async UniTaskVoid LoadAvatar(string path, RawImage picture)
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

        /// <summary>
        /// A Discord name without the characters the font (and its fallbacks) can't draw, which would show as boxes.
        /// "Player" if nothing readable is left.
        /// </summary>
        internal static string Readable(TMP_FontAsset font, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Player";
            }
            if (font == null || font.HasCharacters(name, out uint[] missing, true) || missing == null)
            {
                return name;
            }
            // By code point, so emoji and other characters outside the BMP go as a whole
            var kept = new System.Text.StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i += char.IsSurrogatePair(name, i) ? 2 : 1)
            {
                uint code = (uint) char.ConvertToUtf32(name, i);
                bool space = char.IsWhiteSpace(name, i);
                if (System.Array.IndexOf(missing, code) >= 0 || (space && (kept.Length == 0 || kept[^1] == ' ')))
                {
                    continue;
                }
                kept.Append(space ? " " : char.ConvertFromUtf32((int) code));
            }
            string result = kept.ToString().Trim();
            foreach (char c in result)
            {
                if (char.IsLetterOrDigit(c))
                {
                    return result;
                }
            }
            return "Player";
        }

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
        internal static Sprite MakeRounded(int size, int radius) => Moonlight.MakeRounded(size, radius);
    }
}

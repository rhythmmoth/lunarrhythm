using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Song;
using YARG.Menu;
using YARG.Menu.ScoreScreen;

namespace YARG.Gameplay.HUD
{
    /// <summary>
    /// The Moonlight board's in-game HUD: a flat score panel (progress ring, score, five flat stars, time and a bar)
    /// over YARG's glossy score box, and a song pill in place of the song info text.
    /// </summary>
    public class HudMoonlight : MonoBehaviour
    {
        private const float WIDTH = 375f;

        private static readonly Color OffStar = new(0.227f, 0.267f, 0.459f);
        private static readonly Color Gold = new(0.949f, 0.788f, 0.298f);

        private GameManager _game;
        private TextMeshProUGUI _score, _time;
        private Image _bar;
        private readonly Image[] _stars = new Image[5];
        private readonly Image[] _fills = new Image[5];
        private int _shownScore = -1;
        private int _shownLeft = -1;

        /// <summary>Hides the score box's own graphics and draws the board's panel in its top right corner.</summary>
        public static HudMoonlight CreateScorePanel(Transform scoreBox, GameManager game, TextMeshProUGUI timer)
        {
            foreach (Transform child in scoreBox)
            {
                var hide = child.gameObject.AddComponent<CanvasGroup>();
                hide.alpha = 0f;
                hide.blocksRaycasts = false;
            }

            var panel = Moonlight.Panel("Moonlight Score", scoreBox, 27, Moonlight.PanelFill);
            panel.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-36f, -36f);
            rect.sizeDelta = new Vector2(WIDTH, 165f);

            var view = panel.gameObject.AddComponent<HudMoonlight>();
            view._game = game;
            view.Build(rect);
            return view;
        }

        private void Build(RectTransform panel)
        {
            float inner = WIDTH - 54f;

            // Board FixGame: the score on the left above the stars (no progress ring: the bar shows the same)
            _score = Moonlight.Label("Score", panel, Moonlight.Heading, 45f, Moonlight.Text);
            _score.fontWeight = FontWeight.Medium;
            _score.alignment = TextAlignmentOptions.MidlineLeft;
            _score.overflowMode = TextOverflowModes.Overflow;
            Place(_score.rectTransform, new Vector2(27f, -18f), new Vector2(inner, 63f));

            for (int i = 0; i < 5; i++)
            {
                _stars[i] = MakeImage(panel, Moonlight.Star, OffStar);
                Place(_stars[i].rectTransform, new Vector2(27f + i * 36f, -90f), new Vector2(30f, 30f));
                // The star being earned fills from the left
                _fills[i] = MakeImage(_stars[i].rectTransform, Moonlight.Star, Moonlight.Moon);
                _fills[i].type = UnityEngine.UI.Image.Type.Filled;
                _fills[i].fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
                Stretch(_fills[i].rectTransform);
            }

            // Time left, right-aligned just above the bar
            _time = Moonlight.Label("Time", panel, Moonlight.Body, 19.5f, Moonlight.Soft);
            _time.fontWeight = FontWeight.SemiBold;
            _time.alignment = TextAlignmentOptions.BottomRight;
            Place(_time.rectTransform, new Vector2(27f + 200f, -90f), new Vector2(inner - 200f, 30f));

            var bar = Moonlight.Panel("Bar", panel, 3, Moonlight.Keycap);
            Place(bar.rectTransform, new Vector2(27f, -135f), new Vector2(inner, 6f));
            _bar = Moonlight.Panel("Fill", bar.rectTransform, 3, Moonlight.Moon);
            _bar.rectTransform.anchorMin = Vector2.zero;
            _bar.rectTransform.anchorMax = new Vector2(0f, 1f);
            _bar.rectTransform.pivot = new Vector2(0f, 0.5f);
            _bar.rectTransform.offsetMin = Vector2.zero;
            _bar.rectTransform.offsetMax = Vector2.zero;
        }

        private readonly Vector3[] _corners = new Vector3[4];

        private void LateUpdate()
        {
            if (_game == null) return;

            // The board's corner, 36 from the screen's top right, wherever the box is (it moves down for lyrics)
            ((RectTransform) GetComponentInParent<Canvas>().rootCanvas.transform).GetWorldCorners(_corners);
            var rect = (RectTransform) transform;
            rect.position = _corners[2];
            rect.anchoredPosition += new Vector2(-36f, -36f);

            if (_game.BandScore != _shownScore)
            {
                _shownScore = _game.BandScore;
                _score.text = _shownScore.ToString("N0");
            }

            // Five flat stars: earned ones in moon cream, the next one filling; all gold at six
            float stars = _game.BandStars;
            bool gold = stars >= 6f;
            for (int i = 0; i < 5; i++)
            {
                _fills[i].fillAmount = Mathf.Clamp01(stars - i);
                _fills[i].color = gold ? Gold : Moonlight.Moon;
            }

            double length = _game.SongLength;
            float progress = length > 0 ? Mathf.Clamp01((float) (_game.SongTime / length)) : 0f;
            _bar.rectTransform.sizeDelta = new Vector2(((RectTransform) _bar.transform.parent).rect.width * progress, 0f);
            int left = Mathf.Max(0, Mathf.CeilToInt((float) (length - _game.SongTime)));
            if (left != _shownLeft)
            {
                _shownLeft = left;
                _time.text = $"-{left / 60}:{left % 60:00}";
            }
        }

        /// <summary>The board's song pill (cover, title, artist), in place of the song info text; it fades with it.</summary>
        public static void CreateSongPill(RectTransform songInfo, TextMeshProUGUI yargText, SongEntry song)
        {
            yargText.enabled = false;

            var pill = Moonlight.Panel("Moonlight Song", songInfo, 42,
                new Color(Moonlight.PanelFill.r, Moonlight.PanelFill.g, Moonlight.PanelFill.b, 0.7f));
            pill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Moonlight.Shadow(pill.rectTransform, Moonlight.PillShadow, 42);
            var rect = pill.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            // The info box sits 96 in and 150 down; the board's pill is 36 from the corner
            rect.anchoredPosition = new Vector2(36f - songInfo.anchoredPosition.x, -36f - songInfo.anchoredPosition.y);

            var disc = Moonlight.Panel("Cover", rect, 30, Moonlight.Raised);
            disc.color = new Color(0.353f, 0.114f, 0.247f); // the board's placeholder plum, until the cover loads
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            Place(disc.rectTransform, new Vector2(12f, -12f), new Vector2(60f, 60f));
            var art = new GameObject("Art", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            art.transform.SetParent(disc.transform, false);
            art.raycastTarget = false;
            art.enabled = false;
            Stretch(art.rectTransform);
            ScoreMoonlight.LoadCover(song, art);

            var title = Moonlight.Label("Title", rect, Moonlight.Body, 22.5f, Moonlight.Text);
            title.fontWeight = FontWeight.Bold;
            title.text = song != null ? (string) song.Name : "";
            Place(title.rectTransform, new Vector2(90f, -15f), new Vector2(600f, 30f));
            var artist = Moonlight.Label("Artist", rect, Moonlight.Body, 19.5f, Moonlight.Muted);
            artist.text = song != null ? (string) song.Artist : "";
            Place(artist.rectTransform, new Vector2(90f, -45f), new Vector2(600f, 27f));

            float text = Mathf.Max(title.GetPreferredValues(title.text).x, artist.GetPreferredValues(artist.text).x);
            text = Mathf.Min(text, 600f);
            title.rectTransform.sizeDelta = new Vector2(text, 30f);
            artist.rectTransform.sizeDelta = new Vector2(text, 27f);
            rect.sizeDelta = new Vector2(90f + text + 27f, 84f);
        }

        private static Image MakeImage(Transform parent, Sprite sprite, Color color)
        {
            var image = new GameObject("Image", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Place(RectTransform rect, Vector2 topLeft, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
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

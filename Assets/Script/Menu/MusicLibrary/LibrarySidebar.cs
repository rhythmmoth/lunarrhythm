using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Song;
using YARG.Menu.Persistent;

namespace YARG.Menu.MusicLibrary
{
    /// <summary>
    /// The music library's right-hand panel as drawn on the Moonlight board: the cover beside the title, artist and a
    /// line of details, a cream Play pill, one row per part with its difficulty in moon dots, then the top scores.
    /// Built in code over the prefab sidebar; the prefab's Play button and cover image are moved in, so they keep
    /// working as before. All sizes are the board's (1280×720) times 1.5.
    /// </summary>
    public class LibrarySidebar : MonoBehaviour
    {
        // Lead, Bass, Drums, Vocals: the sidebar's instruments for each pad part, and their icon names
        private static readonly (string Name, string Icon, Instrument Instrument)[] Parts =
        {
            ("Lead", "guitar", Instrument.FiveFretGuitar), ("Bass", "bass", Instrument.FiveFretBass),
            ("Drums", "drums", Instrument.FiveFretRhythm), ("Vocals", "vocals", Instrument.Keys),
        };
        // The songs' diff runs 0-6, shown as 1-7 dots; a 7 is all red
        private const int DOTS = 7;
        private const int TOP_TIER = 6;
        private static readonly Color TopDot = new(1f, 0.302f, 0.369f); // #ff4d5e
        private static readonly Color EmptyDot = new(0.227f, 0.267f, 0.459f); // #3a4475
        private static readonly Color Rule = new(0.627f, 0.686f, 1f, 0.08f);  // the boards' 12% blue line, in linear

        private RawImage _cover;
        private TextMeshProUGUI _kicker, _title, _artist, _meta;
        private readonly TextMeshProUGUI[] _partNames = new TextMeshProUGUI[4];
        private readonly Image[] _icons = new Image[4];
        private readonly Image[][] _dots = new Image[4][];

        /// <summary>Where the leaderboard goes: it takes the rest of the panel's height.</summary>
        public Transform BoardParent { get; private set; }

        public static LibrarySidebar Create(Transform sidebar, HelpBarButton play, RawImage cover, Action<int> pickPart)
        {
            var panel = Moonlight.Panel("Moonlight Sidebar", sidebar, 27, Moonlight.PanelFill);
            panel.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var rect = panel.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(0f, LibraryMoonlight.BOTTOM);
            rect.offsetMax = new Vector2(0f, -LibraryMoonlight.TOP);

            var column = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(30, 30, 30, 30);
            column.spacing = 24f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            var view = panel.gameObject.AddComponent<LibrarySidebar>();

            // Cover and the song's lines, bottom-aligned beside it
            var top = Box("Song", panel.transform, 192f);
            var row = top.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 24f;
            row.childAlignment = TextAnchor.LowerLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            var coverBox = Moonlight.Panel("Cover", top, 18, Moonlight.Raised);
            var coverSize = coverBox.gameObject.AddComponent<LayoutElement>();
            coverSize.minWidth = coverSize.preferredWidth = coverSize.minHeight = coverSize.preferredHeight = 192f;
            coverBox.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            view._cover = cover;
            cover.transform.SetParent(coverBox.transform, false);
            Stretch(cover.rectTransform);

            var lines = new GameObject("Lines", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            lines.transform.SetParent(top, false);
            lines.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var stack = lines.GetComponent<VerticalLayoutGroup>();
            stack.spacing = 6f;
            stack.childAlignment = TextAnchor.LowerLeft;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            view._kicker = Label(lines.transform, Moonlight.Body, 18f, Moonlight.Season, FontWeight.SemiBold);
            view._kicker.characterSpacing = 14f;
            view._kicker.gameObject.SetActive(false); // shown for season and featured songs
            view._title = Label(lines.transform, Moonlight.Heading, 31.5f, Moonlight.Text, FontWeight.Medium);
            view._title.textWrappingMode = TextWrappingModes.Normal;
            view._title.lineSpacing = -12f;
            view._title.maxVisibleLines = 2;
            view._artist = Label(lines.transform, Moonlight.Body, 22.5f, Moonlight.Soft, FontWeight.Regular);
            view._meta = Label(lines.transform, Moonlight.Body, 19.5f, Moonlight.Muted, FontWeight.Regular);

            // The prefab's Play button, now the board's full-width cream pill
            var playBox = Box("Play", panel.transform, 69f);
            play.transform.SetParent(playBox, false);
            Stretch((RectTransform) play.transform);

            // One row per part
            var parts = new GameObject("Parts", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<VerticalLayoutGroup>();
            parts.transform.SetParent(panel.transform, false);
            parts.spacing = 3f;
            parts.childControlWidth = parts.childControlHeight = true;
            parts.childForceExpandWidth = true;
            parts.childForceExpandHeight = false;
            for (int i = 0; i < Parts.Length; i++)
            {
                view.PartRow(parts.transform, i, pickPart);
            }

            var board = new GameObject("Top Scores", typeof(RectTransform), typeof(LayoutElement));
            board.transform.SetParent(panel.transform, false);
            board.GetComponent<LayoutElement>().flexibleHeight = 1f;
            var boardColumn = board.AddComponent<VerticalLayoutGroup>();
            boardColumn.childControlWidth = boardColumn.childControlHeight = true;
            boardColumn.childForceExpandWidth = true;
            boardColumn.childForceExpandHeight = false;
            view.BoardParent = board.transform;

            view.Clear();
            return view;
        }

        private void PartRow(Transform parent, int index, Action<int> pick)
        {
            var row = new GameObject(Parts[index].Name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().minHeight = row.GetComponent<LayoutElement>().preferredHeight = 60f;
            var hit = row.GetComponent<Image>();
            hit.color = Color.clear; // clickable: picks the leaderboard's instrument, like the old rings
            row.GetComponent<Button>().transition = Selectable.Transition.None;
            row.GetComponent<Button>().onClick.AddListener(() => pick(index));

            var line = new GameObject("Rule", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            line.transform.SetParent(row.transform, false);
            line.color = Rule;
            line.raycastTarget = false;
            line.rectTransform.anchorMin = Vector2.zero;
            line.rectTransform.anchorMax = new Vector2(1f, 0f);
            line.rectTransform.pivot = new Vector2(0.5f, 0f);
            line.rectTransform.sizeDelta = new Vector2(0f, 1.5f);

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(row.transform, false);
            icon.preserveAspect = true;
            icon.enabled = false;
            // Asynchronously: a blocking load can run inside the player loop and recurse it
            Addressables.LoadAssetAsync<Sprite>($"InstrumentIcons[{Parts[index].Icon}]").Completed += handle =>
            {
                if (icon == null || handle.Result == null) return;
                icon.sprite = handle.Result;
                icon.enabled = true;
            };
            _icons[index] = icon;
            icon.color = Moonlight.Text;
            icon.raycastTarget = false;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            icon.rectTransform.pivot = new Vector2(0f, 0.5f);
            icon.rectTransform.sizeDelta = new Vector2(33f, 33f);

            var name = Label(row.transform, Moonlight.Body, 22.5f, Moonlight.Text, FontWeight.SemiBold);
            name.text = Parts[index].Name;
            name.rectTransform.anchorMin = Vector2.zero;
            name.rectTransform.anchorMax = Vector2.one;
            name.rectTransform.offsetMin = new Vector2(51f, 0f);
            name.rectTransform.offsetMax = new Vector2(-180f, 0f);
            _partNames[index] = name;

            var dots = new GameObject("Dots", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<HorizontalLayoutGroup>();
            dots.transform.SetParent(row.transform, false);
            dots.spacing = 7.5f;
            dots.childAlignment = TextAnchor.MiddleRight;
            dots.childControlWidth = dots.childControlHeight = false;
            var dotsRect = (RectTransform) dots.transform;
            dotsRect.anchorMin = dotsRect.anchorMax = dotsRect.pivot = new Vector2(1f, 0.5f);
            dotsRect.sizeDelta = new Vector2(DOTS * 15f + (DOTS - 1) * 7.5f, 15f);
            _dots[index] = new Image[DOTS];
            for (int i = 0; i < DOTS; i++)
            {
                var dot = new GameObject("Dot", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                dot.transform.SetParent(dots.transform, false);
                dot.rectTransform.sizeDelta = new Vector2(15f, 15f);
                dot.raycastTarget = false;
                _dots[index][i] = dot;
            }
        }

        /// <summary>The small tag over a song's title: "SEASON 1 SONG" in pink, "FEATURED" in blue, or null.</summary>
        internal static string Kicker(SongEntry song, out Color color)
        {
            int season = OnlineSongs.SeasonOf(song?.ActualLocation, OnlineSongs.ServerNow);
            color = season > 0 ? Moonlight.Season : Moonlight.Featured;
            return season > 0 ? $"SEASON {season} SONG" : OnlineSongs.IsFeatured(song?.ActualLocation) ? "FEATURED" : null;
        }

        public void ShowSong(SongEntry song)
        {
            string kicker = Kicker(song, out var kickerColor);
            _kicker.gameObject.SetActive(kicker != null);
            _kicker.text = kicker ?? string.Empty;
            _kicker.color = kickerColor;
            _title.text = Plain(song.Name);
            _artist.text = Plain(song.Artist);
            var time = TimeSpan.FromMilliseconds(song.SongLengthMilliseconds);
            string length = time.Hours > 0 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
            string genre = song.Genre;
            string charter = song.Charter;
            _meta.text = Plain(string.Join(" · ", new[]
            {
                song.ParsedYear, string.IsNullOrEmpty(genre) ? null : genre, length,
                string.IsNullOrEmpty(charter) ? null : "charted by " + charter,
            }.Where(s => !string.IsNullOrEmpty(s))));
            for (int i = 0; i < Parts.Length; i++)
            {
                var values = song[Parts[i].Instrument];
                bool present = values.SubTracks > 0;
                _partNames[i].color = present ? Moonlight.Text : Moonlight.Faint;
                for (int d = 0; d < DOTS; d++)
                {
                    bool filled = present && d <= values.Intensity;
                    _dots[i][d].sprite = filled ? Moonlight.RoundedSprite(8) : Moonlight.Ring;
                    _dots[i][d].color = !filled ? EmptyDot : values.Intensity >= TOP_TIER ? TopDot : Moonlight.Moon;
                }
            }
        }

        /// <summary>Nothing selected (or a section header): the panel keeps its shape, empty.</summary>
        public void Clear()
        {
            _title.text = _artist.text = _meta.text = string.Empty;
            for (int i = 0; i < Parts.Length; i++)
            {
                _partNames[i].color = Moonlight.Faint;
                foreach (var dot in _dots[i])
                {
                    dot.sprite = Moonlight.Ring;
                    dot.color = EmptyDot;
                }
            }
        }

        /// <summary>Song fields are anyone's text: shown as typed, no rich text tags.</summary>
        private static string Plain(string text) => "<noparse>" + text?.Replace("</noparse>", "") + "</noparse>";

        private static RectTransform Box(string name, Transform parent, float height)
        {
            var box = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            box.transform.SetParent(parent, false);
            var size = box.GetComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = height;
            return (RectTransform) box.transform;
        }

        private static TextMeshProUGUI Label(Transform parent, TMP_FontAsset font, float size, Color color, FontWeight weight)
        {
            var label = Moonlight.Label("Text", parent, font != null ? font : Moonlight.Body, size, color);
            label.fontWeight = weight;
            return label;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}

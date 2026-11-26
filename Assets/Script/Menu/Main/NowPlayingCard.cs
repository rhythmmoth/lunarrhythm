using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Menu.Persistent;

namespace YARG.Menu.Main
{
    /// <summary>
    /// Title screen jukebox card in the top right: the track's cover, title and artist. A track linked to a game song
    /// gets a "Play this song" button that opens it in the library. Hidden while nothing plays.
    /// </summary>
    public class NowPlayingCard : MonoBehaviour
    {
        private const float HEIGHT = 102f;
        private const float PLAY_WIDTH = 176f;

        private RawImage _art;
        private TextMeshProUGUI _song;
        private TextMeshProUGUI _artist;
        private GameObject _play;
        private Jukebox.Track _shown;

        public static NowPlayingCard Create(Transform parent, TMP_FontAsset font, Action playSong)
        {
            // A Moonlight pill in the top right; the art is a circle at its left end
            var rect = Moonlight.Panel("Jukebox", parent, 51, Moonlight.PanelFill).rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-42f, -36f);
            rect.sizeDelta = new Vector2(380f, HEIGHT);

            var disc = Moonlight.Panel("Art", rect, 36, Moonlight.Raised);
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            disc.rectTransform.anchorMin = disc.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            disc.rectTransform.pivot = new Vector2(0f, 0.5f);
            disc.rectTransform.anchoredPosition = new Vector2(15f, 0f);
            disc.rectTransform.sizeDelta = new Vector2(72f, 72f);
            var art = new GameObject("Image", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            art.transform.SetParent(disc.rectTransform, false);
            art.raycastTarget = false;
            art.rectTransform.anchorMin = Vector2.zero;
            art.rectTransform.anchorMax = Vector2.one;
            art.rectTransform.offsetMin = art.rectTransform.offsetMax = Vector2.zero;

            var label = Line("Label", rect, font, 16f, Moonlight.Muted, FontStyles.Bold, 14f);
            label.text = "JUKEBOX";
            label.characterSpacing = 8f;

            var card = rect.gameObject.AddComponent<NowPlayingCard>();
            card._art = art;
            card._song = Line("Song", rect, font, 25f, Moonlight.Text, FontStyles.Bold, 34f);
            card._artist = Line("Artist", rect, font, 19f, Moonlight.Muted, FontStyles.Normal, 66f);
            card._play = PlayButton(rect, font, playSong);
            card.Show();
            return card;
        }

        private static TextMeshProUGUI Line(string name, RectTransform parent, TMP_FontAsset font, float size,
            Color color, FontStyles style, float top)
        {
            var text = MainMenuUI.Text(name, parent, font, size, color, style);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(104f, -top);
            rect.sizeDelta = new Vector2(240f, size * 1.25f);
            return text;
        }

        /// <summary>A cream pill at the card's right end.</summary>
        private static GameObject PlayButton(RectTransform card, TMP_FontAsset font, Action playSong)
        {
            var pill = Moonlight.Panel("Play this song", card, 26, Moonlight.Moon);
            pill.raycastTarget = true;
            var rect = pill.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-24f, 0f);
            rect.sizeDelta = new Vector2(PLAY_WIDTH - 32f, 52f);
            var button = pill.gameObject.AddComponent<Button>();
            button.targetGraphic = pill;
            button.onClick.AddListener(() => playSong?.Invoke());

            var text = MainMenuUI.Text("Text", rect, font, 19f, Moonlight.Night, FontStyles.Bold);
            text.text = "Play this song";
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            return pill.gameObject;
        }

        private void OnEnable()
        {
            MusicPlayer.NowPlayingChanged += Show;
            Show();
        }

        private void OnDisable()
        {
            MusicPlayer.NowPlayingChanged -= Show;
        }

        private void OnDestroy()
        {
            SetArt(null);
        }

        private void Show()
        {
            if (_song == null)
            {
                return; // AddComponent runs OnEnable before Create has built the card
            }

            var track = MusicPlayer.CurrentTrack;
            // Keep the panel on the image so it hides with the card
            GetComponent<Image>().enabled = track != null;
            foreach (Transform child in transform)
            {
                child.gameObject.SetActive(track != null);
            }
            if (track == null)
            {
                _shown = null;
                return;
            }

            bool linked = MusicPlayer.NowPlaying != null;
            _play.SetActive(linked);
            if (track == _shown)
            {
                return;
            }
            _shown = track;
            _song.text = track.Title;
            _artist.text = track.Artist;
            // Fit the card to the longest line, within limits so long titles still get cut off
            float text = Mathf.Clamp(Mathf.Max(_song.GetPreferredValues(track.Title).x,
                _artist.GetPreferredValues(track.Artist).x, 150f), 150f, 330f);
            _song.rectTransform.sizeDelta = new Vector2(text, _song.rectTransform.sizeDelta.y);
            _artist.rectTransform.sizeDelta = new Vector2(text, _artist.rectTransform.sizeDelta.y);
            // Extra room on the right so text clears the pill's rounded end
            var rect = (RectTransform) transform;
            rect.sizeDelta = new Vector2(104f + text + 44f + (linked ? PLAY_WIDTH : 0f), HEIGHT);
            SetArt(LoadCover(track.CoverPath));
        }

        private static Texture2D LoadCover(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }
            var texture = new Texture2D(2, 2);
            if (texture.LoadImage(File.ReadAllBytes(path)))
            {
                return texture;
            }
            Destroy(texture);
            return null;
        }

        private void SetArt(Texture2D texture)
        {
            if (_art == null)
            {
                return;
            }

            if (_art.texture != null)
            {
                Destroy(_art.texture);
            }

            _art.texture = texture;
            // No cover: the disc behind shows through as a plain circle
            _art.color = texture != null ? Color.white : Color.clear;
        }
    }
}

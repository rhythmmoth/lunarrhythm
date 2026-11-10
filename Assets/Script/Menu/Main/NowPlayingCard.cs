using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Song;
using YARG.Helpers.Extensions;
using YARG.Menu.Persistent;

namespace YARG.Menu.Main
{
    /// <summary>Title screen card in the top right showing the menu music's song and album art.</summary>
    public class NowPlayingCard : MonoBehaviour
    {
        private static readonly Color NoArt = new(1f, 1f, 1f, 0.08f);

        private RawImage _art;
        private TextMeshProUGUI _song;
        private TextMeshProUGUI _artist;
        private SongEntry _shown;

        public static NowPlayingCard Create(Transform parent, TMP_FontAsset font, Sprite panel)
        {
            var rect = MainMenuUI.Panel("Now Playing", parent, panel);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-40f, -36f);
            rect.sizeDelta = new Vector2(380f, 104f);

            var art = new GameObject("Art", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            art.transform.SetParent(rect, false);
            art.raycastTarget = false;
            art.rectTransform.anchorMin = art.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            art.rectTransform.pivot = new Vector2(0f, 0.5f);
            art.rectTransform.anchoredPosition = new Vector2(16f, 0f);
            art.rectTransform.sizeDelta = new Vector2(72f, 72f);

            var label = Line("Label", rect, font, 15f, MainMenuUI.Accent, FontStyles.Bold, 16f);
            label.text = "NOW PLAYING";
            label.characterSpacing = 4f;

            var card = rect.gameObject.AddComponent<NowPlayingCard>();
            card._art = art;
            card._song = Line("Song", rect, font, 26f, Color.white, FontStyles.Bold, 36f);
            card._artist = Line("Artist", rect, font, 20f, new Color(0.72f, 0.7f, 0.84f), FontStyles.Normal, 66f);
            card.Show();
            return card;
        }

        private static TextMeshProUGUI Line(string name, RectTransform parent, TMP_FontAsset font, float size,
            Color color, FontStyles style, float top)
        {
            var text = MainMenuUI.Text(name, parent, font, size, color, style);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(104f, -top - size * 1.25f);
            rect.offsetMax = new Vector2(-16f, -top);
            return text;
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

            var song = MusicPlayer.NowPlaying;
            // Keep the panel on the image so it hides with the card
            GetComponent<Image>().enabled = song != null;
            foreach (Transform child in transform)
            {
                child.gameObject.SetActive(song != null);
            }

            if (song == null || song == _shown)
            {
                return;
            }

            _shown = song;
            _song.text = song.Name;
            _artist.text = song.Artist;
            // Fit the card to the longest line, within limits so long titles still get cut off
            float text = Mathf.Max(_song.GetPreferredValues(song.Name).x, _artist.GetPreferredValues(song.Artist).x, 150f);
            var rect = (RectTransform) transform;
            rect.sizeDelta = new Vector2(Mathf.Clamp(104f + text + 20f, 280f, 480f), rect.sizeDelta.y);
            SetArt(null);
            LoadArt(song).Forget();
        }

        private async UniTaskVoid LoadArt(SongEntry song)
        {
            Texture2D texture = null;
            // Dispose on this method's own path: the image is backed by native memory
            using var image = await UniTask.RunOnThreadPool(song.LoadAlbumData);
            if (image != null)
            {
                texture = image.LoadTexture(false);
            }

            if (this == null || _shown != song)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
                return;
            }

            SetArt(texture);
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
            _art.uvRect = new Rect(0f, 0f, 1f, -1f);
            _art.color = texture != null ? Color.white : NoArt;
        }
    }
}

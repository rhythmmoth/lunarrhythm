using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Song;

namespace YARG.Menu.Main
{
    /// <summary>
    /// The title screen's scrolling news line. The text comes from the online song database
    /// (edited in the songdb tool); the bar hides itself when there's none.
    /// </summary>
    public class NewsTicker : MonoBehaviour
    {
        private const float SPEED = 90f; // canvas units per second

        private RectTransform _viewport;
        private RectTransform _textRect;
        private TextMeshProUGUI _text;
        private string _raw;

        public static NewsTicker Create(Transform parent, TMP_FontAsset font, Sprite panel)
        {
            // A pill at the bottom left, right above the help bar
            var rect = Moonlight.Panel("News", parent, 33, Moonlight.PanelFill).rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(42f, 105f);
            rect.sizeDelta = new Vector2(900f, 66f);

            var label = Moonlight.Label("Label", rect, Moonlight.Heading ? Moonlight.Heading : font, 16f, Moonlight.Moon);
            label.text = "NEWS";
            label.characterSpacing = 12f;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.offsetMin = new Vector2(28f, 0f);
            labelRect.offsetMax = new Vector2(110f, 0f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(rect, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(120f, 0f);
            viewport.offsetMax = new Vector2(-28f, 0f);

            var text = MainMenuUI.Text("Text", viewport, font, 23f, Moonlight.Soft, FontStyles.Normal);
            var textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(0f, 1f);
            textRect.pivot = new Vector2(0f, 0.5f);

            var ticker = rect.gameObject.AddComponent<NewsTicker>();
            ticker._viewport = viewport;
            ticker._textRect = textRect;
            ticker._text = text;
            ticker.Apply(OnlineSongs.News);
            return ticker;
        }

        private void Apply(string raw)
        {
            _raw = raw;
            // One line: newlines from the tool become separators
            string news = raw.Trim().Replace("\r", "").Replace("\n", "     •     ");
            bool visible = news.Length > 0;
            // Stay active so Update keeps watching: the menu opens before the online sync has read the news
            GetComponent<Image>().enabled = visible;
            foreach (Transform child in transform)
            {
                child.gameObject.SetActive(visible);
            }

            _text.text = news;
            _textRect.sizeDelta = new Vector2(visible ? _text.GetPreferredValues(news).x : 0f, 0f);
            _textRect.anchoredPosition = Vector2.zero;
        }

        private void Update()
        {
            if (!ReferenceEquals(OnlineSongs.News, _raw))
            {
                Apply(OnlineSongs.News);
            }

            // Start at the left so it's readable right away, then loop in from the right edge
            var pos = _textRect.anchoredPosition;
            pos.x -= SPEED * Time.unscaledDeltaTime;
            if (pos.x < -_textRect.sizeDelta.x)
            {
                pos.x = _viewport.rect.width;
            }
            _textRect.anchoredPosition = pos;
        }
    }
}

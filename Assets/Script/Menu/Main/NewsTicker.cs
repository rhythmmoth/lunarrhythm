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
            var rect = MainMenuUI.Panel("News", parent, panel);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(60f, 95f);   // sits right above the help bar
            rect.offsetMax = new Vector2(-140f, 147f); // leaves room for the Discord button

            var label = MainMenuUI.Text("Label", rect, font, 22f, MainMenuUI.Accent, FontStyles.Bold);
            label.text = "NEWS";
            label.alignment = TextAlignmentOptions.Center;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.offsetMin = new Vector2(16f, 0f);
            labelRect.offsetMax = new Vector2(100f, 0f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(rect, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(112f, 0f);
            viewport.offsetMax = new Vector2(-16f, 0f);

            var text = MainMenuUI.Text("Text", viewport, font, 24f, new Color(0.9f, 0.89f, 0.96f), FontStyles.Normal);
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

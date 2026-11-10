using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YARG.Menu.Main
{
    /// <summary>Builds the plain UI pieces the title screen widgets are made of.</summary>
    internal static class MainMenuUI
    {
        public static readonly Color PanelColor = new(0.05f, 0.04f, 0.11f, 0.78f);
        public static readonly Color Accent = new(0.8f, 0.68f, 1f); // light purple: highlight bars, labels

        public static RectTransform Panel(string name, Transform parent, Sprite sprite)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = PanelColor;
            image.raycastTarget = false;
            return image.rectTransform;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, float size, Color color,
            FontStyles style)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.Left;
            text.raycastTarget = false;
            return text;
        }
    }
}

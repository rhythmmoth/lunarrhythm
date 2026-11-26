using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Menu.Settings;

namespace YARG.Menu.Dialogs
{
    /// <summary>
    /// The Moonlight board's dialogs over YARG's prefabs: a dark card, an Unbounded title and pill buttons
    /// (cream to go ahead, outlined to back out, red to delete or leave). Message and rename dialogs also get
    /// the board's compact card, sized to their text; the bigger dialogs keep their layout.
    /// </summary>
    public class DialogMoonlight : MonoBehaviour
    {
        private const float WIDTH = 584f;
        private const float PAD = 36f;
        private const float GAP = 21f;
        private const float BUTTON = 66f;
        private const float TITLE = 45f;

        // Opaque, and a heavier dim than the board: the game blends in linear space, where bright text shows through
        private static readonly Color Card = new(6f / 255f, 10f / 255f, 28f / 255f, 1f);
        private static readonly Color Field = new(13f / 255f, 19f / 255f, 48f / 255f);
        private static readonly Color Ink = new(11f / 255f, 16f / 255f, 38f / 255f);
        private static readonly Color Edge = new(160f / 255f, 175f / 255f, 1f, 0.35f);

        private Dialog _dialog;
        private CanvasGroup _group;
        private RectTransform _base, _content, _buttons;
        private TextMeshProUGUI _message;
        private bool _compact, _rename, _shown;
        private string _title;
        private readonly Dictionary<ColoredButton, Image> _edges = new();

        public static void Attach(Dialog dialog)
        {
            // The fonts come from the main menu; a dialog before it (a startup error) keeps YARG's look
            if (Moonlight.Body != null && dialog.GetComponent<DialogMoonlight>() == null)
            {
                dialog.gameObject.AddComponent<DialogMoonlight>();
            }
        }

        private void Awake()
        {
            _dialog = GetComponent<Dialog>();
            // Hidden until the first restyle, so YARG's look never shows for a frame
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            var tint = transform.Find("Tint")?.GetComponent<Image>();
            if (tint != null) tint.color = new Color(0.008f, 0.02f, 0.063f, 0.94f);

            _base = (RectTransform) transform.Find("Base");
            if (_base == null) return;
            var card = _base.GetComponent<Image>();
            if (card != null)
            {
                card.sprite = Moonlight.RoundedSprite(33);
                card.type = Image.Type.Sliced;
                card.color = Card;
            }
            _content = (RectTransform) _base.Find("Content");
            _buttons = (RectTransform) _base.Find("Dialog Button Container");

            var title = _dialog.Title;
            title.font = Moonlight.Heading != null ? Moonlight.Heading : title.font;
            title.fontSize = 30f;
            title.fontWeight = FontWeight.Medium;
            title.fontStyle = FontStyles.Normal;
            title.alignment = TextAlignmentOptions.MidlineLeft;

            if (_buttons != null)
            {
                _buttons.sizeDelta = new Vector2(_buttons.sizeDelta.x, BUTTON);
                var row = _buttons.GetComponent<HorizontalLayoutGroup>();
                if (row != null) row.spacing = 15f;
            }

            // The board's compact card: only the plain message and the rename dialog
            _rename = _dialog is RenameDialog;
            _compact = _content != null && (_dialog.GetType() == typeof(MessageDialog) || _rename);
            if (!_compact) return;

            _base.anchorMin = _base.anchorMax = _base.pivot = new Vector2(0.5f, 0.5f);
            _base.anchoredPosition = Vector2.zero;
            if (_dialog is MessageDialog message)
            {
                _message = message.Message;
                _message.font = Moonlight.Body;
                _message.fontSize = 22.5f;
                _message.fontStyle = FontStyles.Normal;
                _message.lineSpacing = 28f; // Barlow's 1.2 to the board's 1.5
                _message.textWrappingMode = TextWrappingModes.Normal;
                _message.alignment = TextAlignmentOptions.TopLeft;
            }
            if (_rename) StyleField();
        }

        /// <summary>The rename field's label for the next rename dialog only ("Code" when joining a lobby).</summary>
        public static string NextFieldLabel;

        private void StyleField()
        {
            var holder = (RectTransform) _content.Find("InputField");
            var field = holder != null ? holder.Find("Field") : null;
            if (field == null) return;
            Top(holder, 0f, 28f + 9f + BUTTON);

            var name = Moonlight.Label("Name", holder, Moonlight.Body, 19.5f, Moonlight.Muted);
            name.text = NextFieldLabel ?? "Name";
            NextFieldLabel = null;
            Top(name.rectTransform, 0f, 28f);

            var rect = (RectTransform) field;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, BUTTON);
            var image = field.GetComponent<Image>();
            image.sprite = Moonlight.RoundedSprite(18);
            image.type = Image.Type.Sliced;
            image.color = Field;
            var edge = Moonlight.Panel("Edge", field, 18, Moonlight.Moon);
            edge.sprite = Moonlight.OutlineSprite(18);
            Stretch(edge.rectTransform);

            var viewport = (RectTransform) field.Find("Viewport");
            if (viewport != null)
            {
                viewport.offsetMin = new Vector2(21f, 0f);
                viewport.offsetMax = new Vector2(-21f, 0f);
            }
            foreach (var text in field.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.font = Moonlight.Body;
                text.fontSize = 24f;
                text.fontStyle = FontStyles.Normal;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.color = text.name == "Placeholder" ? Moonlight.Faint : Moonlight.Text;
            }
        }

        private void LateUpdate()
        {
            var title = _dialog.Title;
            if (title.text != _title)
            {
                _title = SettingsMoonlight.Sentence(title.text ?? "");
                title.text = _title;
            }
            title.color = Moonlight.Text;
            if (_message != null) _message.color = Moonlight.Soft;

            StyleButtons();
            if (_compact) Layout();
            if (_shown) return;
            _shown = true;
            Moonrise.Grow(_group, _base);
        }

        private void Layout()
        {
            float inner = WIDTH - PAD * 2f;
            float y = PAD;
            Top((RectTransform) _dialog.Title.transform, y, TITLE, PAD);
            y += TITLE + GAP;

            float content = _message != null
                ? _message.GetPreferredValues(_message.text ?? "", inner, 0f).y
                : 28f + 9f + BUTTON;
            Top(_content, y, content, PAD);
            y += content + GAP;

            if (_buttons != null)
            {
                Top(_buttons, y, BUTTON, PAD);
                y += BUTTON;
            }
            _base.sizeDelta = new Vector2(WIDTH, y + PAD);
        }

        private void StyleButtons()
        {
            if (_buttons == null) return;
            var buttons = _buttons.GetComponentsInChildren<ColoredButton>(true);
            foreach (var button in buttons)
            {
                // Roles come from the localisation key, which AddDialogButton keeps as the object's name
                string key = button.name;
                bool back = key.EndsWith(".Cancel") || key.EndsWith(".Close") || key.EndsWith(".Back");
                bool danger = key.EndsWith(".Delete") || key.EndsWith(".Remove") || key.EndsWith(".Leave");
                bool single = buttons.Length == 1;
                if (single && key == "Menu.Common.Close" && button.Text.text != "Got it") button.Text.text = "Got it";
                Style(button, single ? 0 : danger ? 2 : back ? 1 : 0);
            }
        }

        // 0 = cream, 1 = outlined, 2 = red
        private void Style(ColoredButton button, int role)
        {
            var image = button.GetComponentInChildren<Button>(true)?.GetComponent<Image>();
            if (image == null) return;
            bool on = image.GetComponent<Button>().interactable;

            if (!_edges.TryGetValue(button, out var edge))
            {
                var rect = image.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                image.sprite = Moonlight.RoundedSprite(33);
                image.type = Image.Type.Sliced;

                var outline = image.transform.Find("Selection Outline")?.GetComponent<Image>();
                if (outline != null)
                {
                    outline.sprite = Moonlight.OutlineSprite(39);
                    outline.type = Image.Type.Sliced;
                    outline.color = Moonlight.Moon;
                }

                edge = Moonlight.Panel("Edge", image.transform, 33, Edge);
                edge.sprite = Moonlight.OutlineSprite(33);
                Stretch(edge.rectTransform);
                edge.transform.SetAsFirstSibling();
                _edges[button] = edge;

                var text = button.Text;
                text.font = Moonlight.Body;
                text.fontSize = 22.5f;
                text.fontWeight = FontWeight.Bold;
                text.fontStyle = FontStyles.Normal;
                text.text = SettingsMoonlight.Sentence(text.text);
            }

            var fill = role == 0 ? Moonlight.Moon
                : role == 2 ? Moonlight.OverDark(Moonlight.Danger, 0.14f)
                : Color.clear;
            var ink = role == 0 ? Ink : role == 2 ? Moonlight.Danger : Moonlight.Text;
            if (!on)
            {
                fill.a *= 0.4f;
                ink.a = 0.4f;
            }
            if (image.color != fill) image.color = fill;
            if (button.Text.color != ink) button.Text.color = ink;
            edge.enabled = role == 1;
        }

        private static void Top(RectTransform rect, float y, float height, float inset = 0f)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(inset, -y - height);
            rect.offsetMax = new Vector2(-inset, -y);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}

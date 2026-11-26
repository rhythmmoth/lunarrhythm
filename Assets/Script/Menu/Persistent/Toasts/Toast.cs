using System;
using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YARG.Menu.Persistent
{
    public class Toast : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private const float FADE_DURATION = 0.25f;
        private const float HOVER_FADE = 0.98f;

        private const float SHRINK_DURATION = 0.3f;

        private const float HEIGHT = 78f;
        private const float GAP = 18f;
        private const float MAX_TEXT = 780f;

        [SerializeField]
        private Image _icon;
        [SerializeField]
        private TextMeshProUGUI _title;
        [SerializeField]
        private TextMeshProUGUI _message;
        [SerializeField]
        private Image _background;
        [SerializeField]
        private CanvasGroup _canvasGroup;

        private bool _canInteract;
        private Coroutine _coroutine;
        private Action _onClick;

        public void Initialize(string type, string message, Sprite icon, Color color, Action onClick)
        {
            // The board's toast: a dark pill with a coloured line icon and one line of text
            _background.sprite = Moonlight.RoundedSprite(39);
            _background.type = Image.Type.Sliced;
            _background.color = new Color(6f / 255f, 10f / 255f, 28f / 255f, 0.94f);
            _background.rectTransform.offsetMin = new Vector2(0f, GAP); // the gap to the next toast is inside it
            _background.rectTransform.offsetMax = Vector2.zero;

            _title.gameObject.SetActive(false);
            _icon.sprite = icon;
            _icon.color = color;
            _icon.rectTransform.anchoredPosition = new Vector2(21f, 0f);
            _icon.rectTransform.sizeDelta = new Vector2(30f, 30f);

            if (Moonlight.Body != null) _message.font = Moonlight.Body;
            _message.fontSize = 22.5f;
            _message.fontStyle = FontStyles.Normal;
            _message.color = Moonlight.Text;
            _message.text = message;
            _message.alignment = TextAlignmentOptions.MidlineLeft;
            _message.textWrappingMode = TextWrappingModes.NoWrap; // measure the single line first
            float width = Mathf.Min(_message.GetPreferredValues(message).x, MAX_TEXT);
            _message.textWrappingMode = TextWrappingModes.Normal;
            float height = Mathf.Max(HEIGHT, _message.GetPreferredValues(message, width, 0f).y + 24f);
            var line = _message.GetComponent<LayoutElement>();
            if (line == null) line = _message.gameObject.AddComponent<LayoutElement>();
            line.minHeight = line.preferredHeight = height;
            GetComponent<VerticalLayoutGroup>().padding = new RectOffset(69, 27, 0, (int) GAP);
            ((RectTransform) transform).sizeDelta = new Vector2(width + 69f + 27f, height + GAP);

            _onClick = onClick;

            _coroutine = StartCoroutine(ToastStartCoroutine());
        }

        private IEnumerator ToastStartCoroutine()
        {
            _canInteract = false;

            // Fade in
            _canvasGroup.alpha = 0f;
            yield return _canvasGroup
                .DOFade(1f, 0.25f)
                .SetUpdate(true)
                .WaitForCompletion();

            _canInteract = true;

            // Wait
            yield return new WaitForSecondsRealtime(5f);

            // Toast
            yield return ToastEndCoroutine();
        }

        private IEnumerator ToastEndCoroutine()
        {
            _canInteract = false;

            // Fade out
            yield return _canvasGroup
                .DOFade(0f, FADE_DURATION)
                .SetUpdate(true)
                .WaitForCompletion();

            var parentRect = transform.parent.GetComponent<RectTransform>();

            // Smoothly move all of the next notifications up
            // The spacing is embedded into the toast so we don't have to worry about that
            yield return transform
                .DOScaleY(0f, SHRINK_DURATION)
                .SetUpdate(true)
                .OnUpdate(() =>
                {
                    // Slow, but not much we can do
                    LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
                })
                .WaitForCompletion();

            Destroy(gameObject);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_canInteract)
            {
                return;
            }

            _onClick?.Invoke();

            StopCoroutine(_coroutine);
            _coroutine = StartCoroutine(ToastEndCoroutine());
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_canInteract)
            {
                return;
            }

            _canvasGroup.DOComplete();
            _canvasGroup.DOFade(HOVER_FADE, FADE_DURATION);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_canInteract)
            {
                return;
            }

            _canvasGroup.DOComplete();
            _canvasGroup.DOFade(1f, FADE_DURATION);
        }
    }
}
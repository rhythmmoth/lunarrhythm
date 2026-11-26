using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Menu;

namespace YARG
{
    /// <summary>
    /// The Moonlight board's loading screen: the logo smaller and higher, and under it a dark pill with a progress
    /// ring, the step in sentence case and one line of detail. YARG's two text lines stay as the source, hidden.
    /// </summary>
    public class LoadingMoonlight : MonoBehaviour
    {
        /// <summary>0–1 for the ring, or below 0 for a spinning arc when the length of the work is unknown.</summary>
        public static float Progress = -1f;

        private TextMeshProUGUI _phrase, _sub, _title, _detail;
        private Image _ring;
        private RectTransform _pill;
        private string _shownPhrase, _shownSub;

        public static void Attach(LoadingScreen screen)
        {
            var view = screen.gameObject.AddComponent<LoadingMoonlight>();
            view._phrase = screen.LoadingPhrase;
            view._sub = screen.SubPhrase;
            view.Build();
        }

        private void Build()
        {
            // The crescent wordmark (board NextLogo) in place of the old full-frame splash art, above the status pill
            var logo = (RectTransform) transform.Find("Logo");
            if (logo != null)
            {
                logo.anchorMin = logo.anchorMax = new Vector2(0.5f, 0.5f);
                logo.anchoredPosition = new Vector2(0f, 90f);
                Moonlight.ShowWordmark(logo.GetComponent<Image>(), 860f);
            }

            _phrase.enabled = false;
            _sub.enabled = false;
            var font = _sub.font; // Barlow; the main menu (which sets Moonlight's fonts) hasn't loaded yet

            _pill = Moonlight.Panel("Moonlight Status", transform, 48, Moonlight.PanelFill).rectTransform;
            _pill.anchorMin = _pill.anchorMax = _pill.pivot = new Vector2(0.5f, 1f);
            _pill.anchoredPosition = new Vector2(0f, -609f);

            var track = Ring(Moonlight.Keycap);
            _ring = Ring(Moonlight.Moon);
            _ring.type = Image.Type.Filled;
            _ring.fillMethod = Image.FillMethod.Radial360;
            _ring.fillOrigin = (int) Image.Origin360.Top;
            _ring.fillClockwise = true;

            _title = Moonlight.Label("Title", _pill, font, 27f, Moonlight.Text);
            _title.fontWeight = FontWeight.Bold;
            _detail = Moonlight.Label("Detail", _pill, font, 21f, Moonlight.Muted);
        }

        private Image Ring(Color color)
        {
            var image = new GameObject("Ring", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(_pill, false);
            image.sprite = Moonlight.ThinRing;
            image.color = color;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(21f, 0f);
            rect.sizeDelta = new Vector2(60f, 60f);
            return image;
        }

        private void LateUpdate()
        {
            if (_phrase.text != _shownPhrase || _sub.text != _shownSub)
            {
                _shownPhrase = _phrase.text;
                _shownSub = _sub.text;
                _title.text = Tidy(_shownPhrase);
                // One line of detail: YARG's sub text is often several "Label: value" lines
                _detail.text = Tidy(Regex.Replace(_shownSub ?? "", @"\s*\n\s*", " · "));
                Layout();
            }

            var ring = (RectTransform) _ring.transform;
            if (Progress >= 0f)
            {
                _ring.fillAmount = Mathf.Clamp01(Progress);
                ring.localEulerAngles = Vector3.zero;
            }
            else
            {
                _ring.fillAmount = 0.25f;
                ring.pivot = new Vector2(0.5f, 0.5f);
                ring.anchoredPosition = new Vector2(51f, 0f);
                ring.Rotate(0f, 0f, -360f * Time.unscaledDeltaTime);
            }
        }

        private void Layout()
        {
            bool detail = !string.IsNullOrEmpty(_detail.text);
            float width = Mathf.Max(_title.GetPreferredValues(_title.text).x,
                detail ? _detail.GetPreferredValues(_detail.text).x : 0f);
            width = Mathf.Min(width, 1100f);
            Place(_title.rectTransform, detail ? 14f : 0f, width, 33f);
            Place(_detail.rectTransform, -16f, width, 27f);
            _detail.gameObject.SetActive(detail);
            _pill.sizeDelta = new Vector2(21f + 60f + 27f + width + 39f, 96f);
        }

        private static void Place(RectTransform rect, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(21f + 60f + 27f, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        // "LOADING SONGS..." → "Loading songs"; "Folders Scanned: 40" → "Folders scanned: 40"
        private static string Tidy(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Trim().TrimEnd('.', '…').Trim();
            return Menu.Settings.SettingsMoonlight.Sentence(text);
        }
    }
}
